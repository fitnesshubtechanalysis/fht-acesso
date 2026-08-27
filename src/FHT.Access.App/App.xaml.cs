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
            collection.AddSingleton<WebcamService>();
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

            var presence = _services.GetRequiredService<PresenceService>();
            presence.EntryOnlyMode = flow.EntryOnlyMode;
            presence.RecognitionCooldown = TimeSpan.FromSeconds(
                settings.RecognitionCooldownSec <= 0 ? 3 : settings.RecognitionCooldownSec);
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
            var webcam = _services!.GetRequiredService<WebcamService>();
            if (!webcam.IsRunning)
            {
                webcam.Configure(
                    settings.CameraWidth,
                    settings.CameraHeight,
                    settings.CameraFps,
                    settings.ProcessFps);
                webcam.Start(settings.WebcamIndex, settings.CameraDeviceId);
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
            var webcam = services.GetRequiredService<WebcamService>();
            var engine = services.GetRequiredService<AutomaticAccessEngine>();
            engine.BindCamera(() => webcam.GetJpegFrame(), () => webcam.HasMotion());
            engine.Start();
        }
        catch (Exception ex)
        {
            Boot($"engine skip: {ex.Message}");
            _logger?.Error($"Automatic engine failed to start: {ex.Message}");
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

            var engine = _services?.GetService<AutomaticAccessEngine>();
            engine?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var visitExpiry = _services?.GetService<VisitExpiryService>();
            visitExpiry?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var supervisor = _services?.GetService<TurnstileConnectionSupervisor>();
            supervisor?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            var sync = _services?.GetService<BackgroundSyncService>();
            sync?.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

            _services?.GetService<WebcamService>()?.Dispose();
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
