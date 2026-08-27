using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Central operating mode. Recognition is enabled only in Automatic.
/// </summary>
public sealed class OperatingModeService
{
    private AccessOperatingMode _mode = AccessOperatingMode.Automatic;
    private readonly object _sync = new();

    public AccessOperatingMode Mode
    {
        get { lock (_sync) return _mode; }
    }

    public bool RecognitionEnabled => Mode == AccessOperatingMode.Automatic;

    public event EventHandler<AccessOperatingMode>? ModeChanged;

    public void SetMode(AccessOperatingMode mode)
    {
        AccessOperatingMode previous;
        lock (_sync)
        {
            if (_mode == mode)
                return;
            previous = _mode;
            _mode = mode;
        }

        ModeChanged?.Invoke(this, mode);
        System.Diagnostics.Debug.WriteLine($"OperatingMode {previous} → {mode} RecognitionEnabled={RecognitionEnabled}");
    }

    public void EnterAttendant() => SetMode(AccessOperatingMode.Attendant);
    public void EnterEnrollment() => SetMode(AccessOperatingMode.Enrollment);
    public void EnterAutomatic() => SetMode(AccessOperatingMode.Automatic);
    public void EnterMaintenance() => SetMode(AccessOperatingMode.Maintenance);
}
