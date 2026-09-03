using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

public sealed class Member
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MemberStatus Status { get; set; } = MemberStatus.Inactive;
    public bool AccessAllowed { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Cpf { get; set; }

    public string? ReasonCode { get; set; }
    public string? OperationalStatus { get; set; }
    public string? FinancialStatus { get; set; }
    public string? AccessStatus { get; set; }
    public string? AccessDecisionKind { get; set; }
    public bool ToleranceUsed { get; set; }
    public Guid? ToleranceOccurrenceId { get; set; }
    public string? OccurrenceCauseCode { get; set; }
    public Guid? RelationshipActionId { get; set; }

    /// <summary>Professor/colaborador — ignora trava de presença (entrada/saída livres).</summary>
    public bool BypassPresence { get; set; }
}
