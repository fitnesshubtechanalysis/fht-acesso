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

        await _memberSync.RefreshMemberAsync(match.MemberId, cancellationToken).ConfigureAwait(false);
        await TryEvaluateOnlineAsync(match.MemberId, cancellationToken).ConfigureAwait(false);

        var member = await _members.GetByIdAsync(match.MemberId, cancellationToken).ConfigureAwait(false);
        var decision = _decision.Decide(member, match.Score);
        _log?.Information(
            $"Face identify: {member?.Name ?? match.MemberId.ToString()} kind={decision.Kind} allowed={decision.Allowed}");

        SetState(decision.Allowed ? RecognitionState.Matched : RecognitionState.Denied);
        return decision;
    }

    private async Task TryEvaluateOnlineAsync(Guid memberId, CancellationToken ct)
    {
        var unitId = _device.UnitId?.Trim();
        if (string.IsNullOrWhiteSpace(unitId))
            return;

        try
        {
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
        catch (Exception ex)
        {
            _log?.Warning($"Online evaluate skipped: {ex.Message}");
        }
    }

    public Task<FaceMatchResult?> IdentifyOnlyAsync(
        byte[] imageBgrOrJpeg,
        CancellationToken cancellationToken = default)
        => _face.IdentifyAsync(imageBgrOrJpeg, cancellationToken);

    public Task EnrollAsync(Guid memberId, byte[] imageBgrOrJpeg, CancellationToken cancellationToken = default)
        => _face.EnrollAsync(memberId, imageBgrOrJpeg, cancellationToken);

    public Task RemoveAsync(Guid memberId, CancellationToken cancellationToken = default)
        => _face.RemoveAsync(memberId, cancellationToken);

    public AccessDecision DecideUnknown() => _decision.Decide(null);

    public void Reset() => SetState(RecognitionState.Idle);

    private void SetState(RecognitionState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
