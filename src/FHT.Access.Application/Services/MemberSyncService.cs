using FHT.Access.Application.Abstractions;
using FHT.Access.Application.Dtos;
using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;

namespace FHT.Access.Application.Services;

public sealed class MemberSyncService
{
    private readonly IGestaoAccessClient _client;
    private readonly IMemberRepository _members;
    private readonly ISettingsStore _settings;
    private readonly IAccessDeviceContext _device;

    public MemberSyncService(
        IGestaoAccessClient client,
        IMemberRepository members,
        ISettingsStore settings,
        IAccessDeviceContext device)
    {
        _client = client;
        _members = members;
        _settings = settings;
        _device = device;
    }

    public async Task<int> SyncAsync(
        string unitId,
        CancellationToken cancellationToken = default,
        bool full = false)
    {
        var syncState = await _settings.GetSyncStateAsync(cancellationToken).ConfigureAwait(false);
        var remote = await _client
            .GetMembersAsync(unitId, full ? null : syncState.LastMembersSyncAt, cancellationToken)
            .ConfigureAwait(false);

        if (remote.Count == 0)
        {
            syncState.LastMembersSyncAt = DateTime.UtcNow;
            await _settings.SaveSyncStateAsync(syncState, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var locals = remote.Select(MapToMember).ToList();
        await _members.UpsertRangeAsync(locals, cancellationToken).ConfigureAwait(false);

        var maxUpdated = locals.Max(m => m.UpdatedAt);
        syncState.LastMembersSyncAt = maxUpdated;
        await _settings.SaveSyncStateAsync(syncState, cancellationToken).ConfigureAwait(false);

        return locals.Count;
    }

    public async Task<int> PullByQueryAsync(string unitId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unitId) || string.IsNullOrWhiteSpace(query))
            return 0;

        var remote = await _client
            .GetMembersAsync(unitId, null, cancellationToken, query)
            .ConfigureAwait(false);
        if (remote.Count == 0)
            return 0;

        await _members.UpsertRangeAsync(remote.Select(MapToMember).ToList(), cancellationToken)
            .ConfigureAwait(false);
        return remote.Count;
    }

    /// <summary>Atualiza um aluno no cache local (status de acesso) antes de liberar na catraca.</summary>
    public async Task<bool> RefreshMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var unitId = _device.UnitId?.Trim();
        if (string.IsNullOrWhiteSpace(unitId))
            return false;

        try
        {
            if (!string.IsNullOrWhiteSpace(_device.DeviceId)
                && !string.IsNullOrWhiteSpace(_device.DeviceSecret))
            {
                await _client
                    .EnsureAuthenticatedAsync(_device.DeviceId.Trim(), _device.DeviceSecret, cancellationToken)
                    .ConfigureAwait(false);
            }

            var remote = await _client
                .GetMembersAsync(unitId, null, cancellationToken, memberId.ToString("D"))
                .ConfigureAwait(false);
            var hit = remote.FirstOrDefault(m => m.Id == memberId);
            if (hit is null)
                return false;

            await _members.UpsertAsync(MapToMember(hit), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Member MapToMember(MemberDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Status = ParseStatus(dto.Status),
        AccessAllowed = dto.AccessAllowed,
        ValidUntil = dto.ValidUntil,
        UpdatedAt = dto.UpdatedAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dto.UpdatedAt, DateTimeKind.Utc)
            : dto.UpdatedAt.ToUniversalTime(),
        PhotoUrl = dto.PhotoUrl,
        Cpf = dto.Cpf,
        ReasonCode = dto.ReasonCode,
        OperationalStatus = dto.OperationalStatus,
        FinancialStatus = dto.FinancialStatus,
        AccessStatus = dto.AccessStatus,
        AccessDecisionKind = dto.AccessDecisionKind,
        ToleranceUsed = dto.ToleranceUsed,
        ToleranceOccurrenceId = dto.ToleranceOccurrenceId,
        OccurrenceCauseCode = dto.OccurrenceCauseCode,
        RelationshipActionId = dto.RelationshipActionId,
        BypassPresence = dto.BypassPresence
    };

    private static MemberStatus ParseStatus(string status)
    {
        if (Enum.TryParse<MemberStatus>(status, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "ativo" or "active" => MemberStatus.Active,
            "bloqueado" or "blocked" => MemberStatus.Blocked,
            _ => MemberStatus.Inactive
        };
    }
}
