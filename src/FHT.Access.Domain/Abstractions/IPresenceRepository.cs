using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Abstractions;

public interface IPresenceRepository
{
    Task<PersonPresenceState?> GetAsync(Guid personId, CancellationToken ct = default);
    Task UpsertAsync(PersonPresenceState state, CancellationToken ct = default);
    Task<IReadOnlyList<PersonPresenceState>> GetByStateAsync(
        PresenceStateKind state,
        CancellationToken ct = default);
    Task<IReadOnlyList<PersonPresenceState>> GetAllAsync(CancellationToken ct = default);
}
