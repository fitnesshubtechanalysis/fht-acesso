using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

public sealed class PresenceCorrectionRecord
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string UnitId { get; set; } = string.Empty;
    public PresenceStateKind PreviousState { get; set; }
    public PresenceStateKind NewState { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
