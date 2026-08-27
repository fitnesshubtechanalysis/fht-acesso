using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class PresenceCorrectionRepository : IPresenceCorrectionRepository
{
    private readonly SqliteConnectionFactory _factory;

    public PresenceCorrectionRepository(SqliteConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(PresenceCorrectionRecord correction, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PresenceCorrections (
                Id, PersonId, UnitId, PreviousState, NewState, OperatorId, Reason, CreatedAt)
            VALUES ($id, $person, $unit, $prev, $new, $op, $reason, $at);
            """;
        cmd.Parameters.AddWithValue("$id", correction.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$person", correction.PersonId.ToString("D"));
        cmd.Parameters.AddWithValue("$unit", correction.UnitId);
        cmd.Parameters.AddWithValue("$prev", correction.PreviousState.ToString());
        cmd.Parameters.AddWithValue("$new", correction.NewState.ToString());
        cmd.Parameters.AddWithValue("$op", correction.OperatorId);
        cmd.Parameters.AddWithValue("$reason", correction.Reason);
        cmd.Parameters.AddWithValue("$at", correction.CreatedAt.ToUniversalTime().ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
