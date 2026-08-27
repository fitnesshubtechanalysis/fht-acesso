namespace FHT.Access.Domain.Entities;

public sealed class MemberFace
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public byte[] Template { get; set; } = Array.Empty<byte>();
    public string ModelVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
