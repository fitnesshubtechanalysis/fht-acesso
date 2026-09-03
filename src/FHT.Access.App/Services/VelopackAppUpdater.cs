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
        var mgr = new UpdateManager(new SimpleWebSource(feedUrl));
        _pendingUpdate = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pendingUpdate?.TargetFullRelease?.Version?.ToString();
    }

    public async Task DownloadUpdateAsync(
        string feedUrl,
        IProgress<int> progress,
        CancellationToken ct = default)
    {
        if (_pendingUpdate is null)
        {
            // Segurança: tenta checar de novo.
            var mgr2 = new UpdateManager(new SimpleWebSource(feedUrl));
            _pendingUpdate = await mgr2.CheckForUpdatesAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("Não há update disponível para baixar.");
        }

        var mgr = new UpdateManager(new SimpleWebSource(feedUrl));
        await mgr.DownloadUpdatesAsync(
            _pendingUpdate,
            progress.Report,
            cancelToken: ct)
            .ConfigureAwait(false);
    }

    public void ApplyAndRestart()
    {
        if (_pendingUpdate is null)
            throw new InvalidOperationException("Nenhum update baixado.");

        var mgr = new UpdateManager(new SimpleWebSource(string.Empty));
        mgr.ApplyUpdatesAndRestart(_pendingUpdate);
        // Não retorna — processo reiniciado pelo Velopack.
    }
}
