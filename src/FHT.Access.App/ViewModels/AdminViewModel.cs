using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using FHT.Access.App.Services;
using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Enums;
using FHT.Access.Infrastructure.Logging;
using FHT.Access.Infrastructure.Settings;

namespace FHT.Access.App.ViewModels;

public sealed class MemberOption
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public override string ToString() => Name;
}

public sealed class AdminViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly JsonSettingsStore _store;
    private readonly DeviceService _deviceService;
    private readonly MemberSyncService _memberSync;
    private readonly OfflineSyncService _offlineSync;
    private readonly MemberPhotoSyncService _photoSync;
    private readonly AccessFlowService _flow;
    private readonly RecognitionService _recognition;
    private readonly IFaceRecognitionService _face;
    private readonly TurnstileService _turnstile;
    private readonly IGestaoAccessClient _gestao;
    private readonly IMemberRepository _members;
    private readonly IPendingSyncRepository _pending;
    private readonly WebcamService _webcam;
    private readonly FileLogger _logger;
    private readonly PublicKioskViewModel _kiosk;
    private readonly UpdateService _updateSvc;

    private string _deviceName = string.Empty;
    private string _unitId = string.Empty;
    private string _adminPin = "1234";
    private bool _kioskPortrait = true;
    private bool _startWithWindows;
    private string _gestaoBaseUrl = string.Empty;
    private string _deviceId = string.Empty;
    private string _deviceSecret = string.Empty;
    private int _webcamIndex;
    private int _webcamIndexExit = -1;
    private string _exitMode = "free";
    private bool _freeGateMode;
    private bool _cameraFlipHorizontal;
    private bool _cameraFlipVertical;
    private int _cameraRotateDegrees;
    private BitmapSource? _webcamPreview;
    private BitmapSource? _stillPreview;
    private double _faceThreshold = 0.92;
    private MemberOption? _selectedMember;
    private bool _selectedMemberHasFace;
    private string _faceStatus = string.Empty;
    private bool _useFakeTurnstile = true;
    private string _turnstileNetwork = string.Empty;
    private string _turnstileIp = string.Empty;
    private string _turnstileSerial = string.Empty;
    private string _turnstileStateText = "Disconnected";
    private int _pendingCount;
    private string _lastMembersSync = "—";
    private string _lastEventsSync = "—";
    private string _statusMessage = string.Empty;
    private string _logTail = string.Empty;
    private string _networkNote = string.Empty;

    public AdminViewModel(
        AppSettings settings,
        JsonSettingsStore store,
        DeviceService deviceService,
        MemberSyncService memberSync,
        OfflineSyncService offlineSync,
        MemberPhotoSyncService photoSync,
        AccessFlowService flow,
        RecognitionService recognition,
        IFaceRecognitionService face,
        TurnstileService turnstile,
        IGestaoAccessClient gestao,
        IMemberRepository members,
        IPendingSyncRepository pending,
        WebcamService webcam,
        FileLogger logger,
        PublicKioskViewModel kiosk,
        UpdateService updateSvc)
    {
        _settings = settings;
        _store = store;
        _deviceService = deviceService;
        _memberSync = memberSync;
        _offlineSync = offlineSync;
        _photoSync = photoSync;
        _flow = flow;
        _recognition = recognition;
        _face = face;
        _turnstile = turnstile;
        _gestao = gestao;
        _members = members;
        _pending = pending;
        _webcam = webcam;
        _logger = logger;
        _kiosk = kiosk;
        _updateSvc = updateSvc;

        Members = new ObservableCollection<MemberOption>();
        PassageLog = new ObservableCollection<string>();

        SaveGeralCommand = new AsyncRelayCommand(SaveGeralAsync);
        TestAuthCommand = new AsyncRelayCommand(TestAuthAsync);
        SyncMembersCommand = new AsyncRelayCommand(SyncMembersAsync);
        StartWebcamCommand = new RelayCommand(StartWebcam);
        StopWebcamCommand = new RelayCommand(StopWebcam);
        CaptureStillCommand = new RelayCommand(CaptureStill);
        EnrollCommand = new AsyncRelayCommand(EnrollAsync);
        RemoveFaceCommand = new AsyncRelayCommand(RemoveFaceAsync);
        IdentifyTestCommand = new AsyncRelayCommand(IdentifyTestAsync);
        ConnectTurnstileCommand = new AsyncRelayCommand(ConnectTurnstileAsync);
        DisconnectTurnstileCommand = new AsyncRelayCommand(DisconnectTurnstileAsync);
        ReleaseEntryCommand = new AsyncRelayCommand(ReleaseEntryAsync);
        ReleaseExitCommand = new AsyncRelayCommand(ReleaseExitAsync);
        FlushPendingCommand = new AsyncRelayCommand(FlushPendingAsync);
        RefreshSyncCommand = new AsyncRelayCommand(RefreshSyncAsync);
        RefreshDiagnosticsCommand = new RelayCommand(RefreshDiagnostics);
        ReturnToKioskCommand = new RelayCommand(() => RequestReturnToKiosk?.Invoke());
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync);

        _turnstile.StateChanged += OnTurnstileStateChanged;
        _turnstile.PassageReceived += OnPassageReceived;
        _webcam.FrameReady += OnWebcamFrame;

        LoadFromSettings();
        _ = RefreshSyncAsync();
        RefreshDiagnostics();
        UpdateNetworkNote();
    }

    public event Action? RequestReturnToKiosk;

    public ObservableCollection<MemberOption> Members { get; }
    public ObservableCollection<string> PassageLog { get; }

    public AsyncRelayCommand SaveGeralCommand { get; }
    public AsyncRelayCommand TestAuthCommand { get; }
    public AsyncRelayCommand SyncMembersCommand { get; }
    public RelayCommand StartWebcamCommand { get; }
    public RelayCommand StopWebcamCommand { get; }
    public RelayCommand CaptureStillCommand { get; }
    public AsyncRelayCommand EnrollCommand { get; }
    public AsyncRelayCommand RemoveFaceCommand { get; }
    public AsyncRelayCommand IdentifyTestCommand { get; }
    public AsyncRelayCommand ConnectTurnstileCommand { get; }
    public AsyncRelayCommand DisconnectTurnstileCommand { get; }
    public AsyncRelayCommand ReleaseEntryCommand { get; }
    public AsyncRelayCommand ReleaseExitCommand { get; }
    public AsyncRelayCommand FlushPendingCommand { get; }
    public AsyncRelayCommand RefreshSyncCommand { get; }
    public RelayCommand RefreshDiagnosticsCommand { get; }
    public RelayCommand ReturnToKioskCommand { get; }
    public AsyncRelayCommand CheckUpdateCommand { get; }

    public string DeviceName
    {
        get => _deviceName;
        set => SetProperty(ref _deviceName, value);
    }

    public string UnitId
    {
        get => _unitId;
        set => SetProperty(ref _unitId, value);
    }

    public string AdminPin
    {
        get => _adminPin;
        set => SetProperty(ref _adminPin, value);
    }

    public bool KioskPortrait
    {
        get => _kioskPortrait;
        set => SetProperty(ref _kioskPortrait, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public string GestaoBaseUrl
    {
        get => _gestaoBaseUrl;
        set
        {
            if (SetProperty(ref _gestaoBaseUrl, value))
                UpdateNetworkNote();
        }
    }

    public string DeviceId
    {
        get => _deviceId;
        set => SetProperty(ref _deviceId, value);
    }

    public string DeviceSecret
    {
        get => _deviceSecret;
        set => SetProperty(ref _deviceSecret, value);
    }

    public int WebcamIndex
    {
        get => _webcamIndex;
        set => SetProperty(ref _webcamIndex, value);
    }

    /// <summary>Second camera index for exit lane (-1 = disabled).</summary>
    public int WebcamIndexExit
    {
        get => _webcamIndexExit;
        set => SetProperty(ref _webcamIndexExit, value);
    }

    /// <summary>Exit control: "free" or "facial" (requires second camera).</summary>
    public string ExitMode
    {
        get => _exitMode;
        set => SetProperty(ref _exitMode, value);
    }

    /// <summary>Catraca livre: registra facial sem validar já-dentro / sem-entrada.</summary>
    public bool FreeGateMode
    {
        get => _freeGateMode;
        set => SetProperty(ref _freeGateMode, value);
    }

    public bool CameraFlipHorizontal
    {
        get => _cameraFlipHorizontal;
        set => SetProperty(ref _cameraFlipHorizontal, value);
    }

    public bool CameraFlipVertical
    {
        get => _cameraFlipVertical;
        set => SetProperty(ref _cameraFlipVertical, value);
    }

    public int CameraRotateDegrees
    {
        get => _cameraRotateDegrees;
        set => SetProperty(ref _cameraRotateDegrees, value);
    }

    public BitmapSource? WebcamPreview
    {
        get => _webcamPreview;
        private set => SetProperty(ref _webcamPreview, value);
    }

    public BitmapSource? StillPreview
    {
        get => _stillPreview;
        private set => SetProperty(ref _stillPreview, value);
    }

    public double FaceThreshold
    {
        get => _faceThreshold;
        set => SetProperty(ref _faceThreshold, value);
    }

    public MemberOption? SelectedMember
    {
        get => _selectedMember;
        set
        {
            if (!SetProperty(ref _selectedMember, value))
                return;
            _ = RefreshSelectedMemberFaceStatusAsync();
        }
    }

    public bool SelectedMemberHasFace
    {
        get => _selectedMemberHasFace;
        private set => SetProperty(ref _selectedMemberHasFace, value);
    }

    public string FaceStatus
    {
        get => _faceStatus;
        private set => SetProperty(ref _faceStatus, value);
    }

    public bool UseFakeTurnstile
    {
        get => _useFakeTurnstile;
        set => SetProperty(ref _useFakeTurnstile, value);
    }

    public string TurnstileNetwork
    {
        get => _turnstileNetwork;
        set => SetProperty(ref _turnstileNetwork, value);
    }

    public string TurnstileIp
    {
        get => _turnstileIp;
        set => SetProperty(ref _turnstileIp, value);
    }

    public string TurnstileSerial
    {
        get => _turnstileSerial;
        set => SetProperty(ref _turnstileSerial, value);
    }

    public string TurnstileStateText
    {
        get => _turnstileStateText;
        private set => SetProperty(ref _turnstileStateText, value);
    }

    public int PendingCount
    {
        get => _pendingCount;
        private set => SetProperty(ref _pendingCount, value);
    }

    public string LastMembersSync
    {
        get => _lastMembersSync;
        private set => SetProperty(ref _lastMembersSync, value);
    }

    public string LastEventsSync
    {
        get => _lastEventsSync;
        private set => SetProperty(ref _lastEventsSync, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LogTail
    {
        get => _logTail;
        private set => SetProperty(ref _logTail, value);
    }

    public string NetworkNote
    {
        get => _networkNote;
        private set => SetProperty(ref _networkNote, value);
    }

    public string AppVersion => _updateSvc.CurrentVersion;
    public string UpdateStatusText => _updateSvc.State switch
    {
        UpdateUiState.None => "Sem atualização pendente.",
        UpdateUiState.Available => $"Versão {_updateSvc.AvailableVersion} disponível — agendada fora do expediente.",
        UpdateUiState.Countdown => $"Atualização em {_updateSvc.CountdownRemaining}s...",
        UpdateUiState.Downloading => $"Baixando {_updateSvc.DownloadPercent}%...",
        UpdateUiState.Applying => "Aplicando — reiniciando...",
        _ => string.Empty
    };

    private async Task CheckUpdateAsync()
    {
        StatusMessage = "Verificando atualização...";
        try
        {
            await _updateSvc.CheckNowAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(UpdateStatusText));
            StatusMessage = UpdateStatusText;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao verificar: {ex.Message}";
        }
    }

    public async Task InitializeAsync()
    {
        await LoadMembersAsync().ConfigureAwait(true);
        await RefreshSyncAsync().ConfigureAwait(true);
        TurnstileStateText = _turnstile.State.ToString();
    }

    public void Dispose()
    {
        _turnstile.StateChanged -= OnTurnstileStateChanged;
        _turnstile.PassageReceived -= OnPassageReceived;
        _webcam.FrameReady -= OnWebcamFrame;
    }

    private void LoadFromSettings()
    {
        DeviceName = _settings.Device?.Name ?? string.Empty;
        UnitId = _settings.UnitId;
        AdminPin = string.IsNullOrWhiteSpace(_settings.AdminPin) ? "1234" : _settings.AdminPin;
        KioskPortrait = _settings.KioskPortrait;
        StartWithWindows = _settings.StartWithWindows;
        GestaoBaseUrl = _settings.GestaoBaseUrl;
        DeviceId = _settings.DeviceId;
        DeviceSecret = _settings.DeviceSecret;
        WebcamIndex = _settings.WebcamIndex;
        WebcamIndexExit = _settings.WebcamIndexExit;
        ExitMode = string.IsNullOrWhiteSpace(_settings.ExitMode) ? "free" : _settings.ExitMode;
        FreeGateMode = _settings.FreeGateMode;
        CameraFlipHorizontal = _settings.CameraFlipHorizontal;
        CameraFlipVertical = _settings.CameraFlipVertical;
        CameraRotateDegrees = _settings.CameraRotateDegrees;
        FaceThreshold = _settings.FaceMatchThreshold;
        UseFakeTurnstile = _settings.UseFakeTurnstile;
        TurnstileNetwork = _settings.TurnstileNetwork;
        TurnstileIp = _settings.TurnstileIp;
        TurnstileSerial = _settings.TurnstileSerial;
        TurnstileStateText = _turnstile.State.ToString();
    }

    private async Task SaveGeralAsync()
    {
        try
        {
            _settings.UnitId = UnitId.Trim();
            _settings.AdminPin = AdminPin;
            _settings.KioskPortrait = KioskPortrait;
            _settings.StartWithWindows = StartWithWindows;
            _settings.GestaoBaseUrl = GestaoBaseUrl.Trim();
            _settings.DeviceId = DeviceId.Trim();
            _settings.DeviceSecret = DeviceSecret;
            _settings.WebcamIndex = WebcamIndex;
            _settings.WebcamIndexExit = WebcamIndexExit;
            _settings.ExitMode = string.IsNullOrWhiteSpace(ExitMode) ? "free" : ExitMode.Trim();
            _settings.FreeGateMode = FreeGateMode;
            _flow.FreeGateMode = FreeGateMode;
            _settings.CameraFlipHorizontal = CameraFlipHorizontal;
            _settings.CameraFlipVertical = CameraFlipVertical;
            _settings.CameraRotateDegrees = CameraRotateDegrees;
            _settings.FaceMatchThreshold = FaceThreshold;
            _settings.UseFakeTurnstile = UseFakeTurnstile;
            _settings.TurnstileNetwork = TurnstileNetwork.Trim();
            _settings.TurnstileIp = TurnstileIp.Trim();
            _settings.TurnstileSerial = TurnstileSerial.Trim();

            var device = await _deviceService.GetDeviceAsync().ConfigureAwait(true) ?? new Device
            {
                Id = Guid.TryParse(DeviceId, out var id) ? id : Guid.NewGuid()
            };
            device.Name = DeviceName.Trim();
            device.UnitId = UnitId.Trim();
            device.Serial = string.IsNullOrWhiteSpace(TurnstileSerial) ? device.Serial : TurnstileSerial.Trim();
            device.IpAddress = string.IsNullOrWhiteSpace(TurnstileIp) ? device.IpAddress : TurnstileIp.Trim();
            await _deviceService.SaveDeviceAsync(device).ConfigureAwait(true);

            await _deviceService.SaveTurnstileConfigAsync(new TurnstileConfig
            {
                NetworkInterface = TurnstileNetwork.Trim(),
                BoardIp = TurnstileIp.Trim(),
                Serial = TurnstileSerial.Trim(),
                UseFake = UseFakeTurnstile
            }).ConfigureAwait(true);

            await _store.SaveAppSettingsAsync(_settings).ConfigureAwait(true);

            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    WindowsStartupHelper.Apply(StartWithWindows, exe);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Falha ao gravar início com Windows: {ex.Message}");
            }

            StatusMessage = "Configurações salvas.";
            _logger.Information("Admin settings saved.");
            UpdateNetworkNote();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao salvar: {ex.Message}";
            _logger.Error(ex.Message);
        }
    }

    private async Task TestAuthAsync()
    {
        try
        {
            await ApplyGestaoUrlToSettingsAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(GestaoBaseUrl))
                throw new InvalidOperationException("Informe a Base URL (ex.: http://localhost:4010).");
            if (string.IsNullOrWhiteSpace(DeviceId) || string.IsNullOrWhiteSpace(DeviceSecret))
                throw new InvalidOperationException("Informe Device ID e Device Secret.");
            var result = await _gestao
                .AuthenticateDeviceAsync(DeviceId.Trim(), DeviceSecret)
                .ConfigureAwait(true);
            StatusMessage = $"Auth OK — unit {result.UnitId}";
            _kiosk.SetOnline(true);
            _logger.Information("Device auth succeeded.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auth falhou: {ex.Message}";
            _kiosk.SetOnline(false);
            _logger.Error($"Device auth failed: {ex.Message}");
        }
    }

    private async Task SyncMembersAsync()
    {
        try
        {
            await ApplyGestaoUrlToSettingsAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(DeviceId) || string.IsNullOrWhiteSpace(DeviceSecret))
                throw new InvalidOperationException("Informe Device ID e Device Secret.");
            await _gestao.AuthenticateDeviceAsync(DeviceId.Trim(), DeviceSecret).ConfigureAwait(true);
            var count = await _memberSync.SyncAsync(UnitId.Trim(), full: true).ConfigureAwait(true);
            StatusMessage = $"Sync members: {count} atualizado(s).";
            _kiosk.SetOnline(true);
            await LoadMembersAsync().ConfigureAwait(true);
            await RefreshSyncAsync().ConfigureAwait(true);
            _logger.Information($"Members sync: {count}");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync falhou: {ex.Message}";
            _kiosk.SetOnline(false);
            _logger.Error($"Members sync failed: {ex.Message}");
        }
    }

    private void StartWebcam()
    {
        try
        {
            _webcam.Start(WebcamIndex);
            StatusMessage = $"Webcam {WebcamIndex} iniciada.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error(ex.Message);
        }
    }

    private void StopWebcam()
    {
        _webcam.Stop();
        StatusMessage = "Webcam parada.";
    }

    private void CaptureStill()
    {
        var jpeg = _webcam.GetJpegFrame();
        if (jpeg is null)
        {
            StatusMessage = "Sem frame disponível.";
            return;
        }

        StillPreview = LoadBitmap(jpeg);
        StatusMessage = "Frame capturado.";
    }

    private async Task EnrollAsync()
    {
        if (SelectedMember is null)
        {
            FaceStatus = "Selecione um aluno.";
            return;
        }

        var jpeg = _webcam.GetJpegFrame();
        if (jpeg is null)
        {
            FaceStatus = "Sem frame da webcam.";
            return;
        }

        try
        {
            await _recognition.EnrollAsync(SelectedMember.Id, jpeg).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(UnitId))
            {
                await _photoSync
                    .EnqueueAndTryUploadAsync(UnitId.Trim(), SelectedMember.Id, jpeg)
                    .ConfigureAwait(true);
            }

            FaceStatus = $"Facial cadastrada: {SelectedMember.Name}";
            SelectedMemberHasFace = true;
            _logger.Information($"Face enrolled for {SelectedMember.Id}");
        }
        catch (Exception ex)
        {
            FaceStatus = $"Enroll falhou: {ex.Message}";
            _logger.Error(ex.Message);
        }
    }

    private async Task RemoveFaceAsync()
    {
        if (SelectedMember is null)
        {
            FaceStatus = "Selecione um aluno.";
            return;
        }

        try
        {
            var existing = await _members.GetFaceAsync(SelectedMember.Id).ConfigureAwait(true);
            if (existing is null)
            {
                FaceStatus = $"{SelectedMember.Name} não tem facial cadastrada.";
                SelectedMemberHasFace = false;
                return;
            }

            await _recognition.RemoveAsync(SelectedMember.Id).ConfigureAwait(true);
            FaceStatus = $"Facial removida: {SelectedMember.Name}";
            SelectedMemberHasFace = false;
            _logger.Information($"Face removed for {SelectedMember.Id}");
        }
        catch (Exception ex)
        {
            FaceStatus = $"Remoção falhou: {ex.Message}";
            _logger.Error(ex.Message);
        }
    }

    private async Task IdentifyTestAsync()
    {
        var jpeg = _webcam.GetJpegFrame();
        if (jpeg is null)
        {
            FaceStatus = "Sem frame da webcam.";
            return;
        }

        try
        {
            var match = await _face.IdentifyAsync(jpeg).ConfigureAwait(true);
            FaceStatus = match is null
                ? "Nenhuma correspondência."
                : $"Match {match.MemberId} score={match.Score:F3}";
        }
        catch (Exception ex)
        {
            FaceStatus = $"Identify falhou: {ex.Message}";
            _logger.Error(ex.Message);
        }
    }

    private async Task ConnectTurnstileAsync()
    {
        try
        {
            var config = new TurnstileConfig
            {
                NetworkInterface = string.IsNullOrWhiteSpace(TurnstileNetwork)
                    ? "Ethernet 2"
                    : TurnstileNetwork.Trim(),
                BoardIp = TurnstileIp.Trim(),
                Serial = TurnstileSerial.Trim(),
                UseFake = UseFakeTurnstile
            };
            await _turnstile.ConnectAsync(config).ConfigureAwait(true);

            // Discovery fills Serial / BoardIp / NetworkInterface (NIC name).
            TurnstileNetwork = config.NetworkInterface;
            TurnstileIp = config.BoardIp;
            TurnstileSerial = config.Serial;
            _settings.TurnstileNetwork = config.NetworkInterface;
            _settings.TurnstileIp = config.BoardIp;
            _settings.TurnstileSerial = config.Serial;
            _store.SaveAppSettings(_settings);

            StatusMessage =
                $"Catraca conectada. NIC={config.NetworkInterface} IP={config.BoardIp} Serial={config.Serial}";
            _logger.Information(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connect falhou: {ex.Message}";
            _logger.Error(ex.Message);
        }
    }

    private async Task DisconnectTurnstileAsync()
    {
        try
        {
            await _turnstile.DisconnectAsync().ConfigureAwait(true);
            StatusMessage = "Catraca desconectada.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error(ex.Message);
        }
    }

    private async Task ReleaseEntryAsync()
    {
        if (_turnstile.State is not TurnstileConnectionState.Connected
            and not TurnstileConnectionState.WaitingPassage)
        {
            StatusMessage = "Catraca não conectada — aguarde Connected.";
            return;
        }

        try
        {
            var result = await _flow
                .ProcessManualReleaseAsync(SelectedMember?.Id, SelectedMember?.Name, "admin_entry")
                .ConfigureAwait(true);
            StatusMessage = result.UiMessage;
            AppendPassage($"[{DateTime.Now:HH:mm:ss}] ReleaseEntry event={result.Event?.Id}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error(ex.Message);
        }
    }

    private async Task ReleaseExitAsync()
    {
        if (_turnstile.State is not TurnstileConnectionState.Connected
            and not TurnstileConnectionState.WaitingPassage)
        {
            StatusMessage = "Catraca não conectada — aguarde Connected.";
            return;
        }

        try
        {
            var result = await _flow
                .ProcessManualExitAsync(SelectedMember?.Id, SelectedMember?.Name)
                .ConfigureAwait(true);
            StatusMessage = result.UiMessage;
            AppendPassage($"[{DateTime.Now:HH:mm:ss}] ReleaseExit event={result.Event?.Id}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _logger.Error(ex.Message);
        }
    }

    private async Task FlushPendingAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(DeviceId) && !string.IsNullOrWhiteSpace(DeviceSecret))
            {
                await _gestao
                    .EnsureAuthenticatedAsync(DeviceId.Trim(), DeviceSecret.Trim())
                    .ConfigureAwait(true);
            }

            var n = 0;
            var photos = 0;
            try
            {
                n = await _offlineSync.FlushAsync(UnitId.Trim()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Flush eventos: {ex.Message}");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(UnitId))
                    photos = await _photoSync.FlushAsync(UnitId.Trim()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Flush fotos: {ex.Message}");
            }

            StatusMessage = $"Flush: {n} evento(s), {photos} foto(s).";
            _kiosk.SetOnline(true);
            await RefreshSyncAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Flush falhou: {ex.Message}";
            _kiosk.SetOnline(false);
            _logger.Error(ex.Message);
        }
    }

    private async Task RefreshSyncAsync()
    {
        try
        {
            var pending = await _pending.GetPendingAsync(500).ConfigureAwait(true);
            PendingCount = pending.Count;

            var sync = await _deviceService.GetSyncStateAsync().ConfigureAwait(true);
            LastMembersSync = sync.LastMembersSyncAt?.ToLocalTime().ToString("g") ?? "—";
            LastEventsSync = sync.LastEventsSyncAt?.ToLocalTime().ToString("g") ?? "—";

            _settings.SyncState ??= new SyncStateSettings();
            _settings.SyncState.LastMembersSyncAt = sync.LastMembersSyncAt;
            _settings.SyncState.LastEventsSyncAt = sync.LastEventsSyncAt;
            _kiosk.RefreshOnlineStatus();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void RefreshDiagnostics()
    {
        try
        {
            var path = _logger.LogFilePath;
            if (!File.Exists(path))
            {
                LogTail = "(sem logs ainda)";
                return;
            }

            var lines = File.ReadAllLines(path);
            var take = Math.Min(80, lines.Length);
            var sb = new StringBuilder();
            for (var i = lines.Length - take; i < lines.Length; i++)
                sb.AppendLine(lines[i]);
            LogTail = sb.ToString();
        }
        catch (Exception ex)
        {
            LogTail = ex.Message;
        }
    }

    private async Task LoadMembersAsync()
    {
        var list = await _members.GetAllAsync().ConfigureAwait(true);
        Members.Clear();
        foreach (var m in list.OrderBy(x => x.Name))
            Members.Add(new MemberOption { Id = m.Id, Name = m.Name });

        if (SelectedMember is null && Members.Count > 0)
            SelectedMember = Members[0];
        else
            await RefreshSelectedMemberFaceStatusAsync().ConfigureAwait(true);
    }

    private async Task RefreshSelectedMemberFaceStatusAsync()
    {
        if (SelectedMember is null)
        {
            SelectedMemberHasFace = false;
            return;
        }

        var face = await _members.GetFaceAsync(SelectedMember.Id).ConfigureAwait(true);
        SelectedMemberHasFace = face is not null;
    }

    private async Task ApplyGestaoUrlToSettingsAsync()
    {
        _settings.GestaoBaseUrl = GestaoBaseUrl.Trim();
        _settings.DeviceId = DeviceId.Trim();
        _settings.DeviceSecret = DeviceSecret;
        _settings.UnitId = UnitId.Trim();
        await _store.SaveAppSettingsAsync(_settings).ConfigureAwait(true);
    }

    private void UpdateNetworkNote()
    {
        NetworkNote = string.IsNullOrWhiteSpace(GestaoBaseUrl)
            ? "Configure a Base URL da Gestão para autenticação e sync."
            : $"Base URL: {GestaoBaseUrl.Trim().TrimEnd('/')} — conectividade depende de rede local/internet.";
    }

    private void OnWebcamFrame(object? sender, WebcamFrameEventArgs e)
    {
        var bitmap = e.Bitmap;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => WebcamPreview = bitmap);
    }

    private void OnTurnstileStateChanged(object? sender, TurnstileConnectionState state)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            TurnstileStateText = state.ToString();
            AppendPassage($"[{DateTime.Now:HH:mm:ss}] State={state}");
        });
    }

    private void OnPassageReceived(object? sender, PassageOutcome outcome)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            AppendPassage($"[{DateTime.Now:HH:mm:ss}] Passage={outcome}");
            StatusMessage = outcome == PassageOutcome.PassageDetected
                ? "PassageDetected"
                : $"Passage: {outcome}";
        });
    }

    private void AppendPassage(string line)
    {
        PassageLog.Insert(0, line);
        while (PassageLog.Count > 100)
            PassageLog.RemoveAt(PassageLog.Count - 1);
    }

    private static BitmapSource? LoadBitmap(byte[] jpeg)
    {
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(jpeg);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
