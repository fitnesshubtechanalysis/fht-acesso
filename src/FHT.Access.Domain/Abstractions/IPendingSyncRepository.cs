using FHT.Access.Domain.Entities;

namespace FHT.Access.Domain.Abstractions;

public interface IPendingSyncRepository
{
    Task EnqueueAsync(PendingSync item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingSync>> GetPendingAsync(int take = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingSync>> GetPendingByKindAsync(
        string kind,
        int take = 100,
        CancellationToken cancellationToken = default);
    Task MarkAttemptAsync(Guid id, string? error, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
