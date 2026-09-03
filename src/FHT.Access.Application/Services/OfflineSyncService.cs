using System.Text.Json;
using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class OfflineSyncService
{
    public const int MaxAttemptsBeforeDrop = 12;
    public const int BatchSize = 40;

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
        var eventItems = await _pendingSync
            .GetPendingByKindAsync(AccessEventService.SyncKindAccessEvent, BatchSize, cancellationToken)
            .ConfigureAwait(false);
        if (eventItems.Count == 0)
            return 0;

        var dtos = new List<AccessEventDto>(eventItems.Count);
        var mapped = new List<(PendingSync Item, AccessEvent Event)>(eventItems.Count);

        foreach (var item in eventItems)
        {
            if (item.Attempts >= MaxAttemptsBeforeDrop)
            {
                await DropPoisonAsync(item, "max_attempts", cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var accessEvent = JsonSerializer.Deserialize<AccessEvent>(item.PayloadJson, JsonOptions);
                if (accessEvent is null)
                {
                    await DropPoisonAsync(item, "Invalid payload", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                dtos.Add(ToDto(accessEvent));
                mapped.Add((item, accessEvent));
            }
            catch (Exception ex)
            {
                await _pendingSync.MarkAttemptAsync(item.Id, ex.Message, cancellationToken).ConfigureAwait(false);
                if (item.Attempts + 1 >= MaxAttemptsBeforeDrop)
                    await DropPoisonAsync(item, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        if (dtos.Count == 0)
            return 0;

        try
        {
            await _client
                .AcknowledgeEventsAsync(unitId, dtos, cancellationToken)
                .ConfigureAwait(false);

            // HTTP 2xx: API agora processa item a item — limpa o lote inteiro do outbox.
            foreach (var (item, accessEvent) in mapped)
            {
                await _pendingSync.RemoveAsync(item.Id, cancellationToken).ConfigureAwait(false);
                accessEvent.SyncStatus = SyncStatus.Synced;
                await _events.UpdateAsync(accessEvent, cancellationToken).ConfigureAwait(false);
            }

            var syncState = await _settings.GetSyncStateAsync(cancellationToken).ConfigureAwait(false);
            syncState.LastEventsSyncAt = DateTime.UtcNow;
            await _settings.SaveSyncStateAsync(syncState, cancellationToken).ConfigureAwait(false);

            return mapped.Count;
        }
        catch (Exception ex)
        {
            foreach (var (item, _) in mapped)
            {
                await _pendingSync.MarkAttemptAsync(item.Id, ex.Message, cancellationToken).ConfigureAwait(false);
                if (item.Attempts + 1 >= MaxAttemptsBeforeDrop || IsPermanentFailure(ex.Message))
                    await DropPoisonAsync(item, ex.Message, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task DropPoisonAsync(PendingSync item, string reason, CancellationToken ct)
    {
        await _pendingSync.RemoveAsync(item.Id, ct).ConfigureAwait(false);
        try
        {
            var accessEvent = JsonSerializer.Deserialize<AccessEvent>(item.PayloadJson, JsonOptions);
            if (accessEvent is not null)
            {
                accessEvent.SyncStatus = SyncStatus.Failed;
                await _events.UpdateAsync(accessEvent, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static bool IsPermanentFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        var m = message.ToLowerInvariant();
        return m.Contains("not_found")
            || m.Contains("validation")
            || m.Contains("uuid")
            || m.Contains("invalid");
    }

    private static AccessEventDto ToDto(AccessEvent e)
    {
        var deviceId = e.DeviceId;
        if (!string.IsNullOrWhiteSpace(deviceId) && !Guid.TryParse(deviceId, out _))
            deviceId = null;

        var occurred = e.OccurredAt.Kind switch
        {
            DateTimeKind.Utc => e.OccurredAt,
            DateTimeKind.Local => e.OccurredAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc)
        };

        return new AccessEventDto(
            e.Id,
            e.MemberId,
            e.Direction.ToString(),
            e.Status.ToString(),
            e.PassageConfirmed,
            occurred,
            e.Source,
            deviceId,
            e.DenialReason,
            e.AttemptId,
            e.VisitId);
    }
}
