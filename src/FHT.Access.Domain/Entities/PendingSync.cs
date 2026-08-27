namespace FHT.Access.Domain.Entities;

/// <summary>
/// Outbox item awaiting upload to gestão (plan alias: SyncItem).
/// </summary>
public sealed class PendingSync
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
