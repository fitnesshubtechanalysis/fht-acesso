using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Toletus;

/// <summary>
/// In-memory turnstile for CI and kiosk demos without hardware.
/// Liberar o braço não inventa o giro: "Entrada registrada" só existe depois de
/// <see cref="NotifyArmTurn"/>, que representa o Passage In/Out da placa real.
/// </summary>
public sealed class FakeTurnstile : ITurnstile
{
    private readonly object _sync = new();
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
        SetState(TurnstileConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
        => ArmWaitingPassageAsync(ct);

    public Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
        => ArmWaitingPassageAsync(ct);

    /// <summary>Giro físico simulado. Sem isso a espera termina em timeout, sem entrada registrada.</summary>
    public void NotifyArmTurn()
    {
        if (State is not TurnstileConnectionState.WaitingPassage and not TurnstileConnectionState.Connected)
            return;

        PassageReceived?.Invoke(this, PassageOutcome.PassageDetected);
        SetState(TurnstileConnectionState.Connected);
    }

    public ValueTask DisposeAsync()
    {
        SetState(TurnstileConnectionState.Disconnected);
        return ValueTask.CompletedTask;
    }

    private Task ArmWaitingPassageAsync(CancellationToken ct)
    {
        if (State is not TurnstileConnectionState.Connected and not TurnstileConnectionState.WaitingPassage)
            throw new InvalidOperationException("Fake turnstile is not connected.");

        ct.ThrowIfCancellationRequested();
        SetState(TurnstileConnectionState.WaitingPassage);
        return Task.CompletedTask;
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
