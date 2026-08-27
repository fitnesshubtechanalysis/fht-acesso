using System.Collections.Concurrent;
using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class PassagePlan
{
    public required Guid AttemptId { get; init; }
    public required AccessDirection Direction { get; init; }
    public required PresenceStateKind PreviousStableState { get; init; }
    public required PresenceStateKind PendingState { get; init; }
}

public sealed class PresenceService
{
    private readonly IPresenceRepository _presence;
    private readonly IAccessAttemptRepository _attempts;
    private readonly IVisitRepository _visits;
    private readonly IPresenceCorrectionRepository _corrections;
    private readonly IAccessEventRepository _events;
    private readonly IDiagnosticLog? _log;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _personLocks = new();
    private readonly SemaphoreSlim _turnstileLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, DateTime> _cooldownUntil = new();

    public TimeSpan RecognitionCooldown { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan VisitMaxDuration { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Piloto: saída livre — presença estimada, sempre libera entrada.</summary>
    public bool EntryOnlyMode { get; set; } = true;

    public PresenceService(
        IPresenceRepository presence,
        IAccessAttemptRepository attempts,
        IVisitRepository visits,
        IPresenceCorrectionRepository corrections,
        IAccessEventRepository events,
        IDiagnosticLog? log = null)
    {
        _presence = presence;
        _attempts = attempts;
        _visits = visits;
        _corrections = corrections;
        _events = events;
        _log = log;
    }

    public bool IsTurnstileBusy => _turnstileLock.CurrentCount == 0;

    public async Task<PersonPresenceState> GetOrCreateAsync(
        Guid personId,
        string unitId,
        CancellationToken ct = default)
    {
        var existing = await _presence.GetAsync(personId, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var created = new PersonPresenceState
        {
            PersonId = personId,
            UnitId = unitId,
            State = PresenceStateKind.Outside,
            Version = 0,
            UpdatedAt = DateTime.UtcNow
        };
        await _presence.UpsertAsync(created, ct).ConfigureAwait(false);
        return created;
    }

    public async Task<(bool Allowed, string? BlockReason)> TryBeginRecognitionAsync(
        Guid personId,
        string unitId,
        CancellationToken ct = default)
    {
        var gate = await _personLocks.GetOrAdd(personId, _ => new SemaphoreSlim(1, 1))
            .WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        if (!gate)
            return (false, "Aguarde — processando acesso anterior.");

        try
        {
            if (_cooldownUntil.TryGetValue(personId, out var until) && until > DateTime.UtcNow)
                return (false, null);

            var p = await GetOrCreateAsync(personId, unitId, ct).ConfigureAwait(false);
            if (p.State is PresenceStateKind.EntryPending or PresenceStateKind.ExitPending)
                return (false, null);

            if (!EntryOnlyMode && p.State == PresenceStateKind.Unknown)
                return (false, "Estado de presença indefinido — procure a recepção.");

            if (!await _turnstileLock.WaitAsync(0, ct).ConfigureAwait(false))
                return (false, "Catraca aguardando passagem.");

            return (true, null);
        }
        finally
        {
            _personLocks.GetOrAdd(personId, _ => new SemaphoreSlim(1, 1)).Release();
        }
    }

    public void ReleaseTurnstileGateIfNotStarted()
    {
        try { _turnstileLock.Release(); } catch { /* not held */ }
    }

    public async Task<PassagePlan?> PlanPassageAsync(
        Guid personId,
        string unitId,
        string source,
        string? deviceId,
        string? turnstileSerial,
        CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(personId, unitId, ct).ConfigureAwait(false);
        var direction = EntryOnlyMode
            ? AccessDirection.Entry
            : p.State switch
            {
                PresenceStateKind.Outside => AccessDirection.Entry,
                PresenceStateKind.Inside => AccessDirection.Exit,
                _ => (AccessDirection?)null
            };
        if (direction is null)
            return null;

        var pending = direction == AccessDirection.Entry
            ? PresenceStateKind.EntryPending
            : PresenceStateKind.ExitPending;

        var attemptId = Guid.NewGuid();
        var idempotency = $"{personId:N}:{DateTime.UtcNow.Ticks}:{direction}";
        var attempt = new AccessAttemptRecord
        {
            Id = attemptId,
            PersonId = personId,
            UnitId = unitId,
            TurnstileSerial = turnstileSerial,
            RequestedDirection = direction.Value,
            Status = AccessAttemptStatus.Recognized,
            Source = source,
            DeviceId = deviceId,
            IdempotencyKey = idempotency,
            RecognizedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await _attempts.AddAsync(attempt, ct).ConfigureAwait(false);

        p.State = pending;
        p.PendingAttemptId = attemptId;
        p.Version++;
        p.UpdatedAt = DateTime.UtcNow;
        await _presence.UpsertAsync(p, ct).ConfigureAwait(false);

        attempt.Status = AccessAttemptStatus.Released;
        attempt.ReleasedAt = DateTime.UtcNow;
        await _attempts.UpdateAsync(attempt, ct).ConfigureAwait(false);

        _log?.Information(
            $"[Presence] Pending {pending} attempt={attemptId} person={personId} dir={direction}");

        return new PassagePlan
        {
            AttemptId = attemptId,
            Direction = direction.Value,
            PreviousStableState = pending == PresenceStateKind.EntryPending
                ? PresenceStateKind.Outside
                : PresenceStateKind.Inside,
            PendingState = pending
        };
    }

    public async Task<(Guid? VisitId, AccessEvent? Event)> ConfirmPassageAsync(
        Guid attemptId,
        bool passageConfirmed,
        string source,
        string? deviceId,
        CancellationToken ct = default)
    {
        var attempt = await _attempts.GetByIdAsync(attemptId, ct).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"Attempt {attemptId} not found.");

        if (attempt.Status is AccessAttemptStatus.PassageConfirmed or AccessAttemptStatus.TimedOut)
        {
            _log?.Information($"[Presence] Idempotent confirm attempt={attemptId} status={attempt.Status}");
            try { _turnstileLock.Release(); } catch { /* ignore */ }
            return (null, null);
        }

        var p = await _presence.GetAsync(attempt.PersonId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Presence missing.");

        if (!passageConfirmed)
        {
            return await TimeoutAttemptAsync(attempt, p, source, deviceId, ct).ConfigureAwait(false);
        }

        attempt.Status = AccessAttemptStatus.PassageConfirmed;
        attempt.PassageConfirmedAt = DateTime.UtcNow;
        await _attempts.UpdateAsync(attempt, ct).ConfigureAwait(false);

        Guid? visitId = null;
        AccessEvent? accessEvent;

        if (attempt.RequestedDirection == AccessDirection.Entry)
        {
            var visit = new VisitRecord
            {
                Id = Guid.NewGuid(),
                PersonId = attempt.PersonId,
                UnitId = attempt.UnitId,
                EntryAttemptId = attemptId,
                EnteredAt = DateTime.UtcNow,
                Status = VisitStatus.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _visits.AddAsync(visit, ct).ConfigureAwait(false);
            visitId = visit.Id;

            p.State = PresenceStateKind.Inside;
            p.ActiveVisitId = visit.Id;
            p.LastConfirmedDirection = AccessDirection.Entry;
            p.LastConfirmedAt = DateTime.UtcNow;

            accessEvent = await RecordEventAsync(
                attempt, AccessEventStatus.Allowed, true, source, deviceId, visitId, ct)
                .ConfigureAwait(false);
        }
        else
        {
            var visit = await _visits.GetOpenVisitForPersonAsync(attempt.PersonId, ct).ConfigureAwait(false);
            if (visit is not null)
            {
                visit.ExitAttemptId = attemptId;
                visit.ExitedAt = DateTime.UtcNow;
                visit.Status = VisitStatus.Closed;
                visit.UpdatedAt = DateTime.UtcNow;
                await _visits.UpdateAsync(visit, ct).ConfigureAwait(false);
                visitId = visit.Id;
            }

            p.State = PresenceStateKind.Outside;
            p.ActiveVisitId = null;
            p.LastConfirmedDirection = AccessDirection.Exit;
            p.LastConfirmedAt = DateTime.UtcNow;

            accessEvent = await RecordEventAsync(
                attempt, AccessEventStatus.Allowed, true, source, deviceId, visitId, ct)
                .ConfigureAwait(false);
        }

        p.PendingAttemptId = null;
        p.Version++;
        p.UpdatedAt = DateTime.UtcNow;
        await _presence.UpsertAsync(p, ct).ConfigureAwait(false);

        _cooldownUntil[attempt.PersonId] = DateTime.UtcNow.Add(RecognitionCooldown);
        try { _turnstileLock.Release(); } catch { /* ignore */ }

        _log?.Information(
            $"[Presence] Confirmed {attempt.RequestedDirection} person={attempt.PersonId} visit={visitId}");

        return (visitId, accessEvent);
    }

    private async Task<(Guid? VisitId, AccessEvent? Event)> TimeoutAttemptAsync(
        AccessAttemptRecord attempt,
        PersonPresenceState p,
        string source,
        string? deviceId,
        CancellationToken ct)
    {
        attempt.Status = AccessAttemptStatus.TimedOut;
        attempt.FailureReason = attempt.RequestedDirection == AccessDirection.Entry
            ? "Entrada liberada sem passagem"
            : "Saída liberada sem passagem";
        attempt.PassageConfirmedAt = null;
        await _attempts.UpdateAsync(attempt, ct).ConfigureAwait(false);

        p.State = attempt.RequestedDirection == AccessDirection.Entry
            ? PresenceStateKind.Outside
            : PresenceStateKind.Inside;
        p.PendingAttemptId = null;
        p.Version++;
        p.UpdatedAt = DateTime.UtcNow;
        await _presence.UpsertAsync(p, ct).ConfigureAwait(false);

        var accessEvent = await RecordEventAsync(
            attempt,
            AccessEventStatus.Allowed,
            passageConfirmed: false,
            source,
            deviceId,
            visitId: null,
            ct).ConfigureAwait(false);

        _cooldownUntil[attempt.PersonId] = DateTime.UtcNow.Add(RecognitionCooldown);
        try { _turnstileLock.Release(); } catch { /* ignore */ }

        _log?.Warning($"[Presence] Timeout {attempt.RequestedDirection} person={attempt.PersonId}");

        return (null, accessEvent);
    }

    private async Task<AccessEvent> RecordEventAsync(
        AccessAttemptRecord attempt,
        AccessEventStatus status,
        bool passageConfirmed,
        string source,
        string? deviceId,
        Guid? visitId,
        CancellationToken ct)
    {
        var ev = new AccessEvent
        {
            Id = Guid.NewGuid(),
            MemberId = attempt.PersonId,
            Direction = attempt.RequestedDirection,
            Status = status,
            PassageConfirmed = passageConfirmed,
            SyncStatus = SyncStatus.Pending,
            OccurredAt = DateTime.UtcNow,
            Source = source,
            DeviceId = deviceId,
            DenialReason = attempt.FailureReason,
            AttemptId = attempt.Id,
            VisitId = visitId
        };
        await _events.AddAsync(ev, ct).ConfigureAwait(false);
        attempt.AccessEventId = ev.Id;
        await _attempts.UpdateAsync(attempt, ct).ConfigureAwait(false);
        return ev;
    }

    public async Task CorrectPresenceAsync(
        Guid personId,
        string unitId,
        PresenceStateKind newState,
        string operatorId,
        string reason,
        CancellationToken ct = default)
    {
        var p = await GetOrCreateAsync(personId, unitId, ct).ConfigureAwait(false);
        var prev = p.State;
        var correction = new PresenceCorrectionRecord
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            UnitId = unitId,
            PreviousState = prev,
            NewState = newState,
            OperatorId = operatorId,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        };
        await _corrections.AddAsync(correction, ct).ConfigureAwait(false);

        if (prev == PresenceStateKind.Inside && newState == PresenceStateKind.Outside)
        {
            var open = await _visits.GetOpenVisitForPersonAsync(personId, ct).ConfigureAwait(false);
            if (open is not null)
            {
                open.Status = VisitStatus.Corrected;
                open.CloseReason = reason;
                open.ExitedAt = DateTime.UtcNow;
                open.UpdatedAt = DateTime.UtcNow;
                await _visits.UpdateAsync(open, ct).ConfigureAwait(false);
            }
        }

        p.State = newState;
        p.PendingAttemptId = null;
        p.ActiveVisitId = newState == PresenceStateKind.Inside ? p.ActiveVisitId : null;
        p.Version++;
        p.UpdatedAt = DateTime.UtcNow;
        await _presence.UpsertAsync(p, ct).ConfigureAwait(false);
        _log?.Information($"[Presence] Correction {prev}->{newState} person={personId} by={operatorId}");
    }

    public async Task ExpireStaleVisitsAsync(CancellationToken ct = default)
    {
        var open = await _visits.GetOpenVisitsAsync(ct).ConfigureAwait(false);
        var cutoff = DateTime.UtcNow - VisitMaxDuration;
        foreach (var v in open.Where(x => x.EnteredAt is not null && x.EnteredAt < cutoff))
        {
            v.Status = VisitStatus.AutoClosed;
            v.CloseReason = "visit_expired";
            v.ExitedAt = DateTime.UtcNow;
            v.UpdatedAt = DateTime.UtcNow;
            await _visits.UpdateAsync(v, ct).ConfigureAwait(false);

            var p = await _presence.GetAsync(v.PersonId, ct).ConfigureAwait(false);
            if (p is null) continue;
            p.State = PresenceStateKind.Outside;
            p.ActiveVisitId = null;
            p.Version++;
            p.UpdatedAt = DateTime.UtcNow;
            await _presence.UpsertAsync(p, ct).ConfigureAwait(false);
            _log?.Warning($"[Presence] Auto-closed visit={v.Id} person={v.PersonId}");
        }
    }
}
