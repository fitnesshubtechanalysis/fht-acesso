using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class AccessFlowResult
{
    public required AccessDecision Decision { get; init; }
    public AccessEvent? Event { get; init; }
    public PassageOutcome? Passage { get; init; }
    public AccessDirection Direction { get; init; } = AccessDirection.Entry;
    public Guid? AttemptId { get; init; }
    public Guid? VisitId { get; init; }
    public required string UiMessage { get; init; }
}

public sealed class AccessFlowService
{
    public const string SourceFace = "face";
    public const string SourceManual = "manual";

    private readonly RecognitionService _recognition;
    private readonly AccessEventService _events;
    private readonly TurnstileService _turnstile;
    private readonly PresenceService _presence;
    private readonly IGestaoAccessClient _gestao;
    private readonly IAccessDeviceContext _device;
    private readonly IMemberRepository _members;
    private readonly IDiagnosticLog? _log;

    private readonly AccessDecisionService _decisionService;

    public AccessFlowService(
        RecognitionService recognition,
        AccessEventService events,
        TurnstileService turnstile,
        PresenceService presence,
        IGestaoAccessClient gestao,
        IAccessDeviceContext device,
        IMemberRepository members,
        AccessDecisionService decisionService,
        IDiagnosticLog? log = null)
    {
        _recognition = recognition;
        _events = events;
        _turnstile = turnstile;
        _presence = presence;
        _gestao = gestao;
        _device = device;
        _members = members;
        _decisionService = decisionService;
        _log = log;
    }

    public TimeSpan PassageTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public string? DeviceId { get; set; }
    public string? OperatorId { get; set; }

    /// <summary>Piloto: saída livre — somente entrada usa facial.</summary>
    public bool EntryOnlyMode { get; set; } = true;

    private string UnitId => _device.UnitId?.Trim() ?? string.Empty;

    public Task<AccessFlowResult> ProcessEntryAsync(byte[] image, CancellationToken ct = default)
        => ProcessRecognitionAsync(image, SourceFace, ct);

    public async Task<AccessFlowResult> ProcessDeniedOnlyAsync(
        AccessDecision decision,
        CancellationToken ct = default)
    {
        if (decision.MemberId is { } mid && decision.Kind == AccessDecisionKind.RequireReception)
            await TryRecordBlockedAttemptAsync(mid, ct).ConfigureAwait(false);

        var deniedEvent = await _events.RecordDeniedAsync(
            decision.MemberId,
            AccessDirection.Entry,
            SourceFace,
            DeviceId,
            denialReason: decision.ReasonCode,
            cancellationToken: ct).ConfigureAwait(false);

        return new AccessFlowResult
        {
            Decision = decision,
            Event = deniedEvent,
            Direction = AccessDirection.Entry,
            UiMessage = string.IsNullOrWhiteSpace(decision.PublicMessage)
                ? AccessDecisionEvaluator.PublicReception
                : decision.PublicMessage
        };
    }

