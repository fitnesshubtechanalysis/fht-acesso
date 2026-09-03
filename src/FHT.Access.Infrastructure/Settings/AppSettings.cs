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
    /// <summary>Second camera for exit lane on the same PC (-1 = single camera).</summary>
    public int WebcamIndexExit { get; set; } = -1;
    public string? CameraDeviceId { get; set; }
    public string? ExitCameraDeviceId { get; set; }
    public int CameraWidth { get; set; } = 1920;
    public int CameraHeight { get; set; } = 1080;
    public int CameraFps { get; set; } = 30;
    public int ProcessFps { get; set; } = 8;
    /// <summary>Espelha horizontalmente o frame (JPEG + preview) — cadastro e reconhecimento ficam iguais.</summary>
    public bool CameraFlipHorizontal { get; set; }
    /// <summary>Inverte verticalmente (câmera de cabeça para baixo).</summary>
    public bool CameraFlipVertical { get; set; }
    /// <summary>Rotação aplicada ao frame: 0, 90, 180 ou 270.</summary>
    public int CameraRotateDegrees { get; set; }
    public double FaceMatchThreshold { get; set; } = 0.48;
    public string AdminPin { get; set; } = "1234";
    public int AttendantIdleMinutes { get; set; } = 5;
    public bool KioskPortrait { get; set; } = true;
    public string DataDirectory { get; set; } = string.Empty;
    public int ExitProcessFps { get; set; } = 12;
    public int ExitProcessMaxWidth { get; set; } = 1920;
    public int ExitCameraWidth { get; set; }
    public int ExitCameraHeight { get; set; }
    public int PassageTimeoutSec { get; set; } = 15;
    /// <summary>Seconds for "Entrada/Saída registrada" on kiosk (default 5).</summary>
    public int PassageSuccessDisplaySec { get; set; } = 5;
    /// <summary>Minimum seconds for "Pode passar na catraca/saída" (default 3).</summary>
    public int PassageReleaseMinDisplaySec { get; set; } = 3;
    public int RecognitionCooldownSec { get; set; } = 3;
    public int VisitMaxHours { get; set; } = 12;
    public bool StartWithWindows { get; set; }
    public int StartupDelaySec { get; set; } = 8;
    /// <summary>
    /// Catraca livre com facial: registra entrada/saída pela lane, sem bloquear
    /// por dupla entrada, dupla saída ou saída sem entrada. Plano/facial seguem iguais.
    /// Passagem física na catraca continua obrigatória para confirmar presença.
    /// </summary>
    public bool FreeGateMode { get; set; }

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
