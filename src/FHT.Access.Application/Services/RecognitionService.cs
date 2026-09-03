using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
namespace FHT.Access.Application.Services;

public sealed class RecognitionService
{
    private readonly IFaceRecognitionService _face;
    private readonly IMemberRepository _members;
    private readonly AccessDecisionService _decision;
    private readonly MemberSyncService _memberSync;
    private readonly IGestaoAccessClient _gestao;
    private readonly IAccessDeviceContext _device;
    private readonly IDiagnosticLog? _log;

    public RecognitionService(
        IFaceRecognitionService face,
        IMemberRepository members,
        AccessDecisionService decision,
        MemberSyncService memberSync,
        IGestaoAccessClient gestao,
        IAccessDeviceContext device,
        IDiagnosticLog? log = null)
    {
        _face = face;
        _members = members;
        _decision = decision;
        _memberSync = memberSync;
        _gestao = gestao;
        _device = device;
        _log = log;
    }

    public RecognitionState State { get; private set; } = RecognitionState.Idle;
    public event EventHandler<RecognitionState>? StateChanged;

    public async Task<AccessDecision> IdentifyAndDecideAsync(
        byte[] imageBgrOrJpeg,
        CancellationToken cancellationToken = default)
    {
        SetState(RecognitionState.Identifying);

        var match = await _face.IdentifyAsync(imageBgrOrJpeg, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            SetState(RecognitionState.Denied);
            _log?.Information("Face identify: no match.");
            return _decision.Decide(null);
        }

        return await DecideFromMatchAsync(match, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fast local-only identify for the kiosk polling loop (no HTTP).
    /// </summary>
    public Task<FaceMatchResult?> IdentifyOnlyAsync(
        byte[] imageBgrOrJpeg,
        CancellationToken cancellationToken = default,
        FaceDetectionOptions? detection = null)
        => _face.IdentifyAsync(imageBgrOrJpeg, cancellationToken, detection);

    /// <summary>
    /// One-shot decision after a local match (refresh + online evaluate at most once).
    /// </summary>
    public async Task<AccessDecision> DecideFromMatchAsync(
        FaceMatchResult match,
        CancellationToken cancellationToken = default)
    {
        SetState(RecognitionState.Identifying);

        // Prefer local decision immediately — never block gate release on HTTP.
        var member = await _members.GetByIdAsync(match.MemberId, cancellationToken).ConfigureAwait(false);
        var decision = _decision.Decide(member, match.Score);

        _log?.Information(
            $"Face identify: {member?.Name ?? match.MemberId.ToString()} score={match.Score:F3} kind={decision.Kind} allowed={decision.Allowed}");

        SetState(decision.Allowed ? RecognitionState.Matched : RecognitionState.Denied);

        // Best-effort enrich in background (does not delay UI / catraca).
        _ = Task.Run(async () =>
        {
            try
            {
                using var linked = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _memberSync.RefreshMemberAsync(match.MemberId, linked.Token).ConfigureAwait(false);
                await TryEvaluateOnlineAsync(match.MemberId, linked.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore background enrich failures
            }
        });

        return decision;
    }

    public Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken cancellationToken = default)
        => _face.EnrollAsync(memberId, imageBgrOrJpeg, cancellationToken);

    public async Task RemoveAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        await _face.RemoveAsync(memberId, cancellationToken).ConfigureAwait(false);
        await _members.RemoveFaceAsync(memberId, cancellationToken).ConfigureAwait(false);
        _log?.Information($"Face template removed for {memberId}");
    }

    public AccessDecision DecideUnknown() => _decision.Decide(null);

    public void Reset() => SetState(RecognitionState.Idle);

    private async Task TryEvaluateOnlineAsync(Guid memberId, CancellationToken ct)
    {
        var unitId = _device.UnitId?.Trim();
        if (string.IsNullOrWhiteSpace(unitId))
            return;

        if (!string.IsNullOrWhiteSpace(_device.DeviceId)
            && !string.IsNullOrWhiteSpace(_device.DeviceSecret))
        {
            await _gestao
                .EnsureAuthenticatedAsync(_device.DeviceId.Trim(), _device.DeviceSecret, ct)
                .ConfigureAwait(false);
        }

        var eval = await _gestao.EvaluateAccessAsync(unitId, memberId, ct).ConfigureAwait(false);
        if (eval is null)
            return;

        var member = await _members.GetByIdAsync(memberId, ct).ConfigureAwait(false);
        if (member is null)
            return;

        member.AccessDecisionKind = eval.Kind;
        member.OperationalStatus = eval.Operational;
        member.FinancialStatus = eval.Financial;
        member.AccessStatus = eval.Access;
        member.OccurrenceCauseCode = eval.CauseCode;
        member.ToleranceOccurrenceId = eval.OccurrenceId;
        member.RelationshipActionId = eval.RelationshipActionId;
        member.ReasonCode = eval.CauseCode;
        await _members.UpsertAsync(member, ct).ConfigureAwait(false);
    }

    private void SetState(RecognitionState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
