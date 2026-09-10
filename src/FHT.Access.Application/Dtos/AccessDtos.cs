namespace FHT.Access.Application.Dtos;

public sealed record DeviceAuthResult(
    string AccessToken,
    string UnitId,
    string DeviceId,
    DateTime? ExpiresAt);

public sealed record MemberDto(
    Guid Id,
    string Name,
    string Status,
    bool AccessAllowed,
    DateTime? ValidUntil,
    DateTime UpdatedAt,
    string? PhotoUrl = null,
    string? ReasonCode = null,
    string? Cpf = null,
    string? OperationalStatus = null,
    string? FinancialStatus = null,
    string? AccessStatus = null,
    string? AccessDecisionKind = null,
    bool ToleranceUsed = false,
    Guid? ToleranceOccurrenceId = null,
    string? OccurrenceCauseCode = null,
    Guid? RelationshipActionId = null,
    bool BypassPresence = false);

public sealed record AccessEvaluateResultDto(
    string Kind,
    string? CauseCode,
    string Operational,
    string Financial,
    string Access,
    string PublicMessage,
    string PrivateMessage,
    bool AllowAutomaticRelease,
    bool ConsumeToleranceOnPassage,
    bool RequiresManualRelease,
    Guid? OccurrenceId,
    Guid? RelationshipActionId);

public sealed record AccessEventDto(
    Guid Id,
    Guid? MemberId,
    string Direction,
    string Status,
    bool PassageConfirmed,
    DateTime OccurredAt,
    string Source,
    string? DeviceId = null,
    string? DenialReason = null,
    Guid? AttemptId = null,
    Guid? VisitId = null);

public sealed record GateCommandDto(
    Guid Id,
    Guid? CustomerId,
    string? FullName,
    string Direction,
    string? Reason,
    DateTime OccurredAt);

public sealed record DeviceRemoteConfigDto(
    bool? FreeGateMode,
    int? PassageTimeoutSec);

public sealed record GateCommandsPollDto(
    IReadOnlyList<GateCommandDto> Commands,
    bool SyncPending,
    DeviceRemoteConfigDto? Configuration);
