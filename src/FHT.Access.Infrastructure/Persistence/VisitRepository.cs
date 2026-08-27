using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class VisitRepository : IVisitRepository
{
    private readonly SqliteConnectionFactory _factory;

    public VisitRepository(SqliteConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(VisitRecord visit, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Visits (
                Id, PersonId, UnitId, EntryAttemptId, ExitAttemptId,
                EnteredAt, ExitedAt, Status, CloseReason, CreatedAt, UpdatedAt)
            VALUES ($id, $person, $unit, $entry, $exit, $in, $out, $status, $reason, $created, $upd);
            """;
        Bind(cmd, visit);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(VisitRecord visit, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Visits SET
                ExitAttemptId = $exit, ExitedAt = $out, Status = $status,
                CloseReason = $reason, UpdatedAt = $upd
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", visit.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$exit", (object?)visit.ExitAttemptId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$out", (object?)visit.ExitedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", visit.Status.ToString());
        cmd.Parameters.AddWithValue("$reason", (object?)visit.CloseReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$upd", visit.UpdatedAt.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<VisitRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Visits WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<VisitRecord>> GetOpenVisitsAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Visits WHERE Status = 'Open';";
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<VisitRecord?> GetOpenVisitForPersonAsync(Guid personId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Visits WHERE PersonId = $id AND Status = 'Open' LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", personId.ToString("D"));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    private static async Task<List<VisitRecord>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<VisitRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(Map(r));
        return list;
    }

    private static VisitRecord Map(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
        PersonId = Guid.Parse(r.GetString(r.GetOrdinal("PersonId"))),
        UnitId = r.GetString(r.GetOrdinal("UnitId")),
        EntryAttemptId = r.IsDBNull(r.GetOrdinal("EntryAttemptId")) ? null : Guid.Parse(r.GetString(r.GetOrdinal("EntryAttemptId"))),
        ExitAttemptId = r.IsDBNull(r.GetOrdinal("ExitAttemptId")) ? null : Guid.Parse(r.GetString(r.GetOrdinal("ExitAttemptId"))),
        EnteredAt = r.IsDBNull(r.GetOrdinal("EnteredAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("EnteredAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        ExitedAt = r.IsDBNull(r.GetOrdinal("ExitedAt")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("ExitedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        Status = Enum.Parse<VisitStatus>(r.GetString(r.GetOrdinal("Status")), ignoreCase: true),
        CloseReason = r.IsDBNull(r.GetOrdinal("CloseReason")) ? null : r.GetString(r.GetOrdinal("CloseReason")),
        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("CreatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("UpdatedAt")), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    private static void Bind(SqliteCommand cmd, VisitRecord v)
    {
        cmd.Parameters.AddWithValue("$id", v.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$person", v.PersonId.ToString("D"));
        cmd.Parameters.AddWithValue("$unit", v.UnitId);
        cmd.Parameters.AddWithValue("$entry", (object?)v.EntryAttemptId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exit", (object?)v.ExitAttemptId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", (object?)v.EnteredAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$out", (object?)v.ExitedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", v.Status.ToString());
        cmd.Parameters.AddWithValue("$reason", (object?)v.CloseReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", v.CreatedAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$upd", v.UpdatedAt.ToUniversalTime().ToString("O"));
    }
}
