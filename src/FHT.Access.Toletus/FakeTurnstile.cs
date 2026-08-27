using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Toletus;

/// <summary>
/// In-memory turnstile for CI and kiosk demos without hardware.
/// </summary>
public sealed class FakeTurnstile : ITurnstile
{
    private readonly object _sync = new();
    private CancellationTokenSource? _releaseCts;
    private TurnstileConnectionState _state = TurnstileConnectionState.Disconnected;

    public TurnstileConnectionState State
    {
        get
        {
            lock (_sync) return _state;
        }
    }

    public event EventHandler<TurnstileConnectionState>? StateChanged;
    public event EventHandler<PassageOutcome>? PassageReceived;

    public Task ConnectAsync(TurnstileConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ct.ThrowIfCancellationRequested();
        SetState(TurnstileConnectionState.Connecting);
        SetState(TurnstileConnectionState.Connected);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        CancelPendingRelease();
        SetState(TurnstileConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
        => SimulatePassageAsync(ct);

    public Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
        => SimulatePassageAsync(ct);

    public ValueTask DisposeAsync()
    {
        CancelPendingRelease();
        SetState(TurnstileConnectionState.Disconnected);
        return ValueTask.CompletedTask;
    }

    private Task SimulatePassageAsync(CancellationToken ct)
    {
        if (State is not TurnstileConnectionState.Connected and not TurnstileConnectionState.WaitingPassage)
            throw new InvalidOperationException("Fake turnstile is not connected.");

        CancelPendingRelease();
        SetState(TurnstileConnectionState.WaitingPassage);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_sync)
        {
            _releaseCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, cts.Token).ConfigureAwait(false);
                PassageReceived?.Invoke(this, PassageOutcome.PassageDetected);
                SetState(TurnstileConnectionState.Connected);
            }
            catch (OperationCanceledException)
            {
                // disconnected or superseded
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private void CancelPendingRelease()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _releaseCts;
            _releaseCts = null;
        }

        if (cts is null)
            return;

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignore
        }

        cts.Dispose();
    }

    private void SetState(TurnstileConnectionState state)
    {
        lock (_sync)
        {
            if (_state == state)
                return;
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }
}
