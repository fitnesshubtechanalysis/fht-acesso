using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

/// <summary>
/// Poll rápido (~3s) de liberação remota criada na Gestão (recepção web).
/// Também consome sync forçado e configuração remota (freeGate).
/// </summary>
public sealed class GateCommandPollService : IAsyncDisposable
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly IGestaoAccessClient _client;
    private readonly IAccessDeviceContext _device;
    private readonly AccessFlowService _flow;
    private readonly PresenceService _presence;
    private readonly MemberSyncService _memberSync;
    private readonly IDiagnosticLog? _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _started;

    public GateCommandPollService(
        IGestaoAccessClient client,
        IAccessDeviceContext device,
        AccessFlowService flow,
        PresenceService presence,
        MemberSyncService memberSync,
        IDiagnosticLog? log = null)
    {
        _client = client;
        _device = device;
        _flow = flow;
        _presence = presence;
        _memberSync = memberSync;
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
        }
        catch
        {
            return;
        }

        GateCommandsPollDto poll;
        try
        {
            poll = await _client.GetPendingGateCommandsAsync(unitId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Warning($"Gate-commands poll falhou: {ex.Message}");
            return;
        }

        if (poll.Configuration?.FreeGateMode is bool freeGate)
        {
            _presence.FreeGateMode = freeGate;
            _flow.FreeGateMode = freeGate;
        }

        if (poll.SyncPending)
        {
            try
            {
                await _memberSync.SyncAsync(unitId, ct, full: true).ConfigureAwait(false);
                await _client.AckDeviceSyncAsync(unitId, ct).ConfigureAwait(false);
                _log?.Information("Sync forçado pela Gestão concluído.");
            }
            catch (Exception ex)
            {
                _log?.Warning($"Sync forçado falhou: {ex.Message}");
            }
        }

        foreach (var cmd in poll.Commands)
        {
            if (ct.IsCancellationRequested)
                break;

            var direction = string.Equals(cmd.Direction, "exit", StringComparison.OrdinalIgnoreCase)
                ? AccessDirection.Exit
                : AccessDirection.Entry;
            var reason = string.IsNullOrWhiteSpace(cmd.Reason)
                ? "Liberação remota da recepção"
                : cmd.Reason!;

            try
            {
                var result = await _flow
                    .ProcessManualReleaseAsync(cmd.CustomerId, cmd.FullName, reason, direction, ct)
                    .ConfigureAwait(false);

                var ok = result.Decision.Allowed;
                await _client
                    .AckGateCommandAsync(
                        unitId,
                        cmd.Id,
                        ok ? "opened" : "failed",
                        result.UiMessage,
                        ct)
                    .ConfigureAwait(false);

                _log?.Information(
                    ok
                        ? $"Gate remoto OK: {cmd.FullName ?? cmd.CustomerId?.ToString()}"
                        : $"Gate remoto falhou: {result.UiMessage}");
            }
            catch (Exception ex)
            {
                _log?.Warning($"Gate remoto erro: {ex.Message}");
                try
                {
                    await _client
                        .AckGateCommandAsync(unitId, cmd.Id, "failed", ex.Message, ct)
                        .ConfigureAwait(false);
                }
                catch
                {
                    /* ignore ack failure */
                }
            }
        }
    }
}
