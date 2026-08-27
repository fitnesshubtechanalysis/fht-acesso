using System.Text.Json;
using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class OfflineSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IPendingSyncRepository _pendingSync;
    private readonly IAccessEventRepository _events;
    private readonly IGestaoAccessClient _client;
    private readonly ISettingsStore _settings;

    public OfflineSyncService(
        IPendingSyncRepository pendingSync,
        IAccessEventRepository events,
        IGestaoAccessClient client,
        ISettingsStore settings)
    {
        _pendingSync = pendingSync;
        _events = events;
        _client = client;
        _settings = settings;
    }

    public async Task<int> FlushAsync(string unitId, CancellationToken cancellationToken = default)
    {
        var pending = await _pendingSync.GetPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return 0;
        }

        var eventItems = pending
            .Where(p => string.Equals(p.Kind, AccessEventService.SyncKindAccessEvent, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (eventItems.Count == 0)
        {
            return 0;
        }

        var dtos = new List<AccessEventDto>(eventItems.Count);
        var mapped = new List<(PendingSync Item, AccessEvent? Event)>(eventItems.Count);

        foreach (var item in eventItems)
        {
            try
            {
                var accessEvent = JsonSerializer.Deserialize<AccessEvent>(item.PayloadJson, JsonOptions);
                if (accessEvent is null)
                {
                    await _pendingSync.MarkAttemptAsync(item.Id, "Invalid payload", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                dtos.Add(ToDto(accessEvent));
                mapped.Add((item, accessEvent));
            }
            catch (Exception ex)
            {
                await _pendingSync.MarkAttemptAsync(item.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        if (dtos.Count == 0)
        {
            return 0;
        }

        try
        {
            await _client.AcknowledgeEventsAsync(unitId, dtos, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            foreach (var (item, _) in mapped)
            {
                await _pendingSync.MarkAttemptAsync(item.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        foreach (var (item, accessEvent) in mapped)
        {
            await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);

            if (accessEvent is not null)
            {
                accessEvent.SyncStatus = SyncStatus.Synced;
                await _events.UpdateAsync(accessEvent, cancellationToken).ConfigureAwait(false);
            }
        }

        var syncState = await _settings.GetSyncStateAsync(cancellationToken).ConfigureAwait(false);
        syncState.LastEventsSyncAt = DateTime.UtcNow;
        await _settings.SaveSyncStateAsync(syncState, cancellationToken).ConfigureAwait(false);

        return mapped.Count;
    }

    private static AccessEventDto ToDto(AccessEvent e) => new(
        e.Id,
        e.MemberId,
        e.Direction.ToString(),
        e.Status.ToString(),
        e.PassageConfirmed,
        e.OccurredAt,
        e.Source,
        e.DeviceId,
        e.DenialReason,
        e.AttemptId,
        e.VisitId);
}
