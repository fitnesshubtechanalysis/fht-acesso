using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

public sealed class AccessEvent
{
    public Guid Id { get; set; }
    public Guid? MemberId { get; set; }
    public AccessDirection Direction { get; set; }
    public AccessEventStatus Status { get; set; }
    public bool PassageConfirmed { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    public DateTime OccurredAt { get; set; }
    public string Source { get; set; } = string.Empty;
    /// <summary>facial, pin, manual ou card. Evento antigo pode ficar vazio.</summary>
    public string? Method { get; set; }
    public string? DeviceId { get; set; }
    public string? DenialReason { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? VisitId { get; set; }
}
