using FHT.Access.Domain.Entities;

namespace FHT.Access.Domain.Abstractions;

public interface IMemberRepository
{
    Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Member>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(Member member, CancellationToken cancellationToken = default);
    Task UpsertRangeAsync(IEnumerable<Member> members, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Member>> SearchAsync(string query, int take = 30, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> ListFaceMemberIdsAsync(IEnumerable<Guid> memberIds, CancellationToken cancellationToken = default);
    Task SaveFaceAsync(MemberFace face, CancellationToken cancellationToken = default);
    Task<MemberFace?> GetFaceAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task RemoveFaceAsync(Guid memberId, CancellationToken cancellationToken = default);
}
