using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Abstractions;

public interface IAccessEventRepository
{
    Task AddAsync(AccessEvent accessEvent, CancellationToken cancellationToken = default);
    Task UpdateAsync(AccessEvent accessEvent, CancellationToken cancellationToken = default);
    Task<AccessEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccessEvent>> GetBySyncStatusAsync(SyncStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the last allowed event for the member is an entry (open visit).
    /// </summary>
    Task<bool> IsMemberPresentAsync(Guid memberId, CancellationToken cancellationToken = default);
}
