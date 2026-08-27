using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

public sealed class VisitRecord
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string UnitId { get; set; } = string.Empty;
    public Guid? EntryAttemptId { get; set; }
    public Guid? ExitAttemptId { get; set; }
    public DateTime? EnteredAt { get; set; }
    public DateTime? ExitedAt { get; set; }
    public VisitStatus Status { get; set; }
    public string? CloseReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
