using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

public sealed class PersonPresenceState
{
    public Guid PersonId { get; set; }
    public string UnitId { get; set; } = string.Empty;
    public PresenceStateKind State { get; set; } = PresenceStateKind.Outside;
    public Guid? ActiveVisitId { get; set; }
    public Guid? PendingAttemptId { get; set; }
    public AccessDirection? LastConfirmedDirection { get; set; }
    public DateTime? LastConfirmedAt { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}
