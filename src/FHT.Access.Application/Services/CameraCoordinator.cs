using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Arbitrates webcam ownership so recognition and enrollment never fight for the same camera.
/// </summary>
public sealed class CameraCoordinator
{
    private readonly object _sync = new();
    private CameraUsageMode _usage = CameraUsageMode.Idle;
    private string? _owner;

    public CameraUsageMode Usage
    {
        get { lock (_sync) return _usage; }
    }

    public string? Owner
    {
        get { lock (_sync) return _owner; }
    }

    public event EventHandler<CameraUsageMode>? UsageChanged;

    public bool TryAcquire(CameraUsageMode usage, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_sync)
        {
            if (_usage is CameraUsageMode.Enrollment or CameraUsageMode.Maintenance
                && !string.Equals(_owner, owner, StringComparison.Ordinal))
            {
                return false;
            }

            if (_usage is CameraUsageMode.Recognition
                && usage is CameraUsageMode.Enrollment
                && !string.Equals(_owner, owner, StringComparison.Ordinal))
            {
                // Enrollment can preempt monitoring/recognition when attendant starts capture.
                // Caller should stop recognition first via OperatingMode.
            }

            _usage = usage;
            _owner = owner;
        }

        UsageChanged?.Invoke(this, usage);
        return true;
    }

    public void Release(string owner)
    {
        lock (_sync)
        {
            if (!string.Equals(_owner, owner, StringComparison.Ordinal))
                return;
            _usage = CameraUsageMode.Idle;
            _owner = null;
        }

        UsageChanged?.Invoke(this, CameraUsageMode.Idle);
    }

    public bool IsOwnedBy(string owner)
    {
        lock (_sync)
            return string.Equals(_owner, owner, StringComparison.Ordinal);
    }
}
