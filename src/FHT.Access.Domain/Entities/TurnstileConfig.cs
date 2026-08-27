namespace FHT.Access.Domain.Entities;

public sealed class TurnstileConfig
{
    public string NetworkInterface { get; set; } = string.Empty;
    public string BoardIp { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public bool UseFake { get; set; }
}
