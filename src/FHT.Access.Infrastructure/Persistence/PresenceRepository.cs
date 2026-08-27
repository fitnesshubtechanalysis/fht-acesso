using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class PresenceRepository : IPresenceRepository
{
    private readonly SqliteConnectionFactory _factory;

    public PresenceRepository(SqliteConnectionFactory factory) => _factory = factory;

    public async Task<PersonPresenceState?> GetAsync(Guid personId, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT PersonId, UnitId, State, ActiveVisitId, PendingAttemptId,
                   LastConfirmedDirection, LastConfirmedAt, Version, UpdatedAt
            FROM PresenceStates WHERE PersonId = $id;
            """;
        cmd.Parameters.AddWithValue("$id", personId.ToString("D"));
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? Map(r) : null;
    }

    public async Task UpsertAsync(PersonPresenceState state, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PresenceStates (
                PersonId, UnitId, State, ActiveVisitId, PendingAttemptId,
                LastConfirmedDirection, LastConfirmedAt, Version, UpdatedAt)
            VALUES ($id, $unit, $state, $visit, $pending, $dir, $at, $ver, $upd)
            ON CONFLICT(PersonId) DO UPDATE SET
                UnitId = $unit, State = $state, ActiveVisitId = $visit,
                PendingAttemptId = $pending, LastConfirmedDirection = $dir,
                LastConfirmedAt = $at, Version = $ver, UpdatedAt = $upd;
            """;
        Bind(cmd, state);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PersonPresenceState>> GetByStateAsync(
        PresenceStateKind state,
        CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT PersonId, UnitId, State, ActiveVisitId, PendingAttemptId,
                   LastConfirmedDirection, LastConfirmedAt, Version, UpdatedAt
            FROM PresenceStates WHERE State = $state;
            """;
        cmd.Parameters.AddWithValue("$state", state.ToString());
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PersonPresenceState>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT PersonId, UnitId, State, ActiveVisitId, PendingAttemptId,
                   LastConfirmedDirection, LastConfirmedAt, Version, UpdatedAt
            FROM PresenceStates;
            """;
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<List<PersonPresenceState>> ReadAllAsync(
        SqliteCommand cmd,
        CancellationToken ct)
    {
        var list = new List<PersonPresenceState>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(Map(r));
        return list;
    }

    private static PersonPresenceState Map(SqliteDataReader r) => new()
    {
        PersonId = Guid.Parse(r.GetString(0)),
        UnitId = r.GetString(1),
        State = Enum.Parse<PresenceStateKind>(r.GetString(2), ignoreCase: true),
        ActiveVisitId = r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
        PendingAttemptId = r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
        LastConfirmedDirection = r.IsDBNull(5)
            ? null
            : Enum.Parse<AccessDirection>(r.GetString(5), ignoreCase: true),
        LastConfirmedAt = r.IsDBNull(6)
            ? null
            : DateTime.Parse(r.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
        Version = r.GetInt32(7),
        UpdatedAt = DateTime.Parse(r.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    private static void Bind(SqliteCommand cmd, PersonPresenceState s)
    {
        cmd.Parameters.AddWithValue("$id", s.PersonId.ToString("D"));
        cmd.Parameters.AddWithValue("$unit", s.UnitId);
        cmd.Parameters.AddWithValue("$state", s.State.ToString());
        cmd.Parameters.AddWithValue("$visit", (object?)s.ActiveVisitId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pending", (object?)s.PendingAttemptId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dir", (object?)s.LastConfirmedDirection?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", (object?)s.LastConfirmedAt?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ver", s.Version);
        cmd.Parameters.AddWithValue("$upd", s.UpdatedAt.ToUniversalTime().ToString("O"));
    }
}
