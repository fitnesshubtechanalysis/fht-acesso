namespace FHT.Access.Application.Services;

using FHT.Access.Application.Abstractions;

/// <summary>
/// Periodically closes visits that exceeded VisitMaxDuration.
/// </summary>
public sealed class VisitExpiryService : IAsyncDisposable
{
    private readonly PresenceService _presence;
    private readonly IDiagnosticLog? _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _started;

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public VisitExpiryService(PresenceService presence, IDiagnosticLog? log = null)
    {
        _presence = presence;
        _log = log;
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _presence.ExpireStaleVisitsAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log?.Warning($"Visit expiry job failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* ignore */ }
            _loop = null;
        }

        _started = false;
    }
}
