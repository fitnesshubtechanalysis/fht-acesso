using FHT.Access.Application.Abstractions;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Maintains LiteNet3 connection with exponential backoff reconnect.
/// Connected state reflects board.Connected == true only.
/// </summary>
public sealed class TurnstileConnectionSupervisor : IAsyncDisposable
{
    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30)
    ];

    private readonly TurnstileService _turnstile;
    private readonly DeviceService _devices;
    private readonly ISettingsStore _settings;
    private readonly IDiagnosticLog? _log;

    private readonly object _sync = new();
    private CancellationTokenSource? _supervisorCts;
    private Task? _supervisorLoop;
    private TurnstileConfig? _lastConfig;
    private int _backoffIndex;
    private bool _started;
    private bool _manualDisconnect;

    public TurnstileConnectionSupervisor(
        TurnstileService turnstile,
        DeviceService devices,
        ISettingsStore settings,
        IDiagnosticLog? log = null)
    {
        _turnstile = turnstile;
        _devices = devices;
        _settings = settings;
        _log = log;
        _turnstile.StateChanged += OnTurnstileStateChanged;
    }

    public TurnstileConnectionState State => _turnstile.State;

    public bool CanRelease =>
        State is TurnstileConnectionState.Connected or TurnstileConnectionState.WaitingPassage;

    public event EventHandler<TurnstileConnectionState>? StateChanged;

    public void Start(AppSettingsSnapshot settings)
    {
        if (_started)
            return;
        _started = true;
        _supervisorCts = new CancellationTokenSource();
        _supervisorLoop = Task.Run(() => SupervisorLoopAsync(settings, _supervisorCts.Token));

        if (!settings.UseFakeTurnstile)
            _ = ConnectFromSettingsAsync(settings);
    }

    public async Task ConnectFromSettingsAsync(AppSettingsSnapshot settings, CancellationToken ct = default)
    {
        _manualDisconnect = false;
        var config = await BuildConfigAsync(settings, ct).ConfigureAwait(false);
        if (config is null)
            return;

        lock (_sync)
        {
            _lastConfig = config;
            _backoffIndex = 0;
        }

        await SafeConnectAsync(config, ct).ConfigureAwait(false);
    }

    public async Task DisconnectManualAsync(CancellationToken ct = default)
    {
        _manualDisconnect = true;
        await _turnstile.DisconnectAsync(ct).ConfigureAwait(false);
    }

    private async Task SupervisorLoopAsync(AppSettingsSnapshot settings, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(settings.StartupDelaySec <= 0 ? 5 : settings.StartupDelaySec), ct)
            .ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            if (_manualDisconnect || settings.UseFakeTurnstile)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            var state = _turnstile.State;
            if (state is TurnstileConnectionState.Disconnected or TurnstileConnectionState.Error)
            {
                TurnstileConfig? config;
                lock (_sync) config = _lastConfig;

                config ??= await BuildConfigAsync(settings, ct).ConfigureAwait(false);
                if (config is not null)
                {
                    _log?.Information("[TurnstileSupervisor] Reconnecting…");
                    await SafeConnectAsync(config, ct).ConfigureAwait(false);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        }
    }

    private async Task SafeConnectAsync(TurnstileConfig config, CancellationToken ct)
    {
        try
        {
            await _turnstile.ConnectAsync(config, ct).ConfigureAwait(false);
            lock (_sync) _backoffIndex = 0;
        }
        catch (Exception ex)
        {
            _log?.Warning($"[TurnstileSupervisor] Connect failed: {ex.Message}");
            var delay = BackoffSteps[Math.Min(_backoffIndex, BackoffSteps.Length - 1)];
            lock (_sync) _backoffIndex = Math.Min(_backoffIndex + 1, BackoffSteps.Length - 1);
            try { await Task.Delay(delay, ct).ConfigureAwait(false); } catch { /* cancel */ }
        }
    }

    private async Task<TurnstileConfig?> BuildConfigAsync(AppSettingsSnapshot settings, CancellationToken ct)
    {
        if (settings.UseFakeTurnstile)
            return new TurnstileConfig { UseFake = true };

        var stored = await _devices.GetTurnstileConfigAsync(ct).ConfigureAwait(false);
        if (stored is not null)
            return stored;

        if (string.IsNullOrWhiteSpace(settings.TurnstileIp)
            && string.IsNullOrWhiteSpace(settings.TurnstileSerial))
            return null;

        return new TurnstileConfig
        {
            NetworkInterface = settings.TurnstileNetwork,
            BoardIp = settings.TurnstileIp,
            Serial = settings.TurnstileSerial,
            UseFake = false
        };
    }

    private void OnTurnstileStateChanged(object? sender, TurnstileConnectionState state)
        => StateChanged?.Invoke(this, state);

    public async ValueTask DisposeAsync()
    {
        _turnstile.StateChanged -= OnTurnstileStateChanged;
        if (_supervisorCts is not null)
        {
            await _supervisorCts.CancelAsync().ConfigureAwait(false);
            _supervisorCts.Dispose();
            _supervisorCts = null;
        }

        if (_supervisorLoop is not null)
        {
            try { await _supervisorLoop.ConfigureAwait(false); } catch { /* ignore */ }
            _supervisorLoop = null;
        }
    }
}

/// <summary>Read-only settings snapshot for supervisor (avoids Infrastructure coupling in signature).</summary>
public sealed class AppSettingsSnapshot
{
    public bool UseFakeTurnstile { get; init; }
    public string TurnstileNetwork { get; init; } = string.Empty;
    public string TurnstileIp { get; init; } = string.Empty;
    public string TurnstileSerial { get; init; } = string.Empty;
    public int StartupDelaySec { get; init; } = 5;
}
