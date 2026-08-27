using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FHT.Access.App.Services;
using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Enums;
using FHT.Access.Infrastructure.Logging;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.App.ViewModels;

/// <summary>What the attendant intends to do with the member picked on the search screen.</summary>
public enum AttendantIntent
{
    Browse = 0,
    Enroll = 1,
    ManualRelease = 2
}

public sealed class MemberSearchResult
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? PhotoUrl { get; init; }
    public string CpfDisplay { get; init; } = "—";
    public string RegistrationDisplay { get; init; } = "—";
    public string PlanLabel { get; init; } = "Inativo";
    public bool PlanOk { get; init; }
    public bool HasFace { get; init; }
    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoUrl);
    public string FaceStatusLabel => HasFace ? "Cadastrado" : "Não cadastrado";
    public string FaceLabel => HasFace ? "Facial cadastrada" : "Sem facial";
}

/// <summary>
/// Attendant workstation: login → dashboard → search → enrollment / manual release.
/// </summary>
public sealed class AttendantShellViewModel : ViewModelBase, IDisposable
{
    public const string CameraOwner = "attendant-shell";

    private readonly OperatingModeService _mode;
    private readonly AccessStateMachine _states;
    private readonly AttendantSessionService _session;
    private readonly CameraCoordinator _camera;
    private readonly Domain.Abstractions.IMemberRepository _members;
    private readonly MemberSyncService _memberSync;
    private readonly IGestaoAccessClient _gestao;
    private readonly RecognitionService _recognition;
    private readonly AccessFlowService _flow;
    private readonly TurnstileService _turnstile;
    private readonly DeviceService _deviceService;
    private readonly IPendingSyncRepository _pendingSync;
    private readonly MemberPhotoSyncService _photoSync;
    private readonly WebcamService _webcam;
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;
    private readonly AutomaticAccessEngine _accessEngine;
    private readonly Dispatcher _dispatcher;

    private AccessUiState _screen = AccessUiState.AttendantLogin;
    private AttendantIntent _intent = AttendantIntent.Browse;
    private string _username = string.Empty;
    private string _pin = string.Empty;
    private bool _isPasswordVisible;
    private string _loginError = string.Empty;
    private string _searchQuery = string.Empty;
    private string _searchStatus = string.Empty;
    private string _selectedMemberName = string.Empty;
    private Guid? _selectedMemberId;
    private string _enrollStatus = string.Empty;
    private string _releaseStatus = string.Empty;
    private string _selectedReason = string.Empty;
    private string _turnstileStatusText = "Catraca: —";
    private string _cameraStatusText = "Câmera: —";
    private string _gestaoStatusText = "Gestão: —";
    private string _lastSyncText = "Último sync: —";
    private string _turnstileValue = "desconectada";
    private bool _turnstileOk;
    private string _cameraValue = "indisponível";
    private bool _cameraOk;
    private string _gestaoValue = "offline";
    private bool _gestaoOk;
    private string _lastSyncValue = "—";
    private bool _lastSyncOk;
    private int _pendingEventsCount;
    private BitmapSource? _enrollPreview;
    private bool _isSettingsVisible;
    private bool _isIdleWarningVisible;
    private bool _isBusy;
    private bool _previewSubscribed;
    private bool _disposed;
    private bool _didSearch;
    private int _searchSeq;
    private bool _suppressSearch;
    private bool _isSearchDropdownOpen;
    private MemberSearchResult? _pickedMember;
    private DispatcherTimer? _returnToKioskTimer;

