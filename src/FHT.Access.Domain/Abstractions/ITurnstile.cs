using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Abstractions;

public interface ITurnstile : IAsyncDisposable
{
    TurnstileConnectionState State { get; }

    event EventHandler<TurnstileConnectionState>? StateChanged;
    event EventHandler<PassageOutcome>? PassageReceived;

    Task ConnectAsync(TurnstileConfig config, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task ReleaseEntryAsync(string? top = null, string? bottom = null, CancellationToken ct = default);
    Task ReleaseExitAsync(string? top = null, string? bottom = null, CancellationToken ct = default);
}
