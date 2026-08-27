using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Tracks attendant idle timeout and returns to Automatic when inactive.
/// Does not interrupt active Enrollment capture abruptly.
/// </summary>
public sealed class AttendantSessionService
{
    private readonly OperatingModeService _mode;
    private readonly AccessStateMachine _states;
    private readonly object _sync = new();
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private CancellationTokenSource? _watchCts;

    public AttendantSessionService(OperatingModeService mode, AccessStateMachine states)
    {
        _mode = mode;
        _states = states;
        _mode.ModeChanged += OnModeChanged;
    }

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan WarningLead { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsWarningVisible { get; private set; }
    public event EventHandler? IdleWarning;
    public event EventHandler? ForcedLogout;

    public void Touch()
    {
        lock (_sync)
        {
            _lastActivityUtc = DateTime.UtcNow;
            IsWarningVisible = false;
        }
    }

    public void ContinueAttending()
    {
        Touch();
    }

    private void OnModeChanged(object? sender, AccessOperatingMode mode)
    {
        _watchCts?.Cancel();
        _watchCts?.Dispose();
        _watchCts = null;
        IsWarningVisible = false;

        if (mode is AccessOperatingMode.Attendant or AccessOperatingMode.Enrollment)
        {
            Touch();
            _watchCts = new CancellationTokenSource();
            _ = WatchAsync(_watchCts.Token);
        }
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);

                if (_mode.Mode == AccessOperatingMode.Enrollment)
                {
                    // Soft: enrollment counts as activity while on enrollment screen.
                    continue;
                }

                if (_mode.Mode != AccessOperatingMode.Attendant)
                    return;

                TimeSpan idle;
                lock (_sync)
                    idle = DateTime.UtcNow - _lastActivityUtc;

                if (idle >= IdleTimeout)
                {
                    if (!IsWarningVisible)
                    {
                        IsWarningVisible = true;
                        IdleWarning?.Invoke(this, EventArgs.Empty);
                    }

                    var overtime = idle - IdleTimeout;
                    if (overtime >= WarningLead)
                    {
                        ForcedLogout?.Invoke(this, EventArgs.Empty);
                        _mode.EnterAutomatic();
                        _states.ResetAutomaticIdle();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }
}
