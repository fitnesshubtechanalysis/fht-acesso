using System.Windows.Threading;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Enums;

namespace FHT.Access.App.ViewModels;

/// <summary>
/// Chooses between the public kiosk and the attendant workstation.
/// Automatic mode is public; Attendant/Enrollment (or an explicit attendant request,
/// which happens before the PIN is validated) shows the attendant shell.
/// </summary>
public sealed class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly OperatingModeService _mode;
    private readonly Dispatcher _dispatcher;

    private object _currentViewModel;
    private bool _attendantRequested;
    private bool _disposed;

    public ShellViewModel(
        OperatingModeService mode,
        PublicKioskViewModel publicKiosk,
        AttendantShellViewModel attendant)
    {
        _mode = mode;
        PublicKiosk = publicKiosk;
        Attendant = attendant;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _currentViewModel = publicKiosk;

        _mode.ModeChanged += OnModeChanged;
        Attendant.SessionEnded += OnAttendantSessionEnded;
        PublicKiosk.RequestAttendantFromKiosk += OnKioskRequestedAttendant;
        UpdateContent();
    }

    public event EventHandler? ContentChanged;

    public PublicKioskViewModel PublicKiosk { get; }

    public AttendantShellViewModel Attendant { get; }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (SetProperty(ref _currentViewModel, value))
            {
                OnPropertyChanged(nameof(IsAttendantVisible));
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsAttendantVisible => ReferenceEquals(CurrentViewModel, Attendant);

    /// <summary>Summons the attendant PIN screen (tray menu / Ctrl+Shift+A).</summary>
    public void RequestAttendant()
    {
        if (_mode.Mode is AccessOperatingMode.Attendant or AccessOperatingMode.Enrollment)
        {
            _attendantRequested = true;
            UpdateContent();
            return;
        }

        _attendantRequested = true;
        Attendant.BeginLogin();
        UpdateContent();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _mode.ModeChanged -= OnModeChanged;
        Attendant.SessionEnded -= OnAttendantSessionEnded;
        PublicKiosk.RequestAttendantFromKiosk -= OnKioskRequestedAttendant;
    }

    private void OnKioskRequestedAttendant(AttendantIntent intent)
    {
        Attendant.SetPendingIntent(intent);
        RequestAttendant();
    }

    private void OnAttendantSessionEnded(object? sender, EventArgs e)
    {
        _attendantRequested = false;
        _ = _dispatcher.BeginInvoke(UpdateContent);
    }

    private void OnModeChanged(object? sender, AccessOperatingMode mode)
    {
        if (mode == AccessOperatingMode.Automatic)
            _attendantRequested = false;

        _ = _dispatcher.BeginInvoke(UpdateContent);
    }

    private void UpdateContent()
    {
        var attendantMode = _mode.Mode is AccessOperatingMode.Attendant or AccessOperatingMode.Enrollment;
        var showAttendant = attendantMode || _attendantRequested;

        CurrentViewModel = showAttendant ? Attendant : PublicKiosk;

        if (showAttendant)
            PublicKiosk.StopCaptureSubscription();
        else
            PublicKiosk.Start();
    }
}
