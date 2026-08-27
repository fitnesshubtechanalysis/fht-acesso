using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

/// <summary>
/// Access decision — separated from enrollment/financial status.
/// ReasonCode is internal; PublicMessage is kiosk-safe.
/// </summary>
public sealed class AccessDecision
{
    public bool Allowed { get; set; }
    public AccessDecisionKind Kind { get; set; }
    public Guid? MemberId { get; set; }
    public string? MemberName { get; set; }
    public string? ReasonCode { get; set; }
    public double? Score { get; set; }

    public string? OperationalStatus { get; set; }
    public string? FinancialStatus { get; set; }
    public string? AccessStatus { get; set; }
    public string? CauseCode { get; set; }

    public string PublicMessage { get; set; } = string.Empty;
    public string PrivateMessage { get; set; } = string.Empty;

    public bool AllowAutomaticRelease { get; set; }
    public bool ConsumeToleranceOnPassage { get; set; }
    public bool RequiresManualRelease { get; set; }

    public Guid? OccurrenceId { get; set; }
    public Guid? RelationshipActionId { get; set; }
    public bool ToleranceUsed { get; set; }
}