    public AttendantShellViewModel(
        OperatingModeService mode,
        AccessStateMachine states,
        AttendantSessionService session,
        CameraCoordinator camera,
        Domain.Abstractions.IMemberRepository members,
        MemberSyncService memberSync,
        IGestaoAccessClient gestao,
        RecognitionService recognition,
        AccessFlowService flow,
        TurnstileService turnstile,
        DeviceService deviceService,
        WebcamService webcam,
        IPendingSyncRepository pendingSync,
        MemberPhotoSyncService photoSync,
        AppSettings settings,
        FileLogger logger,
        AdminViewModel adminViewModel,
        AutomaticAccessEngine accessEngine)
    {
        _mode = mode;
        _states = states;
        _session = session;
        _camera = camera;
        _members = members;
        _memberSync = memberSync;
        _gestao = gestao;
        _recognition = recognition;
        _flow = flow;
        _turnstile = turnstile;
        _deviceService = deviceService;
        _webcam = webcam;
        _pendingSync = pendingSync;
        _photoSync = photoSync;
        _settings = settings;
        _logger = logger;
        _accessEngine = accessEngine;
        AdminViewModel = adminViewModel;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Results = new ObservableCollection<MemberSearchResult>();
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(ShowNoResults));
        };
        ReasonOptions = new ObservableCollection<string>
        {
            "Primeiro acesso",
            "Falha na câmera",
            "Cadastro facial pendente",
            "Visitante autorizado",
            "Liberação administrativa"
        };
        _selectedReason = ReasonOptions[0];

        LoginCommand = new RelayCommand(Login);
        TogglePasswordVisibilityCommand = new RelayCommand(() => IsPasswordVisible = !IsPasswordVisible);
        GoEnrollCommand = new RelayCommand(() => GoToSearch(AttendantIntent.Enroll));
        GoSearchCommand = new RelayCommand(() => GoToSearch(AttendantIntent.Browse));
        GoManualReleaseCommand = new RelayCommand(() => GoToSearch(AttendantIntent.ManualRelease));
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        EndAttendanceCommand = new RelayCommand(EndAttendance);
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        PickSearchResultCommand = new RelayCommand(PickSearchResult);
        SelectMemberCommand = new RelayCommand(SelectMember);
        SkipMemberCommand = new RelayCommand(() => GoToManualRelease(null, null));
        BackToDashboardCommand = new RelayCommand(GoToDashboard);
        ReturnToKioskCommand = new RelayCommand(ReturnToKiosk);
        CaptureCommand = new AsyncRelayCommand(CaptureAsync);
        EnrollAnotherCommand = new RelayCommand(EnrollAnother);
        ReleaseNowCommand = new AsyncRelayCommand(() => ReleaseAsync("Cadastro facial realizado"));
        ConfirmManualReleaseCommand = new AsyncRelayCommand(() => ReleaseAsync(SelectedReason));
        ContinueAttendingCommand = new RelayCommand(ContinueAttending);

        _session.IdleWarning += OnIdleWarning;
        _session.ForcedLogout += OnForcedLogout;
        _turnstile.StateChanged += OnTurnstileStateChanged;
        AdminViewModel.RequestReturnToKiosk += OnAdminReturnToKiosk;

        RefreshStatusLines();
    }

    public event EventHandler? SessionEnded;

    public AdminViewModel AdminViewModel { get; }

    public ObservableCollection<MemberSearchResult> Results { get; }
    public ObservableCollection<string> ReasonOptions { get; }

    public RelayCommand LoginCommand { get; }
    public RelayCommand TogglePasswordVisibilityCommand { get; }
    public RelayCommand GoEnrollCommand { get; }
    public RelayCommand GoSearchCommand { get; }
    public RelayCommand GoManualReleaseCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand CloseSettingsCommand { get; }
    public RelayCommand EndAttendanceCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand PickSearchResultCommand { get; }
    public RelayCommand SelectMemberCommand { get; }
    public RelayCommand SkipMemberCommand { get; }
    public RelayCommand BackToDashboardCommand { get; }
    public RelayCommand ReturnToKioskCommand { get; }
    public AsyncRelayCommand CaptureCommand { get; }
    public RelayCommand EnrollAnotherCommand { get; }
    public AsyncRelayCommand ReleaseNowCommand { get; }
    public AsyncRelayCommand ConfirmManualReleaseCommand { get; }
    public RelayCommand ContinueAttendingCommand { get; }

    public AccessUiState Screen
    {
        get => _screen;
        private set
        {
            if (SetProperty(ref _screen, value))
                OnPropertyChanged(nameof(ScreenTitle));
        }
    }

    public string ScreenTitle => Screen switch
    {
        AccessUiState.AttendantLogin => "Modo Atendente",
        AccessUiState.AttendantDashboard => "Atendimento",
        AccessUiState.MemberSearch => Intent switch
        {
            AttendantIntent.Enroll => "Cadastrar facial — buscar aluno",
            AttendantIntent.ManualRelease => "Liberação manual — buscar aluno",
            _ => "Buscar aluno"
        },
        AccessUiState.Enrollment => "Cadastro facial",
        AccessUiState.EnrollmentCompleted => "Cadastro concluído",
        AccessUiState.ManualRelease => "Liberação manual",
        _ => "FHT Acesso"
    };

    public AttendantIntent Intent
    {
        get => _intent;
        private set
        {
            if (SetProperty(ref _intent, value))
            {
                OnPropertyChanged(nameof(ScreenTitle));
                OnPropertyChanged(nameof(ShowSkipMember));
            }
        }
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Pin
    {
        get => _pin;
        set => SetProperty(ref _pin, value);
    }

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set => SetProperty(ref _isPasswordVisible, value);
    }

    public string LoginError
    {
        get => _loginError;
        private set => SetProperty(ref _loginError, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_suppressSearch)
            {
                SetProperty(ref _searchQuery, value);
                OnPropertyChanged(nameof(HasSearchQuery));
                return;
            }

            if (!SetProperty(ref _searchQuery, value))
                return;

            _session.Touch();
            OnPropertyChanged(nameof(HasSearchQuery));

            if (_pickedMember is not null &&
                !string.Equals(_searchQuery.Trim(), _pickedMember.Name, StringComparison.OrdinalIgnoreCase))
            {
                SetPickedMember(null);
            }

            _ = DebouncedSearchAsync();
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(_searchQuery);
    public bool HasResults => Results.Count > 0;
    public bool ShowNoResults => _didSearch && Results.Count == 0 && !HasPickedMember;
    public bool ShowSkipMember => Intent == AttendantIntent.ManualRelease;

    public bool IsSearchDropdownOpen
    {
        get => _isSearchDropdownOpen;
        set => SetProperty(ref _isSearchDropdownOpen, value);
    }

    public MemberSearchResult? PickedMember
    {
        get => _pickedMember;
        set => SetPickedMember(value);
    }

    public bool HasPickedMember => _pickedMember is not null;
    public string PreviewName => _pickedMember?.Name ?? "—";
    public string PreviewCpf => _pickedMember?.CpfDisplay ?? "—";
    public string PreviewPlan => _pickedMember?.PlanLabel ?? "—";
    public string PreviewFace => _pickedMember?.FaceStatusLabel ?? "—";
    public string PreviewRegistration => _pickedMember?.RegistrationDisplay ?? "—";
    public string? PreviewPhotoUrl => _pickedMember?.PhotoUrl;
    public bool PreviewHasPhoto => _pickedMember?.HasPhoto == true;
    public bool PreviewPlanOk => _pickedMember?.PlanOk == true;
    public bool PreviewHasFace => _pickedMember?.HasFace == true;

    public string SearchStatus
    {
        get => _searchStatus;
        private set => SetProperty(ref _searchStatus, value);
    }

    public string SelectedMemberName
    {
        get => _selectedMemberName;
        private set => SetProperty(ref _selectedMemberName, value);
    }

    public string EnrollStatus
    {
        get => _enrollStatus;
        private set => SetProperty(ref _enrollStatus, value);
    }

    public string ReleaseStatus
    {
        get => _releaseStatus;
        private set => SetProperty(ref _releaseStatus, value);
    }

    public string SelectedReason
    {
        get => _selectedReason;
        set => SetProperty(ref _selectedReason, value);
    }

    public string TurnstileStatusText
    {
        get => _turnstileStatusText;
        private set => SetProperty(ref _turnstileStatusText, value);
    }

    public string TurnstileValue
    {
        get => _turnstileValue;
        private set => SetProperty(ref _turnstileValue, value);
    }

    public bool TurnstileOk
    {
        get => _turnstileOk;
        private set => SetProperty(ref _turnstileOk, value);
    }

    public string CameraStatusText
    {
        get => _cameraStatusText;
        private set => SetProperty(ref _cameraStatusText, value);
    }

    public string CameraValue
    {
        get => _cameraValue;
        private set => SetProperty(ref _cameraValue, value);
    }

    public bool CameraOk
    {
        get => _cameraOk;
        private set => SetProperty(ref _cameraOk, value);
    }

    public string GestaoStatusText
    {
        get => _gestaoStatusText;
        private set => SetProperty(ref _gestaoStatusText, value);
    }

    public string GestaoValue
    {
        get => _gestaoValue;
        private set => SetProperty(ref _gestaoValue, value);
    }

    public bool GestaoOk
    {
        get => _gestaoOk;
        private set => SetProperty(ref _gestaoOk, value);
    }

    public string LastSyncText
    {
        get => _lastSyncText;
        private set => SetProperty(ref _lastSyncText, value);
    }

    public string LastSyncValue
    {
        get => _lastSyncValue;
        private set => SetProperty(ref _lastSyncValue, value);
    }

    public bool LastSyncOk
    {
        get => _lastSyncOk;
        private set => SetProperty(ref _lastSyncOk, value);
    }

    public int PendingEventsCount
    {
        get => _pendingEventsCount;
        private set => SetProperty(ref _pendingEventsCount, value);
    }

    public BitmapSource? EnrollPreview
    {
        get => _enrollPreview;
        private set
        {
            if (SetProperty(ref _enrollPreview, value))
                OnPropertyChanged(nameof(EnrollmentReady));
        }
    }

    public bool EnrollmentReady => _enrollPreview is not null;

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        private set => SetProperty(ref _isSettingsVisible, value);
    }

    public bool IsIdleWarningVisible
    {
        get => _isIdleWarningVisible;
        private set => SetProperty(ref _isIdleWarningVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            SetProperty(ref _isBusy, value);
        }
    }

    private AttendantIntent _pendingAfterLogin = AttendantIntent.Browse;

    /// <summary>What to open after the PIN succeeds (kiosk unknown-screen actions).</summary>
    public void SetPendingIntent(AttendantIntent intent) => _pendingAfterLogin = intent;

    /// <summary>Puts the shell back on the PIN screen (used when the attendant UI is summoned).</summary>
    public void BeginLogin()
    {
        Pin = string.Empty;
        Username = string.Empty;
        IsPasswordVisible = false;
        LoginError = string.Empty;
        IsSettingsVisible = false;
        IsIdleWarningVisible = false;
        Screen = AccessUiState.AttendantLogin;
        _states.TransitionTo(AccessUiState.AttendantLogin);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _session.IdleWarning -= OnIdleWarning;
        _session.ForcedLogout -= OnForcedLogout;
        _turnstile.StateChanged -= OnTurnstileStateChanged;
        AdminViewModel.RequestReturnToKiosk -= OnAdminReturnToKiosk;
        StopReturnToKioskTimer();
        StopEnrollmentPreview();
    }

    private void Login()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            LoginError = "Informe o usuário.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Pin))
        {
            LoginError = "Informe a senha.";
            return;
        }

        var expected = string.IsNullOrWhiteSpace(_settings.AdminPin) ? "1234" : _settings.AdminPin;
        if (!string.Equals(Pin.Trim(), expected, StringComparison.Ordinal))
        {
            LoginError = "Usuário ou senha inválidos.";
            return;
        }

        Pin = string.Empty;
        Username = string.Empty;
        IsPasswordVisible = false;
        LoginError = string.Empty;
        _mode.EnterAttendant();
        _session.Touch();
        var pending = _pendingAfterLogin;
        _pendingAfterLogin = AttendantIntent.Browse;
        if (pending is AttendantIntent.Enroll or AttendantIntent.ManualRelease)
            GoToSearch(pending);
        else
            GoToDashboard();
        _logger.Information("Attendant session started.");
    }

    private void GoToDashboard()
    {
        _session.Touch();
        StopEnrollmentPreview();
        ReleaseCamera();

        if (_mode.Mode != AccessOperatingMode.Attendant)
            _mode.EnterAttendant();

        Screen = AccessUiState.AttendantDashboard;
        _states.TransitionTo(AccessUiState.AttendantDashboard);
        RefreshStatusLines();
        _ = RefreshDashboardAsync();
    }

    private void GoToSearch(AttendantIntent intent)
    {
        StopReturnToKioskTimer();
        _session.Touch();
        StopEnrollmentPreview();
        ReleaseCamera();

        Intent = intent;
        SetPickedMember(null);
        SearchQuery = string.Empty;
        SearchStatus = string.Empty;
        _didSearch = false;
        IsSearchDropdownOpen = false;
        Results.Clear();
        Screen = AccessUiState.MemberSearch;
        _states.TransitionTo(AccessUiState.MemberSearch);
    }

    private void ClearSearch()
    {
        SetPickedMember(null);
        SearchQuery = string.Empty;
        IsSearchDropdownOpen = false;
    }

    private void PickSearchResult(object? parameter)
    {
        if (parameter is MemberSearchResult result)
            SetPickedMember(result);
    }

    private void SetPickedMember(MemberSearchResult? value)
    {
        if (Equals(_pickedMember, value))
            return;

        _pickedMember = value;
        OnPropertyChanged(nameof(PickedMember));
        OnPropertyChanged(nameof(HasPickedMember));
        OnPropertyChanged(nameof(PreviewName));
        OnPropertyChanged(nameof(PreviewCpf));
        OnPropertyChanged(nameof(PreviewPlan));
        OnPropertyChanged(nameof(PreviewFace));
        OnPropertyChanged(nameof(PreviewRegistration));
        OnPropertyChanged(nameof(PreviewPhotoUrl));
        OnPropertyChanged(nameof(PreviewHasPhoto));
        OnPropertyChanged(nameof(PreviewPlanOk));
        OnPropertyChanged(nameof(PreviewHasFace));
        SelectMemberCommand.RaiseCanExecuteChanged();

        if (value is null)
            return;

        _suppressSearch = true;
        _searchQuery = value.Name;
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(HasSearchQuery));
        _suppressSearch = false;
        IsSearchDropdownOpen = false;
    }

    private async Task DebouncedSearchAsync()
    {
        var seq = Interlocked.Increment(ref _searchSeq);
        await Task.Delay(140).ConfigureAwait(true);
        if (seq != Volatile.Read(ref _searchSeq))
            return;
        await SearchAsync().ConfigureAwait(true);
    }

    private async Task SearchAsync()
    {
        _session.Touch();
        var query = (SearchQuery ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            _didSearch = false;
            SearchStatus = string.Empty;
            IsSearchDropdownOpen = false;
            Results.Clear();
            OnPropertyChanged(nameof(ShowNoResults));
            return;
        }

        try
        {
            var found = await _members.SearchAsync(query, 30).ConfigureAwait(true);

            if (found.Count == 0 && !string.IsNullOrWhiteSpace(_settings.UnitId))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_settings.DeviceId)
                        && !string.IsNullOrWhiteSpace(_settings.DeviceSecret))
                    {
                        await _gestao
                            .EnsureAuthenticatedAsync(_settings.DeviceId.Trim(), _settings.DeviceSecret)
                            .ConfigureAwait(true);
                    }

                    var pulled = await _memberSync
                        .PullByQueryAsync(_settings.UnitId.Trim(), query)
                        .ConfigureAwait(true);
                    if (pulled > 0)
                    {
                        _logger.Information($"Attendant search pulled {pulled} member(s) from Gestão.");
                        found = await _members.SearchAsync(query, 30).ConfigureAwait(true);
                    }
                }
                catch (Exception syncEx)
                {
                    _logger.Warning($"Attendant Gestão pull failed: {syncEx.Message}");
                }
            }

            var faceIds = await _members
                .ListFaceMemberIdsAsync(found.Select(m => m.Id))
                .ConfigureAwait(true);

            Results.Clear();
            foreach (var member in found)
            {
                var plan = DescribePlan(member);
                Results.Add(new MemberSearchResult
                {
                    Id = member.Id,
                    Name = member.Name,
                    PhotoUrl = member.PhotoUrl,
                    HasFace = faceIds.Contains(member.Id),
                    PlanLabel = plan.Label,
                    PlanOk = plan.Ok,
                    CpfDisplay = FormatCpf(member.Cpf),
                    RegistrationDisplay = "—"
                });
            }

            _didSearch = true;
            SearchStatus = Results.Count == 0
                ? "Nenhum aluno encontrado."
                : string.Empty;
            IsSearchDropdownOpen = PickedMember is null && query.Length >= 2;
            OnPropertyChanged(nameof(ShowNoResults));
        }
        catch (Exception ex)
        {
            _didSearch = true;
            SearchStatus = $"Falha na busca: {ex.Message}";
            IsSearchDropdownOpen = false;
            OnPropertyChanged(nameof(ShowNoResults));
            _logger.Error($"Attendant search failed: {ex.Message}");
        }
    }

    private static string FormatCpf(string? cpf)
    {
        var digits = new string((cpf ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 11)
            return string.IsNullOrWhiteSpace(cpf) ? "—" : cpf.Trim();
        return $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}";
    }

    private static (string Label, bool Ok) DescribePlan(Domain.Entities.Member member)
    {
        if (member.Status == MemberStatus.Blocked)
            return ("Bloqueado", false);
        if (member.AccessAllowed)
            return ("Plano vigente", true);
        return ("Sem plano vigente", false);
    }

    private void SelectMember(object? parameter)
    {
        var result = parameter as MemberSearchResult ?? PickedMember;
        if (result is null)
            return;

        _session.Touch();
        _selectedMemberId = result.Id;
        SelectedMemberName = result.Name;

        switch (Intent)
        {
            case AttendantIntent.ManualRelease:
                GoToManualRelease(result.Id, result.Name);
                break;
            default:
                GoToEnrollment();
                break;
        }
    }

    private void GoToEnrollment()
    {
        StopReturnToKioskTimer();
        _session.Touch();
        _mode.EnterEnrollment();
        _camera.TryAcquire(CameraUsageMode.Enrollment, CameraOwner);
        StartEnrollmentPreview();

        EnrollStatus = string.Empty;
        Screen = AccessUiState.Enrollment;
        _states.TransitionTo(AccessUiState.Enrollment, memberDisplayName: SelectedMemberName, memberId: _selectedMemberId);
    }

    private void GoToManualRelease(Guid? memberId, string? memberName)
    {
        _session.Touch();
        StopEnrollmentPreview();
        ReleaseCamera();

        _selectedMemberId = memberId;
        SelectedMemberName = memberName ?? "Sem identificação";
        ReleaseStatus = string.Empty;
        Screen = AccessUiState.ManualRelease;
        _states.TransitionTo(AccessUiState.ManualRelease, memberDisplayName: memberName, memberId: memberId);
    }

    private async Task CaptureAsync()
    {
        _session.Touch();
        if (_selectedMemberId is not { } memberId)
        {
            EnrollStatus = "Selecione um aluno antes de capturar.";
            return;
        }

        var jpeg = _webcam.GetJpegFrame();
        if (jpeg is null || jpeg.Length < 100)
        {
            EnrollStatus = "Sem imagem da câmera. Verifique o dispositivo.";
            return;
        }

        try
        {
            IsBusy = true;
            await _recognition.EnrollAsync(memberId, jpeg).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(_settings.UnitId))
            {
                await _photoSync
                    .EnqueueAndTryUploadAsync(_settings.UnitId.Trim(), memberId, jpeg)
                    .ConfigureAwait(true);
            }

            await _memberSync.RefreshMemberAsync(memberId).ConfigureAwait(true);
            _logger.Information($"Face enrolled for member {memberId}.");

            StopEnrollmentPreview();
            ReleaseCamera();

            var refreshed = await _members.GetByIdAsync(memberId).ConfigureAwait(true);
            var planOk = refreshed is not null && DescribePlan(refreshed).Ok;

            EnrollStatus = !planOk
                ? "Facial cadastrada.\nSem matrícula vigente — a catraca não libera."
                : "Facial cadastrada.\nAproxime-se do totem para entrar.";
            Screen = AccessUiState.EnrollmentCompleted;
            _states.TransitionTo(
                AccessUiState.EnrollmentCompleted,
                memberDisplayName: SelectedMemberName,
                memberId: memberId);
            ScheduleReturnToKiosk(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            EnrollStatus = $"Falha no cadastro: {ex.Message}";
            _logger.Error($"Enrollment failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReleaseAsync(string reason)
    {
        _session.Touch();
        try
        {
            IsBusy = true;
            ReleaseStatus = "Liberando…";
            var result = await _flow
                .ProcessManualReleaseAsync(_selectedMemberId, SelectedMemberName, reason)
                .ConfigureAwait(true);

            ReleaseStatus = result.Passage == PassageOutcome.PassageDetected
                ? "Entrada registrada."
                : "Liberação enviada — passagem não confirmada.";
            _logger.Information($"Manual release ({reason}) for {SelectedMemberName}: {result.Passage}.");
        }
        catch (Exception ex)
        {
            ReleaseStatus = $"Falha na liberação: {ex.Message}";
            _logger.Error($"Manual release failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _session.Touch();
        }
    }

    private void OpenSettings()
    {
        _session.Touch();
        IsSettingsVisible = true;
        _ = AdminViewModel.InitializeAsync();
    }

    private void CloseSettings()
    {
        _session.Touch();
        IsSettingsVisible = false;
        RefreshStatusLines();
    }

    private void EndAttendance()
    {
        StopEnrollmentPreview();
        ReleaseCamera();
        IsSettingsVisible = false;
        IsIdleWarningVisible = false;
        Pin = string.Empty;
        SearchQuery = string.Empty;
        Results.Clear();
        SetPickedMember(null);
        _selectedMemberId = null;
        SelectedMemberName = string.Empty;
        Screen = AccessUiState.AttendantLogin;

        _mode.EnterAutomatic();
        _states.ResetAutomaticIdle();
        _logger.Information("Attendant session ended.");
        SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    private void ContinueAttending()
    {
        _session.ContinueAttending();
        IsIdleWarningVisible = false;
    }

    private void OnIdleWarning(object? sender, EventArgs e)
        => _ = _dispatcher.BeginInvoke(() => IsIdleWarningVisible = true);

    private void OnForcedLogout(object? sender, EventArgs e)
        => _ = _dispatcher.BeginInvoke(() =>
        {
            IsIdleWarningVisible = false;
            StopEnrollmentPreview();
            ReleaseCamera();
            Screen = AccessUiState.AttendantLogin;
            IsSettingsVisible = false;
            SessionEnded?.Invoke(this, EventArgs.Empty);
        });

    private void OnTurnstileStateChanged(object? sender, TurnstileConnectionState state)
        => _ = _dispatcher.BeginInvoke(RefreshStatusLines);

    private void RefreshStatusLines()
    {
        TurnstileOk = _turnstile.State == TurnstileConnectionState.Connected;
        TurnstileValue = TurnstileOk ? "Conectada" : Capitalize(Describe(_turnstile.State));
        TurnstileStatusText = $"Catraca: {Describe(_turnstile.State)}";

        CameraOk = _webcam.IsRunning;
        CameraValue = CameraOk ? "Ativa" : "Indisponível";
        CameraStatusText = CameraOk ? "Câmera: ativa" : "Câmera: indisponível";

        GestaoOk = !string.IsNullOrWhiteSpace(_settings.GestaoBaseUrl);
        GestaoValue = GestaoOk ? "Online" : "Offline";
        GestaoStatusText = GestaoOk
            ? "Gestão: online"
            : "Gestão: não configurada (modo local)";
    }

    private async Task RefreshDashboardAsync()
    {
        await RefreshSyncTextAsync().ConfigureAwait(true);
        try
        {
            var pending = await _pendingSync.GetPendingAsync(500).ConfigureAwait(true);
            PendingEventsCount = pending.Count;
        }
        catch (Exception ex)
        {
            PendingEventsCount = 0;
            _logger.Warning($"Pending events count failed: {ex.Message}");
        }
    }

    private async Task RefreshSyncTextAsync()
    {
        try
        {
            var sync = await _deviceService.GetSyncStateAsync().ConfigureAwait(true);
            var last = sync.LastMembersSyncAt > sync.LastEventsSyncAt
                ? sync.LastMembersSyncAt
                : sync.LastEventsSyncAt;
            LastSyncOk = last is not null;
            LastSyncValue = last is null ? "—" : last.Value.ToLocalTime().ToString("HH:mm");
            LastSyncText = last is null
                ? "Último sync: —"
                : $"Último sync: {last.Value.ToLocalTime():dd/MM HH:mm}";
        }
        catch (Exception ex)
        {
            LastSyncOk = false;
            LastSyncValue = "—";
            LastSyncText = "Último sync: indisponível";
            _logger.Warning($"Sync state read failed: {ex.Message}");
        }
    }

    private void StartEnrollmentPreview()
    {
        if (_previewSubscribed)
            return;

        _previewSubscribed = true;
        _webcam.FrameReady += OnEnrollFrame;

        try
        {
            if (!_webcam.IsRunning)
                _webcam.Start(_settings.WebcamIndex);
        }
        catch (Exception ex)
        {
            EnrollStatus = $"Câmera indisponível: {ex.Message}";
        }
    }

    private void StopEnrollmentPreview()
    {
        if (!_previewSubscribed)
            return;

        _previewSubscribed = false;
        _webcam.FrameReady -= OnEnrollFrame;
        EnrollPreview = null;
    }

    private void ReleaseCamera() => _camera.Release(CameraOwner);

    private void OnEnrollFrame(object? sender, WebcamFrameEventArgs e)
    {
        var bitmap = e.Bitmap;
        _ = _dispatcher.BeginInvoke(() => EnrollPreview = bitmap);
    }

    private static string Capitalize(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private void EnrollAnother()
    {
        StopReturnToKioskTimer();
        _mode.EnterAttendant();
        GoToSearch(AttendantIntent.Enroll);
    }

    private void OnAdminReturnToKiosk()
    {
        CloseSettings();
        ReturnToKiosk();
    }

    private void ReturnToKiosk()
    {
        StopReturnToKioskTimer();
        StopEnrollmentPreview();
        ReleaseCamera();
        IsSettingsVisible = false;
        _mode.EnterAutomatic();
        _states.ResetAutomaticIdle();
        _accessEngine.RetryFromKiosk();
        _logger.Information("Totem reativado (reconhecimento facial ligado).");
    }

    private void ScheduleReturnToKiosk(TimeSpan delay)
    {
        StopReturnToKioskTimer();
        _returnToKioskTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = delay
        };
        _returnToKioskTimer.Tick += (_, _) =>
        {
            StopReturnToKioskTimer();
            ReturnToKiosk();
        };
        _returnToKioskTimer.Start();
    }

    private void StopReturnToKioskTimer()
    {
        if (_returnToKioskTimer is null)
            return;

        _returnToKioskTimer.Stop();
        _returnToKioskTimer = null;
    }

    private static string Describe(TurnstileConnectionState state) => state switch
    {
        TurnstileConnectionState.Connected => "conectada",
        TurnstileConnectionState.Connecting => "conectando…",
        TurnstileConnectionState.Discovering => "procurando…",
        TurnstileConnectionState.Reconnecting => "reconectando…",
        TurnstileConnectionState.WaitingPassage => "aguardando passagem",
        TurnstileConnectionState.Error => "erro",
        _ => "desconectada"
    };
}
