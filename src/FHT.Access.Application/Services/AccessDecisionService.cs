using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class AccessDecisionService
{
    public const string ReasonMemberNotFound = "MEMBER_NOT_FOUND";

    private readonly AccessDecisionEvaluator _evaluator;

    public AccessDecisionService(AccessDecisionEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public AccessDecision Decide(Member? member, double? score = null)
    {
        if (member is null)
        {
            return new AccessDecision
            {
                Allowed = false,
                Kind = AccessDecisionKind.RequireReception,
                ReasonCode = ReasonMemberNotFound,
                PublicMessage = AccessDecisionEvaluator.PublicReception,
                PrivateMessage = "Rosto não identificado.",
                Score = score
            };
        }

        if (!string.IsNullOrWhiteSpace(member.AccessDecisionKind)
            && Enum.TryParse<AccessDecisionKind>(member.AccessDecisionKind, ignoreCase: true, out var synced))
        {
            return MapSyncedDecision(member, synced, score);
        }

        return _evaluator.Evaluate(member, score);
    }

    public AccessDecision DecideManual(Member? member, string reason, double? score = null)
        => _evaluator.EvaluateManual(member, reason, score);

    private static AccessDecision MapSyncedDecision(Member member, AccessDecisionKind kind, double? score)
    {
        var allowed = kind is AccessDecisionKind.AllowRegular
            or AccessDecisionKind.AllowTolerance
            or AccessDecisionKind.AllowManual;

        return new AccessDecision
        {
            Allowed = allowed,
            Kind = kind,
            MemberId = member.Id,
            MemberName = member.Name,
            ReasonCode = member.ReasonCode,
            Score = score,
            OperationalStatus = member.OperationalStatus,
            FinancialStatus = member.FinancialStatus,
            AccessStatus = member.AccessStatus,
            CauseCode = member.OccurrenceCauseCode ?? member.ReasonCode,
            OccurrenceId = member.ToleranceOccurrenceId,
            RelationshipActionId = member.RelationshipActionId,
            ToleranceUsed = member.ToleranceUsed,
            AllowAutomaticRelease = allowed,
            ConsumeToleranceOnPassage = kind == AccessDecisionKind.AllowTolerance && !member.ToleranceUsed,
            RequiresManualRelease = kind == AccessDecisionKind.RequireReception,
            PublicMessage = kind switch
            {
                AccessDecisionKind.AllowTolerance => AccessDecisionEvaluator.PublicTolerance,
                AccessDecisionKind.RequireReception or AccessDecisionKind.DenyAdministrative
                    or AccessDecisionKind.DenySecurity => AccessDecisionEvaluator.PublicReception,
                _ => string.Empty
            },
            PrivateMessage = kind.ToString()
        };
    }
}
