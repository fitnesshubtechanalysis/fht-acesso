using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Entities;

public sealed class AccessAttemptRecord
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string UnitId { get; set; } = string.Empty;
    public string? TurnstileSerial { get; set; }
    public AccessDirection RequestedDirection { get; set; }
    public AccessAttemptStatus Status { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? AccessEventId { get; set; }
    public DateTime RecognizedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? PassageConfirmedAt { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
}
