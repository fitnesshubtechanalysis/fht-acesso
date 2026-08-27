using FHT.Access.Application.Dtos;

namespace FHT.Access.Application.Abstractions;

public interface IGestaoAccessClient
{
    Task<DeviceAuthResult> AuthenticateDeviceAsync(
        string deviceId,
        string deviceSecret,
        CancellationToken ct = default);

    Task<IReadOnlyList<MemberDto>> GetMembersAsync(
        string unitId,
        DateTime? updatedSince,
        CancellationToken ct = default,
        string? query = null);

    Task AcknowledgeEventsAsync(
        string unitId,
        IReadOnlyList<AccessEventDto> events,
        CancellationToken ct = default);

    /// <summary>Upload JPEG face enrollment as the member photo on Gestão.</summary>
    Task<string> UploadMemberPhotoAsync(
        string unitId,
        Guid memberId,
        byte[] jpeg,
        CancellationToken ct = default);

    /// <summary>
    /// Authenticates when the device token is missing, near expiry, or <paramref name="force"/> is true.
    /// </summary>
    Task EnsureAuthenticatedAsync(
        string deviceId,
        string deviceSecret,
        CancellationToken ct = default,
        bool force = false);

    Task<AccessEvaluateResultDto?> EvaluateAccessAsync(
        string unitId,
        Guid memberId,
        CancellationToken ct = default);

    Task ConsumeToleranceAsync(
        string unitId,
        Guid memberId,
        Guid? accessEventId,
        string? deviceId,
        CancellationToken ct = default);

    Task RecordBlockedAttemptAsync(
        string unitId,
        Guid memberId,
        CancellationToken ct = default);
}
