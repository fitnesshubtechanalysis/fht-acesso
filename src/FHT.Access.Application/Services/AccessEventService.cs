using System.Text.Json;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class AccessEventService
{
    public const string SyncKindAccessEvent = "access_event";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAccessEventRepository _events;
    private readonly IPendingSyncRepository _pendingSync;

    public AccessEventService(
        IAccessEventRepository events,
        IPendingSyncRepository pendingSync)
    {
        _events = events;
        _pendingSync = pendingSync;
    }

    public async Task<AccessEvent> RecordAllowedAsync(
        Guid? memberId,
        AccessDirection direction,
        string source,
        string? deviceId = null,
        bool passageConfirmed = false,
        CancellationToken cancellationToken = default)
    {
        var accessEvent = CreateEvent(
            memberId,
            direction,
            AccessEventStatus.Allowed,
            source,
            deviceId,
            passageConfirmed,
            denialReason: null);

        await PersistAndEnqueueAsync(accessEvent, cancellationToken).ConfigureAwait(false);
        return accessEvent;
    }

    public async Task<AccessEvent> RecordDeniedAsync(
        Guid? memberId,
        AccessDirection direction,
        string source,
        string? deviceId = null,
        string? denialReason = null,
        CancellationToken cancellationToken = default)
    {
        var accessEvent = CreateEvent(
            memberId,
            direction,
            AccessEventStatus.Denied,
            source,
            deviceId,
            passageConfirmed: false,
            denialReason);

        await PersistAndEnqueueAsync(accessEvent, cancellationToken).ConfigureAwait(false);
        return accessEvent;
    }

    public async Task UpdatePassageAsync(
        AccessEvent accessEvent,
        bool passageConfirmed,
        CancellationToken cancellationToken = default)
    {
        accessEvent.PassageConfirmed = passageConfirmed;
        await _events.UpdateAsync(accessEvent, cancellationToken).ConfigureAwait(false);
    }

    private static AccessEvent CreateEvent(
        Guid? memberId,
        AccessDirection direction,
        AccessEventStatus status,
        string source,
        string? deviceId,
        bool passageConfirmed,
        string? denialReason) => new()
    {
        Id = Guid.NewGuid(),
        MemberId = memberId,
        Direction = direction,
        Status = status,
        PassageConfirmed = passageConfirmed,
        SyncStatus = SyncStatus.Pending,
        OccurredAt = DateTime.UtcNow,
        Source = source,
        DeviceId = deviceId,
        DenialReason = denialReason
    };

    public async Task EnqueueSyncAsync(AccessEvent accessEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessEvent);
        var pending = new PendingSync
        {
            Id = Guid.NewGuid(),
            Kind = SyncKindAccessEvent,
            PayloadJson = JsonSerializer.Serialize(accessEvent, JsonOptions),
            CreatedAt = DateTime.UtcNow,
            Attempts = 0
        };
        await _pendingSync.EnqueueAsync(pending, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistAndEnqueueAsync(AccessEvent accessEvent, CancellationToken cancellationToken)
    {
        await _events.AddAsync(accessEvent, cancellationToken).ConfigureAwait(false);

        var pending = new PendingSync
        {
            Id = Guid.NewGuid(),
            Kind = SyncKindAccessEvent,
            PayloadJson = JsonSerializer.Serialize(accessEvent, JsonOptions),
            CreatedAt = DateTime.UtcNow,
            Attempts = 0
        };

        await _pendingSync.EnqueueAsync(pending, cancellationToken).ConfigureAwait(false);
    }
}
