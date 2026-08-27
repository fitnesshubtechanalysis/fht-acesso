using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class AccessEventRepository : IAccessEventRepository
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IPresenceRepository? _presence;

    public AccessEventRepository(SqliteConnectionFactory factory, IPresenceRepository? presence = null)
    {
        _factory = factory;
        _presence = presence;
    }

    public async Task AddAsync(AccessEvent accessEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessEvent);
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AccessEvents (
                Id, MemberId, Direction, Status, PassageConfirmed, SyncStatus,
                OccurredAt, Source, DeviceId, DenialReason, SyncedAt, AttemptId, VisitId)
            VALUES (
                $id, $memberId, $direction, $status, $passage, $sync,
                $occurredAt, $source, $deviceId, $denial, $syncedAt, $attemptId, $visitId);
            """;
        Bind(command, accessEvent);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AccessEvent accessEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessEvent);
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AccessEvents SET
                MemberId = $memberId,
                Direction = $direction,
                Status = $status,
                PassageConfirmed = $passage,
                SyncStatus = $sync,
                OccurredAt = $occurredAt,
                Source = $source,
                DeviceId = $deviceId,
                DenialReason = $denial,
                SyncedAt = $syncedAt,
                AttemptId = $attemptId,
                VisitId = $visitId
            WHERE Id = $id;
            """;
        Bind(command, accessEvent);
        if (accessEvent.SyncStatus == SyncStatus.Synced)
        {
            command.Parameters["$syncedAt"].Value = DateTime.UtcNow.ToString("O");
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccessEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return Map(reader);
    }

    public async Task<IReadOnlyList<AccessEvent>> GetBySyncStatusAsync(
        SyncStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + """
             WHERE SyncStatus = $sync
            ORDER BY OccurredAt;
            """;
        command.Parameters.AddWithValue("$sync", status.ToString());

        var list = new List<AccessEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(Map(reader));

        return list;
    }

    public async Task<bool> IsMemberPresentAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        if (_presence is not null)
        {
            var p = await _presence.GetAsync(memberId, cancellationToken).ConfigureAwait(false);
            if (p is not null)
                return p.State == PresenceStateKind.Inside;
        }

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Direction
            FROM AccessEvents
            WHERE MemberId = $memberId
              AND Status = $status
              AND PassageConfirmed = 1
            ORDER BY OccurredAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$memberId", memberId.ToString("D"));
        command.Parameters.AddWithValue("$status", AccessEventStatus.Allowed.ToString());

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalar is null or DBNull)
            return false;

        var direction = Enum.Parse<AccessDirection>(Convert.ToString(scalar)!, ignoreCase: true);
        return direction == AccessDirection.Entry;
    }

    private const string SelectColumns = """
        SELECT Id, MemberId, Direction, Status, PassageConfirmed, SyncStatus,
               OccurredAt, Source, DeviceId, DenialReason, AttemptId, VisitId
        FROM AccessEvents
        """;

    private static void Bind(SqliteCommand command, AccessEvent accessEvent)
    {
        command.Parameters.AddWithValue("$id", accessEvent.Id.ToString("D"));
        command.Parameters.AddWithValue(
            "$memberId",
            accessEvent.MemberId is null ? DBNull.Value : accessEvent.MemberId.Value.ToString("D"));
        command.Parameters.AddWithValue("$direction", accessEvent.Direction.ToString());
        command.Parameters.AddWithValue("$status", accessEvent.Status.ToString());
        command.Parameters.AddWithValue("$passage", accessEvent.PassageConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$sync", accessEvent.SyncStatus.ToString());
        command.Parameters.AddWithValue("$occurredAt", accessEvent.OccurredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$source", accessEvent.Source);
        command.Parameters.AddWithValue("$deviceId", (object?)accessEvent.DeviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$denial", (object?)accessEvent.DenialReason ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$syncedAt",
            accessEvent.SyncStatus == SyncStatus.Synced
                ? DateTime.UtcNow.ToString("O")
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$attemptId",
            accessEvent.AttemptId is null ? DBNull.Value : accessEvent.AttemptId.Value.ToString("D"));
        command.Parameters.AddWithValue(
            "$visitId",
            accessEvent.VisitId is null ? DBNull.Value : accessEvent.VisitId.Value.ToString("D"));
    }

    private static AccessEvent Map(SqliteDataReader reader)
    {
        var ev = new AccessEvent
        {
            Id = Guid.Parse(reader.GetString(0)),
            MemberId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            Direction = Enum.Parse<AccessDirection>(reader.GetString(2), ignoreCase: true),
            Status = Enum.Parse<AccessEventStatus>(reader.GetString(3), ignoreCase: true),
            PassageConfirmed = reader.GetInt64(4) != 0,
            SyncStatus = Enum.Parse<SyncStatus>(reader.GetString(5), ignoreCase: true),
            OccurredAt = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Source = reader.GetString(7),
            DeviceId = reader.IsDBNull(8) ? null : reader.GetString(8),
            DenialReason = reader.IsDBNull(9) ? null : reader.GetString(9)
        };

        if (reader.FieldCount > 10 && !reader.IsDBNull(10))
            ev.AttemptId = Guid.Parse(reader.GetString(10));
        if (reader.FieldCount > 11 && !reader.IsDBNull(11))
            ev.VisitId = Guid.Parse(reader.GetString(11));

        return ev;
    }
}
