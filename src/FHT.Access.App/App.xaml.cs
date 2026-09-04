using System.IO;
using System.Windows;
using System.Windows.Threading;
using FHT.Access.App.Services;
using FHT.Access.App.ViewModels;
using FHT.Access.Application;
using FHT.Access.Application.Services;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Face;
using FHT.Access.Infrastructure;
using FHT.Access.Infrastructure.Logging;
using FHT.Access.Infrastructure.Persistence;
using FHT.Access.Infrastructure.Settings;
using FHT.Access.Toletus;
using Microsoft.Extensions.DependencyInjection;
using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Services;
using Velopack;

namespace FHT.Access.App;

public partial class App : System.Windows.Application
{
    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FHT", "Access", "boot.log");

    private ServiceProvider? _services;
    private FileLogger? _logger;
    private TrayIconService? _tray;
    private MainWindow? _mainWindow;
    private bool _exiting;

    private static void Boot(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(BootLogPath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(BootLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try { File.WriteAllText(BootLogPath, string.Empty); } catch { /* ignore */ }
        Boot("OnStartup begin");

        if (!SingleInstanceGuard.TryAcquire())
        {
            Boot("second instance — bringing existing window forward");
            SingleInstanceGuard.BringExistingToFront();
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Boot("building ServiceCollection");
            var collection = new ServiceCollection();

            Boot("AddFhtAccessInfrastructure");
            collection.AddFhtAccessInfrastructure();

            // Velopack updater — deve ser o primeiro singleton registrado antes do DI ser construído.
            collection.AddSingleton<IAppUpdater, VelopackAppUpdater>();
            collection.AddSingleton<UpdateService>(sp =>
            {
                var s = sp.GetRequiredService<AppSettings>();
                var log = sp.GetRequiredService<FileLogger>();
                return new UpdateService(
                    sp.GetRequiredService<IGestaoAccessClient>(),
                    sp.GetRequiredService<IAppUpdater>(),
                    sp.GetRequiredService<OperatingModeService>(),
                    new UpdateServiceOptions
                    {
                        UnitId = s.UnitId ?? string.Empty,
                        DeviceId = s.DeviceId ?? string.Empty,
                        LogWarning = m => log.Warning(m),
                        LogError = m => log.Error(m),
                        LogInfo = m => log.Information(m),
                    });
            });

            Boot("peek settings");
            var peek = new JsonSettingsStore().LoadAppSettings();

            Boot($"UseFakeTurnstile={peek.UseFakeTurnstile}");
            if (peek.UseFakeTurnstile)
            {
                collection.RegisterFake();
            }
            else
            {
                collection.AddSingleton<ITurnstile>(sp =>
                {
                    var log = sp.GetRequiredService<FileLogger>();
                    return new ToletusLiteNetTurnstile(
                        information: m => log.Information(m),
                        error: m => log.Error(m));
                });
            }

            collection.AddSingleton<IFaceRecognitionService>(sp =>
            {
                var settings = sp.GetRequiredService<AppSettings>();
                var members = sp.GetRequiredService<IMemberRepository>();
                LocalHistogramFaceService? face = null;
                face = new LocalHistogramFaceService(
                    settings.FaceMatchThreshold,
                    (memberId, template, ct) => members.SaveFaceAsync(
                        new MemberFace
                        {
                            MemberId = memberId,
                            Template = template,
                            ModelVersion = face!.ModelVersion,
                            CreatedAt = DateTime.UtcNow
                        },
                        ct));
                return face;
            });

            collection.AddFhtAccessApplication();
            collection.AddSingleton<WebcamLaneHost>();
            collection.AddSingleton<WebcamService>(sp => sp.GetRequiredService<WebcamLaneHost>().Entry);
            collection.AddSingleton<PublicKioskViewModel>();
            collection.AddSingleton<AdminViewModel>();
            collection.AddSingleton<AttendantShellViewModel>();
            collection.AddSingleton<ShellViewModel>();
            collection.AddSingleton<MainWindow>();

            Boot("BuildServiceProvider");
            _services = collection.BuildServiceProvider();

            Boot("FileLogger");
            _logger = _services.GetRequiredService<FileLogger>();
            _logger.Information("FHT Access starting.");

            Boot("EnsureCreated");
            _services.GetRequiredService<DatabaseInitializer>().EnsureCreated();

            var settings = _services.GetRequiredService<AppSettings>();
            var flow = _services.GetRequiredService<AccessFlowService>();
            flow.DeviceId = string.IsNullOrWhiteSpace(settings.DeviceId) ? null : settings.DeviceId;
            flow.PassageTimeout = TimeSpan.FromSeconds(settings.PassageTimeoutSec <= 0 ? 10 : settings.PassageTimeoutSec);
            flow.EntryOnlyMode = settings.ExitMode != "facial";

            var dualGate = settings.WebcamIndexExit >= 0 && settings.WebcamIndexExit != settings.WebcamIndex;
            flow.DualGateMode = dualGate && string.Equals(settings.ExitMode, "facial", StringComparison.OrdinalIgnoreCase);

            var presence = _services.GetRequiredService<PresenceService>();
            presence.EntryOnlyMode = flow.EntryOnlyMode;
            presence.DualGateMode = flow.DualGateMode;
            presence.FreeGateMode = settings.FreeGateMode;
            flow.FreeGateMode = settings.FreeGateMode;

            if (dualGate && !flow.DualGateMode)
            {
                _logger?.Warning(
                    $"Câmera saída={settings.WebcamIndexExit} configurada mas exitMode={settings.ExitMode} — " +
                    "saída facial desligada. Use exitMode=facial ou reinicie após salvar appsettings.");
            }
            if (settings.FreeGateMode)
            {
                _logger?.Information(
                    "FreeGateMode=ON — facial registra entrada/saída sem validar presença (piloto catraca livre).");
            }
            presence.RecognitionCooldown = TimeSpan.FromSeconds(
                settings.RecognitionCooldownSec <= 0 ? 3 : settings.RecognitionCooldownSec);
            presence.StalePendingThreshold = flow.PassageTimeout + TimeSpan.FromSeconds(2);
            presence.VisitMaxDuration = TimeSpan.FromHours(settings.VisitMaxHours <= 0 ? 12 : settings.VisitMaxHours);

            var attendantSession = _services.GetRequiredService<AttendantSessionService>();
            attendantSession.IdleTimeout = TimeSpan.FromMinutes(
                settings.AttendantIdleMinutes <= 0 ? 5 : settings.AttendantIdleMinutes);

            ApplyWindowsStartup(settings);

            Boot("PresenceBootstrap");
            var bootstrap = _services.GetRequiredService<PresenceBootstrapService>();
            bootstrap.InitializeAsync(settings.UnitId).GetAwaiter().GetResult();

            Boot("VisitExpiryService");
            _services.GetRequiredService<VisitExpiryService>().Start();

            Boot("HydrateFaceGallery");
            HydrateFaceGallery();

            Boot("TurnstileSupervisor");
            StartTurnstileSupervisor(settings);

            Boot("StartWebcam");
            StartWebcam(settings);

            Boot("StartAutomaticEngine");
            StartAutomaticEngine();

            Boot("StartBackgroundSync");
            _services.GetRequiredService<BackgroundSyncService>().Start();

            Boot("StartUpdateService");
            StartUpdateService();

            Boot("resolve MainWindow");
            var main = _services.GetRequiredService<MainWindow>();
            _mainWindow = main;
            MainWindow = main;

            Boot("TrayIcon");
            _tray = new TrayIconService(
                openWindow: () => Dispatcher.Invoke(main.ShowAndActivate),
                openAttendant: () => Dispatcher.Invoke(main.OpenAttendant),
                exitApplication: () => Dispatcher.Invoke(ExitApplication));

            Boot("Show MainWindow");
            main.Show();
            main.Activate();
            Boot("Main window shown OK");
            _logger.Information("Main window shown.");
        }
        catch (Exception ex)
        {
            Boot($"FAIL {ex}");
            _logger?.Error($"Startup failed: {ex}");
            MessageBox.Show(
                $"Falha ao iniciar o FHT Acesso:\n\n{ex.Message}",
                "FHT Acesso",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ApplyWindowsStartup(AppSettings settings)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            WindowsStartupHelper.Apply(settings.StartWithWindows, exe);
        }
        catch (Exception ex)
        {
            Boot($"startup registry skip: {ex.Message}");
        }
    }

    private void HydrateFaceGallery()
    {
        try
        {
            if (_services?.GetRequiredService<IFaceRecognitionService>() is not LocalHistogramFaceService face)
                return;

            var members = _services.GetRequiredService<IMemberRepository>();
            var all = members.GetAllAsync().GetAwaiter().GetResult();
            var loaded = 0;

            foreach (var member in all)
            {
                var stored = members.GetFaceAsync(member.Id).GetAwaiter().GetResult();
                if (stored is null || stored.Template.Length == 0)
                    continue;

                if (!LocalHistogramFaceService.CanHydrate(stored.ModelVersion))
                    continue;

                try
                {
                    face.LoadTemplate(member.Id, stored.Template);
                    loaded++;
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Face template skipped for {member.Id}: {ex.Message}");
                }
            }

            _logger?.Information($"Face gallery hydrated: {loaded} template(s).");
        }
        catch (Exception ex)
        {
            Boot($"hydrate skip: {ex.Message}");
            _logger?.Warning($"Face gallery hydrate skipped: {ex.Message}");
        }
    }

    private void StartWebcam(AppSettings settings)
    {
        try
        {
            var lanes = _services!.GetRequiredService<WebcamLaneHost>();
            lanes.Start(settings);

            // Dá tempo do OpenCV entregar o 1º frame antes de ligar o motor.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline
                   && lanes.Entry.State is not WebcamConnectionState.Connected
                   and not WebcamConnectionState.Unavailable)
            {
                Thread.Sleep(200);
            }

            _logger?.Information(
                lanes.Entry.State == WebcamConnectionState.Connected
                    ? $"Entry camera {settings.WebcamIndex} connected (frames={lanes.Entry.FramesCaptured})."
                    : $"Entry camera {settings.WebcamIndex} NOT connected — state={lanes.Entry.State}, error={lanes.Entry.LastOpenError ?? "—"}");

            if (lanes.DualGateEnabled)
            {
                var exitOk = lanes.WaitForExitCamera(TimeSpan.FromSeconds(8));
                _logger?.Information(
                    exitOk
                        ? $"Exit camera {settings.WebcamIndexExit} connected (frames={lanes.Exit.FramesCaptured})."
                        : $"Exit camera {settings.WebcamIndexExit} NOT connected — state={lanes.Exit.State}, error={lanes.Exit.LastOpenError ?? "—"}");
            }
            else
            {
                _logger?.Information(
                    $"Exit facial desligada (exitMode={settings.ExitMode}, webcamIndexExit={settings.WebcamIndexExit}).");
            }
        }
        catch (Exception ex)
        {
            Boot($"webcam skip: {ex.Message}");
            _logger?.Warning($"Webcam start skipped: {ex.Message}");
        }
    }

    private void StartAutomaticEngine()
    {
        try
        {
            var services = _services!;
            var lanes = services.GetRequiredService<WebcamLaneHost>();
            var gates = services.GetRequiredService<GateLaneEngineHost>();
            var settings = services.GetRequiredService<AppSettings>();
            var flow = services.GetRequiredService<AccessFlowService>();
            var presence = services.GetRequiredService<PresenceService>();

            var dualFacial = lanes.DualGateEnabled
                               && string.Equals(settings.ExitMode, "facial", StringComparison.OrdinalIgnoreCase);
            flow.EntryOnlyMode = !dualFacial;
            flow.DualGateMode = dualFacial;
            presence.EntryOnlyMode = flow.EntryOnlyMode;
            presence.DualGateMode = dualFacial;
            presence.FreeGateMode = settings.FreeGateMode;
            flow.FreeGateMode = settings.FreeGateMode;

            var face = services.GetRequiredService<IFaceRecognitionService>() as LocalHistogramFaceService;
            if (face is not null)
                _logger?.Information($"Face engine ready: model={face.ModelVersion}.");

            gates.ConfigureDualGate(dualFacial);
            var successDisplay = TimeSpan.FromSeconds(
                settings.PassageSuccessDisplaySec <= 0 ? 5 : settings.PassageSuccessDisplaySec);
            var releaseMinDisplay = TimeSpan.FromSeconds(
                settings.PassageReleaseMinDisplaySec <= 0 ? 3 : settings.PassageReleaseMinDisplaySec);
            gates.BindCameras(
                () => lanes.Entry.GetJpegFrame(),
                () => IsApproachSignal(lanes.Entry, face, FaceDetectionOptions.ApproachPresence),
                lanes.DualGateEnabled
                    ? () => lanes.Exit.GetJpegFrame()
                    : null,
                lanes.DualGateEnabled
                    ? () => IsApproachSignal(lanes.Exit, face, FaceDetectionOptions.ApproachPresence)
                    : null);
            gates.ConfigureKioskDisplay(successDisplay, releaseMinDisplay);
            gates.Start();

            _logger?.Information(
                $"Gate lanes: entry cam={settings.WebcamIndex} state={lanes.Entry.State}, " +
                $"exit cam={settings.WebcamIndexExit} state={lanes.Exit.State}, " +
                $"dualFacial={dualFacial}, exitMode={settings.ExitMode}, freeGate={settings.FreeGateMode}, " +
                $"exitEngine={dualFacial && lanes.Exit.State == WebcamConnectionState.Connected}");
        }
        catch (Exception ex)
        {
            Boot($"engine skip: {ex.Message}");
            _logger?.Error($"Automatic engine failed to start: {ex.Message}");
        }
    }

    /// <summary>
    /// Abre o totem só com movimento na ROI + rosto (quando o frame já existe).
    /// Sem frame ainda, movimento basta para não travar o aquecimento da câmera.
    /// </summary>
    private static bool IsApproachSignal(
        WebcamService cam,
        LocalHistogramFaceService? face,
        FaceDetectionOptions presence)
    {
        if (!cam.HasMotion())
            return false;

        var jpeg = cam.GetJpegFrame();
        if (jpeg is null || jpeg.Length < 100)
            return true;

        if (face is null)
            return true;

        return face.HasNearbyFace(jpeg, presence);
    }

    private void StartUpdateService()
    {
        try
        {
            var svc = _services!.GetRequiredService<UpdateService>();
            svc.Start();
            _logger?.Information($"UpdateService started (app version {svc.CurrentVersion}).");
        }
        catch (Exception ex)
        {
            Boot($"update service skip: {ex.Message}");
            _logger?.Warning($"UpdateService skipped: {ex.Message}");
        }
    }

    private void StartTurnstileSupervisor(AppSettings settings)
    {
        try
        {
            var supervisor = _services!.GetRequiredService<TurnstileConnectionSupervisor>();
            var snapshot = new AppSettingsSnapshot
            {
                UseFakeTurnstile = settings.UseFakeTurnstile,
                TurnstileNetwork = settings.TurnstileNetwork,
                TurnstileIp = settings.TurnstileIp,
                TurnstileSerial = settings.TurnstileSerial,
                StartupDelaySec = settings.StartupDelaySec
            };
            supervisor.Start(snapshot);
        }
        catch (Exception ex)
        {
            Boot($"turnstile supervisor skip: {ex.Message}");
            _logger?.Warning($"Turnstile supervisor skipped: {ex.Message}");
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
            return;

        _exiting = true;
        _logger?.Information("Exit requested from tray — stopping recognition.");
        _mainWindow?.AllowClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            _tray = null;

            var gates = _services?.GetService<GateLaneEngineHost>();
            gates?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var visitExpiry = _services?.GetService<VisitExpiryService>();
            visitExpiry?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var supervisor = _services?.GetService<TurnstileConnectionSupervisor>();
            supervisor?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var sync = _services?.GetService<BackgroundSyncService>();
            sync?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var updateSvc = _services?.GetService<UpdateService>();
            updateSvc?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            _services?.GetService<WebcamLaneHost>()?.Dispose();
            _services?.GetService<ShellViewModel>()?.Dispose();
            _services?.GetService<AttendantShellViewModel>()?.Dispose();
            _services?.GetService<PublicKioskViewModel>()?.Dispose();
            _services?.GetService<AdminViewModel>()?.Dispose();

            var turnstile = _services?.GetService<TurnstileService>();
            turnstile?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            // best-effort shutdown
        }

        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Boot($"UI ex: {e.Exception}");
        _logger?.Error($"Unhandled UI: {e.Exception}");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Boot($"Domain ex: {e.ExceptionObject}");
        _logger?.Error($"Unhandled domain: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Boot($"Task ex: {e.Exception}");
        _logger?.Error($"Unobserved task: {e.Exception}");
        e.SetObserved();
    }
}
