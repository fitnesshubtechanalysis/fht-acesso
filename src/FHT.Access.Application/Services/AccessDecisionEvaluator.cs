using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Mirrors gestão-api access-decision.ts for offline-first evaluation.
/// </summary>
public sealed class AccessDecisionEvaluator
{
    public const string PublicTolerance =
        "Acesso liberado. Por favor, procure a recepção para atualizar seu cadastro.";
    public const string PublicReception = "Por favor, procure a recepção para continuar.";

    public int AutoTolerancesPerOccurrence { get; set; } = 1;

    public AccessDecision Evaluate(Member member, double? score = null)
    {
        var occurrence = member.ToleranceOccurrenceId is not null
            ? new OccurrenceSnapshot(
                member.ToleranceOccurrenceId.Value,
                member.OccurrenceCauseCode ?? "unknown",
                member.ToleranceUsed,
                0,
                "open",
                member.RelationshipActionId)
            : null;

        return EvaluateInternal(member, occurrence, score);
    }

    private AccessDecision EvaluateInternal(
        Member member,
        OccurrenceSnapshot? occurrence,
        double? score)
    {
        var operational = member.OperationalStatus ?? string.Empty;
        var financial = member.FinancialStatus ?? string.Empty;
        var access = member.AccessStatus ?? string.Empty;
        var lifecycle = MapLifecycle(member.Status);
        var causeCode = DeriveCauseCode(lifecycle, operational, financial, access, member.ReasonCode);

        var baseDecision = new AccessDecision
        {
            MemberId = member.Id,
            MemberName = member.Name,
            Score = score,
            OperationalStatus = operational,
            FinancialStatus = financial,
            AccessStatus = access,
            CauseCode = causeCode,
            OccurrenceId = occurrence?.Id,
            RelationshipActionId = occurrence?.RelationshipActionId,
            ToleranceUsed = occurrence?.ToleranceUsed ?? false,
        };

        if (lifecycle == "bloqueado" || member.Status == MemberStatus.Blocked)
        {
            return Deny(baseDecision, AccessDecisionKind.DenySecurity,
                "Aluno bloqueado administrativamente.", causeCode);
        }

        if (operational == "suspended")
        {
            return Deny(baseDecision, AccessDecisionKind.DenyAdministrative,
                "Matrícula suspensa — atendimento necessário.", causeCode);
        }

        if (IsFullyRegular(operational, financial, access))
        {
            return Allow(baseDecision, AccessDecisionKind.AllowRegular, string.Empty,
                "Acesso regular.", causeCode: null);
        }

        if (!IsToleranceEligible(lifecycle, operational, financial, access, member))
        {
            baseDecision.Kind = AccessDecisionKind.RequireReception;
            baseDecision.Allowed = false;
            baseDecision.PublicMessage = PublicReception;
            baseDecision.PrivateMessage = "Situação requer atendimento — tolerância não aplicável.";
            baseDecision.RequiresManualRelease = true;
            return baseDecision;
        }

        var open = occurrence?.Status == "open" ? occurrence : null;
        var toleranceUsed = open?.ToleranceUsed ?? false;
        var autoLimit = Math.Max(1, AutoTolerancesPerOccurrence);

        if (!toleranceUsed && (open?.AttemptCount ?? 0) < autoLimit)
        {
            baseDecision.Kind = AccessDecisionKind.AllowTolerance;
            baseDecision.Allowed = true;
            baseDecision.AllowAutomaticRelease = true;
            baseDecision.ConsumeToleranceOnPassage = true;
            baseDecision.PublicMessage = PublicTolerance;
            baseDecision.PrivateMessage = $"Primeira liberação por tolerância ({causeCode}).";
            return baseDecision;
        }

        baseDecision.Kind = AccessDecisionKind.RequireReception;
        baseDecision.Allowed = false;
        baseDecision.PublicMessage = PublicReception;
        baseDecision.PrivateMessage =
            "Tolerância já utilizada. É necessária regularização ou autorização do atendente.";
        baseDecision.RequiresManualRelease = true;
        return baseDecision;
    }

    public AccessDecision EvaluateManual(Member? member, string reason, double? score = null)
    {
        if (member is null)
        {
            return new AccessDecision
            {
                Allowed = true,
                Kind = AccessDecisionKind.AllowManual,
                AllowAutomaticRelease = true,
                PublicMessage = string.Empty,
                PrivateMessage = reason,
                ReasonCode = $"MANUAL:{reason}",
                Score = score
            };
        }

        var d = EvaluateInternal(member, null, score);
        d.Allowed = true;
        d.Kind = AccessDecisionKind.AllowManual;
        d.AllowAutomaticRelease = true;
        d.PublicMessage = string.Empty;
        d.PrivateMessage = reason;
        d.ReasonCode = $"MANUAL:{reason}";
        d.ConsumeToleranceOnPassage = false;
        d.RequiresManualRelease = false;
        return d;
    }

    private static AccessDecision Allow(
        AccessDecision d,
        AccessDecisionKind kind,
        string publicMsg,
        string privateMsg,
        string? causeCode)
    {
        d.Allowed = true;
        d.Kind = kind;
        d.AllowAutomaticRelease = true;
        d.PublicMessage = publicMsg;
        d.PrivateMessage = privateMsg;
        d.CauseCode = causeCode;
        return d;
    }

    private static AccessDecision Deny(
        AccessDecision d,
        AccessDecisionKind kind,
        string privateMsg,
        string? causeCode)
    {
        d.Allowed = false;
        d.Kind = kind;
        d.PublicMessage = PublicReception;
        d.PrivateMessage = privateMsg;
        d.CauseCode = causeCode;
        return d;
    }

    private static string MapLifecycle(MemberStatus status) =>
        status == MemberStatus.Blocked ? "bloqueado" : "ativo";

    private static bool IsFullyRegular(string operational, string financial, string access) =>
        (operational is "active" or "expiring") &&
        (financial is "regular" or "no_receivables") &&
        access == "allowed";

    private static bool IsToleranceEligible(
        string lifecycle,
        string operational,
        string financial,
        string access,
        Member member)
    {
        if (lifecycle == "bloqueado") return false;
        if (operational == "suspended") return false;

        if (operational is "no_enrollment" or "inactive" or "expired_recently") return true;

        if (access == "blocked") return false;
        if (access == "manual_review") return true;
        if (financial is "overdue" or "partially_paid_overdue" or "pending_validation") return true;
        if (!member.AccessAllowed && member.ReasonCode is not null) return true;

        return false;
    }

    private static string DeriveCauseCode(
        string lifecycle,
        string operational,
        string financial,
        string access,
        string? reasonCode)
    {
        if (lifecycle == "bloqueado") return "blocked";
        if (operational == "suspended") return "suspended";
        if (operational == "no_enrollment") return "no_active_enrollment";
        if (operational == "inactive") return "inactive";
        if (operational == "expired_recently") return "expired_recently";
        if (financial is "overdue" or "partially_paid_overdue") return "overdue_review";
        if (financial == "pending_validation") return "pending_validation";
        if (access == "manual_review") return "manual_review";
        return reasonCode ?? "unknown";
    }

    private sealed record OccurrenceSnapshot(
        Guid Id,
        string CauseCode,
        bool ToleranceUsed,
        int AttemptCount,
        string Status,
        Guid? RelationshipActionId);
}
