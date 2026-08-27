namespace FHT.Access.Domain.Entities;

public sealed class SyncState
{
    public DateTime? LastMembersSyncAt { get; set; }
    public DateTime? LastEventsSyncAt { get; set; }
    public string? Cursor { get; set; }
}
