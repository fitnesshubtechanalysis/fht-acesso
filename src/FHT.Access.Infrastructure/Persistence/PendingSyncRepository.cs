using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class PendingSyncRepository : IPendingSyncRepository
{
    private readonly SqliteConnectionFactory _factory;

    public PendingSyncRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task EnqueueAsync(PendingSync item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PendingSync (Id, Kind, PayloadJson, CreatedAt, Attempts, LastError)
            VALUES ($id, $kind, $payload, $createdAt, $attempts, $lastError);
            """;
        command.Parameters.AddWithValue("$id", item.Id == Guid.Empty ? Guid.NewGuid().ToString("D") : item.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", item.Kind);
        command.Parameters.AddWithValue("$payload", item.PayloadJson);
        command.Parameters.AddWithValue(
            "$createdAt",
            (item.CreatedAt == default ? DateTime.UtcNow : item.CreatedAt).ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$attempts", item.Attempts);
        command.Parameters.AddWithValue("$lastError", (object?)item.LastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingSync>> GetPendingAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, PayloadJson, CreatedAt, Attempts, LastError
            FROM PendingSync
            ORDER BY CreatedAt
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", take);

        var list = new List<PendingSync>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new PendingSync
            {
                Id = Guid.Parse(reader.GetString(0)),
                Kind = reader.GetString(1),
                PayloadJson = reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Attempts = (int)reader.GetInt64(4),
                LastError = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return list;
    }

    public async Task MarkAttemptAsync(Guid id, string? error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PendingSync
            SET Attempts = Attempts + 1,
                LastError = $error
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PendingSync WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
