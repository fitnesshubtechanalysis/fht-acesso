using FHT.Access.Application.Abstractions;

namespace FHT.Access.Application.Services;

/// <summary>
/// Periodically pulls member snapshots (status, photos) from Gestão and uploads pending access events + photos.
/// Face templates stay on the device for offline recognition.
/// </summary>
public sealed class BackgroundSyncService : IAsyncDisposable
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly MemberSyncService _members;
    private readonly OfflineSyncService _events;
    private readonly MemberPhotoSyncService _photos;
    private readonly IGestaoAccessClient _client;
    private readonly IAccessDeviceContext _device;
    private readonly IDiagnosticLog? _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _started;

    public BackgroundSyncService(
        MemberSyncService members,
        OfflineSyncService events,
        MemberPhotoSyncService photos,
        IGestaoAccessClient client,
        IAccessDeviceContext device,
        IDiagnosticLog? log = null)
    {
        _members = members;
        _events = events;
        _photos = photos;
        _client = client;
        _device = device;
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

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            await TickAsync(ct).ConfigureAwait(false);
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

    private async Task TickAsync(CancellationToken ct)
    {
        var unitId = _device.UnitId?.Trim();
        if (string.IsNullOrWhiteSpace(unitId))
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(_device.DeviceId)
                && !string.IsNullOrWhiteSpace(_device.DeviceSecret))
            {
                await _client
                    .EnsureAuthenticatedAsync(_device.DeviceId.Trim(), _device.DeviceSecret, ct)
                    .ConfigureAwait(false);
            }

            var flushed = await _events.FlushAsync(unitId, ct).ConfigureAwait(false);
            var photos = await _photos.FlushAsync(unitId, ct).ConfigureAwait(false);
            var pulled = await _members.SyncAsync(unitId, ct).ConfigureAwait(false);
            _log?.Information($"Auto-sync: {pulled} aluno(s), {flushed} evento(s), {photos} foto(s).");
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_device.DeviceId)
                    && !string.IsNullOrWhiteSpace(_device.DeviceSecret))
                {
                    await _client
                        .EnsureAuthenticatedAsync(
                            _device.DeviceId.Trim(),
                            _device.DeviceSecret,
                            ct,
                            force: true)
                        .ConfigureAwait(false);
                    var flushed = await _events.FlushAsync(unitId, ct).ConfigureAwait(false);
                    var photos = await _photos.FlushAsync(unitId, ct).ConfigureAwait(false);
                    var pulled = await _members.SyncAsync(unitId, ct).ConfigureAwait(false);
                    _log?.Information(
                        $"Auto-sync (reauth): {pulled} aluno(s), {flushed} evento(s), {photos} foto(s).");
                }
            }
            catch (Exception ex)
            {
                _log?.Warning($"Auto-sync falhou após reauth: {ex.Message}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _log?.Warning($"Auto-sync falhou: {ex.Message}");
        }
    }
}
