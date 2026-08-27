using FHT.Access.Domain.Entities;

namespace FHT.Access.Domain.Abstractions;

public interface IPresenceCorrectionRepository
{
    Task AddAsync(PresenceCorrectionRecord correction, CancellationToken ct = default);
}
