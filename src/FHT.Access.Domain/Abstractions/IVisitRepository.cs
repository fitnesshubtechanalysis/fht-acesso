using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Domain.Abstractions;

public interface IVisitRepository
{
    Task AddAsync(VisitRecord visit, CancellationToken ct = default);
    Task UpdateAsync(VisitRecord visit, CancellationToken ct = default);
    Task<VisitRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<VisitRecord>> GetOpenVisitsAsync(CancellationToken ct = default);
    Task<VisitRecord?> GetOpenVisitForPersonAsync(Guid personId, CancellationToken ct = default);
}