    public async Task<AccessFlowResult> ProcessAuthorizedPassageAsync(
        AccessDecision decision,
        string source,
        CancellationToken ct = default)
    {
        if (decision.MemberId is not { } memberId)
        {
            return new AccessFlowResult
            {
                Decision = decision,
                UiMessage = AccessDecisionEvaluator.PublicReception
            };
        }

        if (!decision.AllowAutomaticRelease)
            return await ProcessDeniedOnlyAsync(decision, ct).ConfigureAwait(false);

        var (allowed, blockReason) = await _presence
            .TryBeginRecognitionAsync(memberId, UnitId, ct)
            .ConfigureAwait(false);

        if (!allowed)
        {
            return new AccessFlowResult
            {
                Decision = decision,
                Direction = AccessDirection.Entry,
                UiMessage = blockReason ?? AccessDecisionEvaluator.PublicReception
            };
        }

        try
        {
            _presence.EntryOnlyMode = EntryOnlyMode;
            var plan = await _presence
                .PlanPassageAsync(memberId, UnitId, source, DeviceId, null, ct)
                .ConfigureAwait(false);

            if (plan is null)
            {
                _presence.ReleaseTurnstileGateIfNotStarted();
                return new AccessFlowResult
                {
                    Decision = decision,
                    UiMessage = "Estado de presença indefinido — procure a recepção."
                };
            }

            await _turnstile
                .ReleaseEntryAsync(top: decision.MemberName, cancellationToken: ct)
                .ConfigureAwait(false);

            var passage = await _turnstile
                .WaitForPassageAsync(PassageTimeout, ct)
                .ConfigureAwait(false);

            var passageConfirmed = passage == PassageOutcome.PassageDetected;
            var (visitId, accessEvent) = await _presence
                .ConfirmPassageAsync(plan.AttemptId, passageConfirmed, source, DeviceId, ct)
                .ConfigureAwait(false);

            if (accessEvent is not null)
            {
                accessEvent.DenialReason = decision.Kind == AccessDecisionKind.AllowTolerance
                    ? $"TOLERANCE:{decision.CauseCode}"
                    : decision.ReasonCode;
                await _events.EnqueueSyncAsync(accessEvent, ct).ConfigureAwait(false);
            }

            if (passageConfirmed && decision.ConsumeToleranceOnPassage)
                await TryConsumeToleranceAsync(memberId, accessEvent?.Id, ct).ConfigureAwait(false);

            if (passageConfirmed && decision.Kind == AccessDecisionKind.AllowTolerance)
                await UpdateLocalToleranceUsedAsync(memberId, ct).ConfigureAwait(false);

            var ui = passageConfirmed
                ? (string.IsNullOrWhiteSpace(decision.PublicMessage)
                    ? "Entrada registrada"
                    : decision.PublicMessage)
                : "Entrada liberada sem passagem";

            return new AccessFlowResult
            {
                Decision = decision,
                Event = accessEvent,
                Passage = passage,
                Direction = AccessDirection.Entry,
                AttemptId = plan.AttemptId,
                VisitId = visitId,
                UiMessage = ui
            };
        }
        catch
        {
            _presence.ReleaseTurnstileGateIfNotStarted();
            throw;
        }
    }

    public async Task<AccessFlowResult> ProcessManualReleaseAsync(
        Guid? memberId,
        string? memberName,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new AccessFlowResult
            {
                Decision = new AccessDecision { Allowed = false, Kind = AccessDecisionKind.RequireReception },
                UiMessage = "Informe o motivo da liberação manual."
            };
        }

        Member? member = null;
        if (memberId is { } id)
            member = await _members.GetByIdAsync(id, ct).ConfigureAwait(false);

        var decision = _decisionService.DecideManual(member, reason);
        decision.MemberId = memberId;
        decision.MemberName = memberName ?? member?.Name;

        return await ProcessAuthorizedPassageAsync(decision, SourceManual, ct).ConfigureAwait(false);
    }

    public Task<AccessFlowResult> ProcessManualExitAsync(
        Guid? memberId,
        string? memberName,
        CancellationToken ct = default)
    {
        if (EntryOnlyMode)
        {
            return Task.FromResult(new AccessFlowResult
            {
                Decision = new AccessDecision
                {
                    Allowed = true,
                    Kind = AccessDecisionKind.AllowFreeExit,
                    MemberId = memberId,
                    MemberName = memberName
                },
                Direction = AccessDirection.Exit,
                UiMessage = "Saída livre — sem controle facial nesta fase."
            });
        }

        return ProcessManualReleaseAsync(memberId, memberName, "exit", ct);
    }

    private async Task TryConsumeToleranceAsync(Guid memberId, Guid? eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UnitId))
            return;
        try
        {
            await _gestao.ConsumeToleranceAsync(UnitId, memberId, eventId, DeviceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Warning($"Tolerance consume failed (will retry on sync): {ex.Message}");
        }
    }

    private async Task TryRecordBlockedAttemptAsync(Guid memberId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UnitId))
            return;
        try
        {
            await _gestao.RecordBlockedAttemptAsync(UnitId, memberId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Warning($"Blocked attempt record failed: {ex.Message}");
        }
    }

    private async Task UpdateLocalToleranceUsedAsync(Guid memberId, CancellationToken ct)
    {
        var member = await _members.GetByIdAsync(memberId, ct).ConfigureAwait(false);
        if (member is null)
            return;
        member.ToleranceUsed = true;
        member.AccessDecisionKind = AccessDecisionKind.RequireReception.ToString();
        await _members.UpsertAsync(member, ct).ConfigureAwait(false);
    }

    private async Task<AccessFlowResult> ProcessRecognitionAsync(
        byte[] image,
        string source,
        CancellationToken ct)
    {
        var decision = await _recognition.IdentifyAndDecideAsync(image, ct).ConfigureAwait(false);
        if (!decision.Allowed)
            return await ProcessDeniedOnlyAsync(decision, ct).ConfigureAwait(false);
        return await ProcessAuthorizedPassageAsync(decision, source, ct).ConfigureAwait(false);
    }
}
