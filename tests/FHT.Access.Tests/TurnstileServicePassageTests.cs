using FHT.Access.Application.Services;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Tests;

public class TurnstileServicePassageTests
{
    [Fact]
    public async Task ReleaseAndWait_DoesNotMiss_FastPassageDetected()
    {
        var turnstile = new InstantPassageTurnstile();
        await using var service = new TurnstileService(turnstile);
        await turnstile.ConnectAsync(new TurnstileConfig { UseFake = true });

        var outcome = await service.ReleaseAndWaitForPassageAsync(
            ct => turnstile.ReleaseEntryAsync(ct: ct),
            TimeSpan.FromSeconds(2));

        Assert.Equal(PassageOutcome.PassageDetected, outcome);
    }

    [Fact]
    public async Task WaitForPassage_IgnoresBoardTimeout_UntilAppTimeout()
    {
        var turnstile = new BoardTimeoutThenNothingTurnstile();
        await using var service = new TurnstileService(turnstile);
        await turnstile.ConnectAsync(new TurnstileConfig { UseFake = true });

        var wait = service.WaitForPassageAsync(TimeSpan.FromMilliseconds(400));
        turnstile.Raise(PassageOutcome.Timeout);
        turnstile.Raise(PassageOutcome.Unknown);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outcome = await wait;
        sw.Stop();

        Assert.Equal(PassageOutcome.Timeout, outcome);
        Assert.True(sw.ElapsedMilliseconds >= 250, $"ended too early: {sw.ElapsedMilliseconds}ms");
    }

    private sealed class InstantPassageTurnstile : ITurnstile
    {
        public TurnstileConnectionState State { get; private set; } = TurnstileConnectionState.Disconnected;
        public event EventHandler<TurnstileConnectionState>? StateChanged;
        public event EventHandler<PassageOutcome>? PassageReceived;

        public Task ConnectAsync(TurnstileConfig config, CancellationToken ct = default)
        {
            State = TurnstileConnectionState.Connected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            State = TurnstileConnectionState.Disconnected;
            return Task.CompletedTask;
        }

        public Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
        {
            State = TurnstileConnectionState.WaitingPassage;
            // Synchronous raise — reproduces free-gate race if wait arms after release.
            PassageReceived?.Invoke(this, PassageOutcome.PassageDetected);
            State = TurnstileConnectionState.Connected;
            return Task.CompletedTask;
        }

        public Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
            => ReleaseEntryAsync(top, bottom, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BoardTimeoutThenNothingTurnstile : ITurnstile
    {
        public TurnstileConnectionState State { get; private set; } = TurnstileConnectionState.Disconnected;
        public event EventHandler<TurnstileConnectionState>? StateChanged;
        public event EventHandler<PassageOutcome>? PassageReceived;

        public void Raise(PassageOutcome outcome) => PassageReceived?.Invoke(this, outcome);

        public Task ConnectAsync(TurnstileConfig config, CancellationToken ct = default)
        {
            State = TurnstileConnectionState.Connected;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
