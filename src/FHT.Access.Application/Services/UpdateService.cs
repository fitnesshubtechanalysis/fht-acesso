using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Configurações injetadas no <see cref="UpdateService"/> para evitar
/// dependência de Infrastructure nesta camada.
/// </summary>
public sealed class UpdateServiceOptions
{
    public string UnitId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(15);
    public int CountdownSeconds { get; init; } = 60;
    public Action<string>? LogWarning { get; init; }
    public Action<string>? LogError { get; init; }
    public Action<string>? LogInfo { get; init; }
}

/// <summary>
/// Verifica se há nova versão na Gestão, baixa e aplica via <see cref="IAppUpdater"/>.
/// Emite progresso através de <see cref="StateChanged"/> para a UI.
/// </summary>
public sealed class UpdateService : IAsyncDisposable
{
    private readonly IGestaoAccessClient _gestao;
    private readonly IAppUpdater _updater;
    private readonly OperatingModeService _mode;
    private readonly UpdateServiceOptions _opts;

    private CancellationTokenSource _cts = new();
    private Task? _loop;

    private UpdateUiState _state = UpdateUiState.None;
    private int _countdownRemaining;
    private int _downloadPercent;
    private string? _availableVersion;
    private string? _releaseNotes;
    private bool _mandatory;
    private string? _downloadUrl;

    private int _applyAfterHour = 20;
    private int _applyBeforeHour = 5;

    public UpdateService(
        IGestaoAccessClient gestao,
        IAppUpdater updater,
        OperatingModeService mode,
        UpdateServiceOptions opts)
    {
        _gestao = gestao;
        _updater = updater;
        _mode = mode;
        _opts = opts;
    }

    // ── Propriedades observáveis ─────────────────────────────────────────────

    public UpdateUiState State => _state;
    public int CountdownRemaining => _countdownRemaining;
    public int DownloadPercent => _downloadPercent;
    public string? AvailableVersion => _availableVersion;
    public string? ReleaseNotes => _releaseNotes;
    public bool IsMandatory => _mandatory;
    public string CurrentVersion => _updater.CurrentVersion;

    /// <summary>Disparado toda vez que o estado ou progresso muda.</summary>
    public event EventHandler? StateChanged;

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    public void Start()
    {
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }

    // ── Trigger manual (Admin) ───────────────────────────────────────────────

    /// <summary>Força verificação imediata. Chamado pelo botão no Admin.</summary>
    public async Task CheckNowAsync(CancellationToken ct = default)
    {
        await PollAndPrepareAsync(ct).ConfigureAwait(false);
    }

    // ── Loop principal ───────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        // Delay inicial para o app carregar por completo antes do primeiro poll.
        await SafeDelay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollAndPrepareAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _opts.LogWarning?.Invoke($"[Update] Poll falhou: {ex.Message}");
            }

            var delay = _state is UpdateUiState.None or UpdateUiState.Available
                ? _opts.PollInterval
                : TimeSpan.FromSeconds(5);

            await SafeDelay(delay, ct).ConfigureAwait(false);
        }
    }

    private async Task PollAndPrepareAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.UnitId)
            || string.IsNullOrWhiteSpace(_opts.DeviceId))
            return;

        UpdateChannelDto? channel = null;
        try
        {
            channel = await _gestao
                .GetUpdateChannelAsync(_opts.UnitId, _opts.DeviceId, _updater.CurrentVersion, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _opts.LogWarning?.Invoke($"[Update] GetUpdateChannel falhou: {ex.Message}");
            return;
        }

        if (channel is null || string.IsNullOrWhiteSpace(channel.LatestVersion))
        {
            if (_state == UpdateUiState.Available)
                Transition(UpdateUiState.None);
            return;
        }

        if (IsVersionUpToDate(_updater.CurrentVersion, channel.LatestVersion))
        {
            if (_state == UpdateUiState.Available)
                Transition(UpdateUiState.None);
            return;
        }

        if (string.IsNullOrWhiteSpace(channel.DownloadUrl))
        {
            _opts.LogWarning?.Invoke("[Update] Canal disponível mas sem downloadUrl.");
            return;
        }

        _availableVersion = channel.LatestVersion;
        _releaseNotes = channel.ReleaseNotes;
        _mandatory = channel.Mandatory;
        _downloadUrl = channel.DownloadUrl;
        _applyAfterHour = channel.ApplyAfterHour;
        _applyBeforeHour = channel.ApplyBeforeHour;

        if (_state is UpdateUiState.Downloading or UpdateUiState.Applying or UpdateUiState.Countdown)
            return;

        Transition(UpdateUiState.Available);

        if (_mandatory || IsWithinApplyWindow())
            await BeginCountdownAndApplyAsync(ct).ConfigureAwait(false);
    }

    private async Task BeginCountdownAndApplyAsync(CancellationToken ct)
    {
        // Pausa reconhecimento — kiosk mostra overlay de atualização.
        _mode.EnterMaintenance();

        // Countdown
        var secs = _opts.CountdownSeconds;
        Transition(UpdateUiState.Countdown, countdown: secs);
        for (var i = secs; i > 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            Set(countdown: i);
            await SafeDelay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        // Download
        Transition(UpdateUiState.Downloading, percent: 0);
        _opts.LogInfo?.Invoke($"[Update] Baixando versão {_availableVersion} de {_downloadUrl}");

        try
        {
            var available = await _updater.CheckForUpdateAsync(_downloadUrl!, ct).ConfigureAwait(false);
            if (available is null)
            {
                _opts.LogWarning?.Invoke("[Update] Updater não encontrou release — feedUrl incorreta?");
                _mode.EnterAutomatic();
                Transition(UpdateUiState.Available);
                return;
            }

            await _updater.DownloadUpdateAsync(
                _downloadUrl!,
                new Progress<int>(p => Set(percent: p)),
                ct)
                .ConfigureAwait(false);

            Transition(UpdateUiState.Applying);
            _opts.LogInfo?.Invoke("[Update] Aplicando e reiniciando...");
            _updater.ApplyAndRestart();
            // Acima não retorna.
        }
        catch (OperationCanceledException)
        {
            _mode.EnterAutomatic();
            Transition(UpdateUiState.Available);
            throw;
        }
        catch (Exception ex)
        {
            _opts.LogError?.Invoke($"[Update] Falha: {ex.Message}");
            _mode.EnterAutomatic();
            Transition(UpdateUiState.Available);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool IsWithinApplyWindow()
    {
        var hour = DateTime.Now.Hour;
        // Ex.: applyAfterHour=20, applyBeforeHour=5 → 20h–04h59
        if (_applyAfterHour > _applyBeforeHour)
            return hour >= _applyAfterHour || hour < _applyBeforeHour;
        return hour >= _applyAfterHour && hour < _applyBeforeHour;
    }

    private static bool IsVersionUpToDate(string current, string available)
    {
        if (!Version.TryParse(current, out var cur) || !Version.TryParse(available, out var avail))
            return false;
        return cur >= avail;
    }

    private void Transition(UpdateUiState state, int? countdown = null, int? percent = null)
    {
        _state = state;
        if (countdown.HasValue) _countdownRemaining = countdown.Value;
        if (percent.HasValue) _downloadPercent = percent.Value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Set(int? countdown = null, int? percent = null)
    {
        if (countdown.HasValue) _countdownRemaining = countdown.Value;
        if (percent.HasValue) _downloadPercent = percent.Value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
    }
}
