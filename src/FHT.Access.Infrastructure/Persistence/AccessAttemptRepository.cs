using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class AccessAttemptRepository : IAccessAttemptRepository
{
    private readonly SqliteConnectionFactory _factory;

    public AccessAttemptRepository(SqliteConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(AccessAttemptRecord attempt, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AccessAttempts (
                Id, PersonId, UnitId, TurnstileSerial, RequestedDirection, Status, Source,
                DeviceId, IdempotencyKey, AccessEventId, RecognizedAt, ReleasedAt,
                PassageConfirmedAt, FailureReason, CreatedAt)
            VALUES (
                $id, $person, $unit, $serial, $dir, $status, $source, $device, $key,
                $eventId, $rec, $rel, $pass, $fail, $created);
            """;
        Bind(cmd, attempt);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AccessAttemptRecord attempt, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AccessAttempts SET
                Status = $status, ReleasedAt = $rel, PassageConfirmedAt = $pass,
                FailureReason = $fail, AccessEventId = $eventId
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", attempt.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$status", attempt.Status.ToString());
        cmd.Parameters.AddWithValue("$rel", (object?)attempt.ReleasedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pass", (object?)attempt.PassageConfirmedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fail", (object?)attempt.FailureReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$eventId", (object?)attempt.AccessEventId?.ToString("D") ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<AccessAttemptRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AccessAttempts WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<AccessAttemptRecord?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AccessAttempts WHERE IdempotencyKey = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    private static AccessAttemptRecord Map(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        PersonId = Guid.Parse(r.GetString(r.GetOrdinal("PersonId"))),
        UnitId = r.GetString(r.GetOrdinal("UnitId")),
        TurnstileSerial = r.IsDBNull(r.GetOrdinal("TurnstileSerial")) ? null : r.GetString(r.GetOrdinal("TurnstileSerial")),
        RequestedDirection = Enum.Parse<AccessDirection>(r.GetString(r.GetOrdinal("RequestedDirection")), ignoreCase: true),
        Status = Enum.Parse<AccessAttemptStatus>(r.GetString(r.GetOrdinal("Status")), ignoreCase: true),
        Source = r.GetString(r.GetOrdinal("Source")),
        DeviceId = r.IsDBNull(r.GetOrdinal("DeviceId")) ? null : r.GetString(r.GetOrdinal("DeviceId")),
        IdempotencyKey = r.GetString(r.GetOrdinal("IdempotencyKey")),
        AccessEventId = r.IsDBNull(r.GetOrdinal("AccessEventId")) ? null : Guid.Parse(r.GetString(r.GetOrdinal("AccessEventId"))),
        RecognizedAt = DateTime.Parse(r.GetString(r.GetOrdinal("RecognizedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        ReleasedAt = r.IsDBNull(r.GetOrdinal("ReleasedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("ReleasedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        PassageConfirmedAt = r.IsDBNull(r.GetOrdinal("PassageConfirmedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("PassageConfirmedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        FailureReason = r.IsDBNull(r.GetOrdinal("FailureReason")) ? null : r.GetString(r.GetOrdinal("FailureReason")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    private static void Bind(SqliteCommand cmd, AccessAttemptRecord a)
    {
        cmd.Parameters.AddWithValue("$id", a.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$person", a.PersonId.ToString("D"));
        cmd.Parameters.AddWithValue("$unit", a.UnitId);
        cmd.Parameters.AddWithValue("$serial", (object?)a.TurnstileSerial ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dir", a.RequestedDirection.ToString());
        cmd.Parameters.AddWithValue("$status", a.Status.ToString());
        cmd.Parameters.AddWithValue("$source", a.Source);
        cmd.Parameters.AddWithValue("$device", (object?)a.DeviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$key", a.IdempotencyKey);
        cmd.Parameters.AddWithValue("$eventId", (object?)a.AccessEventId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rec", a.RecognizedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$rel", (object?)a.ReleasedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pass", (object?)a.PassageConfirmedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fail", (object?)a.FailureReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", a.CreatedAt.ToUniversalTime().ToString("O"));
    }
}
