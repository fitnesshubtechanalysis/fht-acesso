using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.App.Services;

/// <summary>
/// Entry + exit webcams on a single PC. Kiosk preview always uses the entry camera.
/// Exit camera (when enabled) runs silent observation only — never shown on the viewer.
/// </summary>
public sealed class WebcamLaneHost : IDisposable
{
    private const int ExitStaggerMs = 2500;

    public WebcamService Entry { get; } = new();
    public WebcamService Exit { get; } = new();

    /// <summary>True when an exit camera index is configured and distinct from entry.</summary>
    public bool ExitCameraEnabled { get; private set; }

    /// <summary>
    /// Legacy alias: hardware exit camera available.
    /// Dual-gate UI/recognition is no longer used — exit is silent observation only.
    /// </summary>
    public bool DualGateEnabled => ExitCameraEnabled;

    /// <summary>Preview source for the kiosk UI (always entry).</summary>
    public WebcamService ActivePreview { get; private set; }

    public WebcamLaneHost()
    {
        ActivePreview = Entry;
    }

    public void Configure(AppSettings settings)
    {
        Entry.Configure(settings.CameraWidth, settings.CameraHeight, settings.CameraFps, settings.ProcessFps);

        var exitW = settings.ExitCameraWidth > 0 ? settings.ExitCameraWidth : settings.CameraWidth;
        var exitH = settings.ExitCameraHeight > 0 ? settings.ExitCameraHeight : settings.CameraHeight;
        var exitPreviewFps = settings.CameraFps > 0 ? Math.Min(settings.CameraFps, 24) : 24;
        var exitProcessFps = settings.ExitProcessFps > 0 ? settings.ExitProcessFps : 12;
        Exit.Configure(exitW, exitH, exitPreviewFps, exitProcessFps);
        Exit.MaxProcessWidth = settings.ExitProcessMaxWidth > 0 ? settings.ExitProcessMaxWidth : 1920;

        Entry.MotionRatioThreshold = 0.032;
        Entry.MotionPixelThreshold = 28;
        Entry.MotionHold = TimeSpan.FromMilliseconds(900);
        Entry.MotionRoiWidthFraction = 0.42;
        Entry.MotionRoiHeightFraction = 0.52;
        Entry.MotionRoiCenterY = 0.44;

        Exit.MotionRatioThreshold = 0.036;
        Exit.MotionPixelThreshold = 28;
        Exit.MotionHold = TimeSpan.FromMilliseconds(900);
        Exit.MotionRoiWidthFraction = 0.38;
        Exit.MotionRoiHeightFraction = 0.48;
        Exit.MotionRoiCenterY = 0.40;

        var indexOk = settings.WebcamIndexExit >= 0
                      && settings.WebcamIndexExit != settings.WebcamIndex;
        // free = do not open exit cam. facial / observe / anything else with valid index → open for silent capture.
        var mode = (settings.ExitMode ?? "free").Trim();
        var observe = !string.Equals(mode, "free", StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase);
        ExitCameraEnabled = indexOk && observe;
        ActivePreview = Entry;
    }

    public void Start(AppSettings settings)
    {
        Configure(settings);

        if (!Entry.IsRunning)
            Entry.Start(settings.WebcamIndex, settings.CameraDeviceId);

        if (ExitCameraEnabled && !Exit.IsRunning)
        {
            // Stagger open — USB hubs often reject the 2nd open if simultaneous with the 1st.
            Thread.Sleep(ExitStaggerMs);
            Exit.Start(settings.WebcamIndexExit, settings.ExitCameraDeviceId);
        }
    }

    /// <summary>Wait until exit camera connects or timeout (call after Start).</summary>
    public bool WaitForExitCamera(TimeSpan timeout)
    {
        if (!ExitCameraEnabled)
            return true;

        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (Exit.State == WebcamConnectionState.Connected)
                return true;
            if (!Exit.IsRunning)
                return false;
            Thread.Sleep(200);
        }

        return Exit.State == WebcamConnectionState.Connected;
    }

    public void StopAll()
    {
        Entry.Stop();
        Exit.Stop();
    }

    public void SetActivePreviewLane(bool exitLane)
    {
        // Exit never owns the kiosk viewer — keep entry preview always.
        _ = exitLane;
        ActivePreview = Entry;
    }

    public void Dispose()
    {
        Entry.Dispose();
        Exit.Dispose();
    }
}
