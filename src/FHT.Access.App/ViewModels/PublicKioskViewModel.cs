using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FHT.Access.App.Services;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Enums;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.App.ViewModels;

/// <summary>
/// Public (student facing) screen. Pure observer of <see cref="AccessStateMachine"/> —
/// recognition itself is driven by <see cref="AutomaticAccessEngine"/>.
/// </summary>
public sealed class PublicKioskViewModel : ViewModelBase, IDisposable
{
    private const string IdleHeadline = "Aproxime-se da câmera para iniciar.";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly WebcamService _webcam;
    private readonly AccessStateMachine _states;
    private readonly AutomaticAccessEngine _engine;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _clockTimer;

    private BitmapSource? _preview;
    private AccessUiState _uiState = AccessUiState.AutomaticIdle;
    private string _resultMessage = string.Empty;
    private string _memberName = string.Empty;
    private string _clockTime = string.Empty;
    private string _clockDate = string.Empty;
    private string _systemStatusText = "SISTEMA OFFLINE";
    private bool _isOnline;
    private bool _subscribed;
    private bool _disposed;

    public PublicKioskViewModel(
        WebcamService webcam,
        AccessStateMachine states,
        AutomaticAccessEngine engine,
        AppSettings settings)
    {
        _webcam = webcam;
        _states = states;
        _engine = engine;
        _settings = settings;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => RefreshClock();
        RefreshClock();
        RefreshOnlineStatus();

        _states.StateChanged += OnStateChanged;

        EnrollFaceCommand = new RelayCommand(() => RequestAttendantFromKiosk?.Invoke(AttendantIntent.Enroll));
        CallAttendantCommand = new RelayCommand(() => RequestAttendantFromKiosk?.Invoke(AttendantIntent.Browse));
        EmergencyReleaseCommand = new RelayCommand(() => RequestAttendantFromKiosk?.Invoke(AttendantIntent.ManualRelease));
        TryAgainCommand = new RelayCommand(() => _engine.RetryFromKiosk());

        ApplyState(_states.State);
    }

    public event Action<AttendantIntent>? RequestAttendantFromKiosk;

    public ICommand EnrollFaceCommand { get; }
    public ICommand CallAttendantCommand { get; }
    public ICommand EmergencyReleaseCommand { get; }
    public ICommand TryAgainCommand { get; }

    public BitmapSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public AccessUiState UiState
    {
        get => _uiState;
        private set
        {
            if (!SetProperty(ref _uiState, value))
                return;

            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsCameraActive));
            OnPropertyChanged(nameof(IsUnknown));
            OnPropertyChanged(nameof(IsDeniedResult));
            OnPropertyChanged(nameof(IsPositiveResult));
        }
    }

    public bool IsIdle => UiState == AccessUiState.AutomaticIdle;

    public bool IsCameraActive => UiState is AccessUiState.FaceDetected
        or AccessUiState.Recognizing
        or AccessUiState.Recognized;

    public bool IsUnknown => UiState == AccessUiState.Unknown;

    public bool IsDeniedResult => UiState == AccessUiState.Denied;

    public bool IsPositiveResult => UiState is AccessUiState.Authorized
        or AccessUiState.WaitingPassage
        or AccessUiState.PassageConfirmed;

    public string IdleMessage => IdleHeadline;

    public string ResultMessage
    {
        get => _resultMessage;
        private set => SetProperty(ref _resultMessage, value);
    }

    public string MemberName
    {
        get => _memberName;
        private set => SetProperty(ref _memberName, value);
    }

    public string ClockTime
    {
        get => _clockTime;
        private set => SetProperty(ref _clockTime, value);
    }

    public string ClockDate
    {
        get => _clockDate;
        private set => SetProperty(ref _clockDate, value);
    }

    public string SystemStatusText
    {
        get => _systemStatusText;
        private set => SetProperty(ref _systemStatusText, value);
    }

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (SetProperty(ref _isOnline, value))
                SystemStatusText = value ? "SISTEMA ONLINE" : "SISTEMA OFFLINE";
        }
    }

    public void Start()
    {
        if (_disposed)
            return;

        if (!_clockTimer.IsEnabled)
            _clockTimer.Start();

        if (_subscribed)
            return;

        _subscribed = true;
        _webcam.FrameReady += OnFrameReady;
        RefreshOnlineStatus();
    }

    public void StopCaptureSubscription()
    {
        if (!_subscribed)
            return;

        _subscribed = false;
        _webcam.FrameReady -= OnFrameReady;
    }

    public void SetOnline(bool online) => IsOnline = online;

    public void RefreshOnlineStatus()
    {
        if (!string.IsNullOrWhiteSpace(_settings.GestaoBaseUrl) && !string.IsNullOrWhiteSpace(_settings.DeviceId))
        {
            var lastMembers = _settings.SyncState?.LastMembersSyncAt;
            var lastEvents = _settings.SyncState?.LastEventsSyncAt;
            var last = lastMembers > lastEvents ? lastMembers : lastEvents;
            IsOnline = last is not null && last > DateTime.UtcNow.AddHours(-24);
            return;
        }

        IsOnline = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _states.StateChanged -= OnStateChanged;
        _clockTimer.Stop();
        StopCaptureSubscription();
    }

    private void OnStateChanged(object? sender, AccessUiState state)
        => _ = _dispatcher.BeginInvoke(() => ApplyState(state));

    private void ApplyState(AccessUiState state)
    {
        UiState = state;
        MemberName = _states.MemberDisplayName ?? string.Empty;
        ResultMessage = BuildResultMessage(state, _states.StatusMessage, _states.MemberDisplayName);
    }

    private static string BuildResultMessage(AccessUiState state, string? statusMessage, string? memberName)
    {
        if (!string.IsNullOrWhiteSpace(statusMessage))
            return statusMessage;

        var name = string.IsNullOrWhiteSpace(memberName) ? null : memberName;

        return state switch
        {
            AccessUiState.Unknown =>
                "Não foi possível identificar seu rosto.",
            AccessUiState.Authorized or AccessUiState.WaitingPassage or AccessUiState.PassageConfirmed => name is null
                ? "Entrada registrada.\nTenha um ótimo treino!"
                : $"Olá, {name}!\n\nEntrada registrada.\nTenha um ótimo treino!",
            AccessUiState.Denied => AccessDecisionEvaluator.PublicReception,
            _ => string.Empty
        };
    }

    private void RefreshClock()
    {
        var now = DateTime.Now;
        ClockTime = now.ToString("HH:mm", PtBr);
        var day = now.ToString("ddd", PtBr).ToUpperInvariant().TrimEnd('.');
        ClockDate = $"{now:dd/MM/yyyy} • {day}";
    }

    private void OnFrameReady(object? sender, WebcamFrameEventArgs e)
    {
        var bitmap = e.Bitmap;
        _ = _dispatcher.BeginInvoke(() => Preview = bitmap);
    }
}
