using FHT.Access.Infrastructure.Settings;



namespace FHT.Access.App.Services;



/// <summary>

/// Entry + exit webcams on a single PC. Enrollment and kiosk preview use the entry camera by default.

/// </summary>

public sealed class WebcamLaneHost : IDisposable

{

    private const int ExitStaggerMs = 2500;



    public WebcamService Entry { get; } = new();

    public WebcamService Exit { get; } = new();



    public bool DualGateEnabled { get; private set; }



    /// <summary>Preview source for the kiosk UI (follows active recognition lane).</summary>

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
        // Saída: menos sensível que o antigo 0.004 (disparava com qualquer movimento longe).
        Exit.MotionRatioThreshold = 0.024;
        Exit.MotionPixelThreshold = 26;
        Exit.MotionHold = TimeSpan.FromSeconds(2.0);
        Exit.MotionRoiWidthFraction = 0.38;
        Exit.MotionRoiHeightFraction = 0.48;
        Exit.MotionRoiCenterY = 0.40;

        Entry.MotionRatioThreshold = 0.028;
        Entry.MotionPixelThreshold = 28;
        Entry.MotionHold = TimeSpan.FromSeconds(1.6);
        Entry.MotionRoiWidthFraction = 0.50;
        Entry.MotionRoiHeightFraction = 0.58;
        Entry.MotionRoiCenterY = 0.45;



        DualGateEnabled = settings.WebcamIndexExit >= 0

                          && settings.WebcamIndexExit != settings.WebcamIndex;

    }



    public void Start(AppSettings settings)

    {

        Configure(settings);



        if (!Entry.IsRunning)

            Entry.Start(settings.WebcamIndex, settings.CameraDeviceId);



        if (DualGateEnabled && !Exit.IsRunning)

        {

            // Stagger open — USB hubs often reject the 2nd open if simultaneous with the 1st.

            Thread.Sleep(ExitStaggerMs);

            Exit.Start(settings.WebcamIndexExit, settings.ExitCameraDeviceId);

        }

    }



    /// <summary>Wait until exit camera connects or timeout (call after Start).</summary>

    public bool WaitForExitCamera(TimeSpan timeout)

    {

        if (!DualGateEnabled)

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

        ActivePreview = exitLane && DualGateEnabled ? Exit : Entry;

    }



    public void Dispose()

    {

        Entry.Dispose();

        Exit.Dispose();

    }

}


