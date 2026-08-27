namespace FHT.Access.Infrastructure.Settings;

public sealed class AppSettings
{
    public string GestaoBaseUrl { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceSecret { get; set; } = string.Empty;
    public bool UseFakeTurnstile { get; set; } = true;
    public string TurnstileNetwork { get; set; } = string.Empty;
    public string TurnstileIp { get; set; } = string.Empty;
    public string TurnstileSerial { get; set; } = string.Empty;
    public string? TurnstileNicGuid { get; set; }
    public string? TurnstileExpectedIpv4 { get; set; }
    public int WebcamIndex { get; set; }
    public string? CameraDeviceId { get; set; }
    public int CameraWidth { get; set; } = 1920;
    public int CameraHeight { get; set; } = 1080;
    public int CameraFps { get; set; } = 30;
    public int ProcessFps { get; set; } = 8;
    public double FaceMatchThreshold { get; set; } = 0.35;
    public string AdminPin { get; set; } = "1234";
    public int AttendantIdleMinutes { get; set; } = 5;
    public bool KioskPortrait { get; set; } = true;
    public string DataDirectory { get; set; } = string.Empty;
    public int PassageTimeoutSec { get; set; } = 10;
    public int RecognitionCooldownSec { get; set; } = 3;
    public int VisitMaxHours { get; set; } = 12;
    public bool StartWithWindows { get; set; }
    public int StartupDelaySec { get; set; } = 8;
    public string ExitMode { get; set; } = "free";

    public DeviceSettings? Device { get; set; }
    public SyncStateSettings? SyncState { get; set; }
}

public sealed class DeviceSettings
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UnitId { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class SyncStateSettings
{
    public DateTime? LastMembersSyncAt { get; set; }
    public DateTime? LastEventsSyncAt { get; set; }
    public string? Cursor { get; set; }
}
