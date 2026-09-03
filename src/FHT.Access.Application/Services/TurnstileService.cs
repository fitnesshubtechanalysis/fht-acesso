using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class TurnstileService : IAsyncDisposable
{
    private readonly ITurnstile _turnstile;
    private bool _subscribed;

    public TurnstileService(ITurnstile turnstile)
    {
        _turnstile = turnstile;
        EnsureSubscribed();
    }

    public TurnstileConnectionState State => _turnstile.State;

    public event EventHandler<TurnstileConnectionState>? StateChanged;
    public event EventHandler<PassageOutcome>? PassageReceived;

    public Task ConnectAsync(TurnstileConfig config, CancellationToken cancellationToken = default)
        => _turnstile.ConnectAsync(config, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _turnstile.DisconnectAsync(cancellationToken);

    public Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken cancellationToken = default)
        => _turnstile.ReleaseEntryAsync(top, bottom, cancellationToken);

    public Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken cancellationToken = default)
        => _turnstile.ReleaseExitAsync(top, bottom, cancellationToken);

    public async Task<PassageOutcome> WaitForPassageAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<PassageOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Só PassageDetected encerra cedo. Timeout/Unknown da placa (comum em acesso livre)
        // não abortam a espera — senão a app desiste antes do aluno girar a catraca.
        void Handler(object? sender, PassageOutcome outcome)
        {
            if (outcome == PassageOutcome.PassageDetected)
                tcs.TrySetResult(outcome);
        }

        PassageReceived += Handler;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetResult(PassageOutcome.Timeout));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            PassageReceived -= Handler;
        }
    }

    /// <summary>
    /// Arme a escuta de passagem <b>antes</b> do Release — evita perder PassageDetected
    /// quando a catraca (esp. em acesso livre) responde muito rápido.
    /// </summary>
    public async Task<PassageOutcome> ReleaseAndWaitForPassageAsync(
        Func<CancellationToken, Task> releaseAsync,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseAsync);

        var tcs = new TaskCompletionSource<PassageOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, PassageOutcome outcome)
        {
            if (outcome == PassageOutcome.PassageDetected)
                tcs.TrySetResult(outcome);
        }

        PassageReceived += Handler;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetResult(PassageOutcome.Timeout));

            await releaseAsync(cancellationToken).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            PassageReceived -= Handler;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_subscribed)
        {
            _turnstile.StateChanged -= OnStateChanged;
            _turnstile.PassageReceived -= OnPassageReceived;
            _subscribed = false;
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureSubscribed()
    {
        if (_subscribed)
        {
            return;
        }

        _turnstile.StateChanged += OnStateChanged;
        _turnstile.PassageReceived += OnPassageReceived;
        _subscribed = true;
    }

    private void OnStateChanged(object? sender, TurnstileConnectionState state)
        => StateChanged?.Invoke(this, state);

    private void OnPassageReceived(object? sender, PassageOutcome outcome)
        => PassageReceived?.Invoke(this, outcome);
}
