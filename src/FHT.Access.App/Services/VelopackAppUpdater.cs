using System.Reflection;
using FHT.Access.Application.Abstractions;
using Velopack;
using Velopack.Sources;

namespace FHT.Access.App.Services;

/// <summary>
/// Implementação de <see cref="IAppUpdater"/> via Velopack.
/// Só referencia Velopack nesta camada (App), mantendo Application limpa.
/// </summary>
public sealed class VelopackAppUpdater : IAppUpdater
{
    private UpdateManager? _manager;
    private string? _feedUrl;
    private UpdateInfo? _pendingUpdate;

    public string CurrentVersion
    {
        get
        {
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                if (ver is null) return "0.0.0";
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch
            {
                return "0.0.0";
            }
        }
    }

    public async Task<string?> CheckForUpdateAsync(string feedUrl, CancellationToken ct = default)
    {
        var mgr = ManagerFor(feedUrl);
        _pendingUpdate = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pendingUpdate?.TargetFullRelease?.Version?.ToString();
    }

    public async Task DownloadUpdateAsync(
        string feedUrl,
        IProgress<int> progress,
        CancellationToken ct = default)
    {
        var mgr = ManagerFor(feedUrl);
        if (_pendingUpdate is null)
        {
            _pendingUpdate = await mgr.CheckForUpdatesAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("Não há update disponível para baixar.");
        }

        await mgr.DownloadUpdatesAsync(
            _pendingUpdate,
            progress.Report,
            cancelToken: ct)
            .ConfigureAwait(false);
    }

    public void ApplyAndRestart()
    {
        if (_pendingUpdate?.TargetFullRelease is null || _manager is null)
            throw new InvalidOperationException("Nenhum update baixado.");

        var asset = _pendingUpdate.TargetFullRelease;
        var manager = _manager;

        // O helper do Velopack espera este processo terminar. O manager vazio
        // usado antes lançava erro aqui e o pacote só era aplicado na próxima abertura.
        void Apply()
        {
            manager.WaitExitThenApplyUpdates(asset, silent: true, restart: true);
            Environment.Exit(0);
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(Apply);
        else
            Apply();
    }

    private UpdateManager ManagerFor(string feedUrl)
    {
        if (_manager is not null && string.Equals(_feedUrl, feedUrl, StringComparison.Ordinal))
            return _manager;

        _pendingUpdate = null;
        _feedUrl = feedUrl;
        _manager = new UpdateManager(new SimpleWebSource(feedUrl));
        return _manager;
    }
}
