using FHT.Access.Application.Services;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.App.Services;

/// <summary>
/// Câmera de entrada. A de saída não abre: não reconhece, não mostra nome e não libera.
/// </summary>
public sealed class WebcamLaneHost : IDisposable
{
    public WebcamService Entry { get; } = new();
    public WebcamService Exit { get; } = new();

    /// <summary>A câmera de saída permanece desligada.</summary>
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

        Entry.MotionRatioThreshold = 0.018;
        Entry.MotionPixelThreshold = 28;
        Entry.MotionHold = TimeSpan.FromMilliseconds(900);
        Entry.MotionRoiWidthFraction = 0.62;
        Entry.MotionRoiHeightFraction = 0.68;
        Entry.MotionRoiCenterY = 0.46;

        Exit.MotionRatioThreshold = 0.036;
        Exit.MotionPixelThreshold = 28;
        Exit.MotionHold = TimeSpan.FromMilliseconds(900);
        Exit.MotionRoiWidthFraction = 0.38;
        Exit.MotionRoiHeightFraction = 0.48;
        Exit.MotionRoiCenterY = 0.40;

        // A câmera de saída fica desligada. Só a de entrada reconhece e libera.
        _ = settings.WebcamIndexExit;
        _ = settings.ExitMode;
        ExitCameraEnabled = false;
        ActivePreview = Entry;
    }

    public void Start(AppSettings settings)
    {
        Configure(settings);

        if (!Entry.IsRunning)
        {
            var order = EntryCameraSelection.TryOrder(settings.WebcamIndex, settings.WebcamIndexExit);
            Entry.Start(order[0], settings.CameraDeviceId, order);
        }

        Exit.Stop();
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
