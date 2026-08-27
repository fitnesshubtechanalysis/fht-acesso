namespace FHT.Access.Domain.Entities;

public sealed class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? IpAddress { get; set; }
}
