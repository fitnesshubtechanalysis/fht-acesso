using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Runs entry and/or exit recognition loops on a single PC (one camera per lane).
/// </summary>
public sealed class GateLaneEngineHost : IAsyncDisposable
{
    private readonly OperatingModeService _mode;
    private readonly AccessStateMachine _states;
    private readonly CameraCoordinator _camera;
    private readonly RecognitionService _recognition;
    private readonly AccessFlowService _flow;

    private readonly RecognitionSessionGuard _entrySessions = new();
    private readonly RecognitionSessionGuard _exitSessions = new();

    private AutomaticAccessEngine? _entryEngine;
    private AutomaticAccessEngine? _exitEngine;
    private bool _dualGate;

    public GateLaneEngineHost(
        OperatingModeService mode,
        AccessStateMachine states,
        CameraCoordinator camera,
        RecognitionService recognition,
        AccessFlowService flow)
    {
        _mode = mode;
        _states = states;
        _camera = camera;
        _recognition = recognition;
        _flow = flow;
    }

    public AutomaticAccessEngine EntryEngine => _entryEngine
        ?? throw new InvalidOperationException("Gate lanes not configured.");

    public void ConfigureDualGate(bool enabled)
    {
        _dualGate = enabled;
        _flow.DualGateMode = enabled;
    }

    public void BindCameras(
        Func<byte[]?> captureEntryJpeg,
        Func<bool> entryMotionHint,
        Func<byte[]?>? captureExitJpeg = null,
        Func<bool>? exitMotionHint = null)
    {
        _entryEngine ??= CreateEngine(AccessDirection.Entry, AutomaticAccessEngine.EntryCameraOwner, _entrySessions);
        _entryEngine.BindCamera(captureEntryJpeg, entryMotionHint);

        if (captureExitJpeg is not null && exitMotionHint is not null)
        {
            _exitEngine ??= CreateEngine(AccessDirection.Exit, AutomaticAccessEngine.ExitCameraOwner, _exitSessions);
            _exitEngine.BindCamera(captureExitJpeg, exitMotionHint);
        }
    }

    public void Start()
    {
        _entryEngine ??= CreateEngine(AccessDirection.Entry, AutomaticAccessEngine.EntryCameraOwner, _entrySessions);
        _entryEngine.Start();

        if (_dualGate)
        {
            _exitEngine ??= CreateEngine(AccessDirection.Exit, AutomaticAccessEngine.ExitCameraOwner, _exitSessions);
            _exitEngine.Start();
        }
    }

    public void RetryFromKiosk()
    {
        _entryEngine?.RetryFromKiosk();
        if (_dualGate)
            _exitEngine?.RetryFromKiosk();
    }

    public void ConfigureKioskDisplay(TimeSpan passageSuccess, TimeSpan passageReleaseMin)
    {
        if (_entryEngine is not null)
        {
            _entryEngine.PassageSuccessDisplay = passageSuccess;
            _entryEngine.PassageReleaseMinDisplay = passageReleaseMin;
        }

        if (_exitEngine is not null)
        {
            _exitEngine.PassageSuccessDisplay = passageSuccess;
            _exitEngine.PassageReleaseMinDisplay = passageReleaseMin;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_entryEngine is not null)
            await _entryEngine.DisposeAsync().ConfigureAwait(false);
        if (_exitEngine is not null)
            await _exitEngine.DisposeAsync().ConfigureAwait(false);
    }

    private AutomaticAccessEngine CreateEngine(
        AccessDirection direction,
        string cameraOwner,
        RecognitionSessionGuard sessions)
        => new(
            _mode,
            _states,
            sessions,
            _camera,
            _recognition,
            _flow,
            direction,
            cameraOwner);
}
