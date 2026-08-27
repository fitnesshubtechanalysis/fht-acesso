using FHT.Access.Domain.Entities;

namespace FHT.Access.Domain.Abstractions;

public interface IAccessAttemptRepository
{
    Task AddAsync(AccessAttemptRecord attempt, CancellationToken ct = default);
    Task UpdateAsync(AccessAttemptRecord attempt, CancellationToken ct = default);
    Task<AccessAttemptRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AccessAttemptRecord?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default);
}
