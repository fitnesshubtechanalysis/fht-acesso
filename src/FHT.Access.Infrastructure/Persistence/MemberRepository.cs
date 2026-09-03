using FHT.Access.Domain.Abstractions;
using FHT.Access.Domain.Entities;
using FHT.Access.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class MemberRepository : IMemberRepository
{
    private const string SelectColumns = """
        Id, Name, Status, AccessAllowed, ValidUntil, UpdatedAt, PhotoUrl, Cpf,
        ReasonCode, OperationalStatus, FinancialStatus, AccessStatus, AccessDecisionKind,
        ToleranceUsed, ToleranceOccurrenceId, OccurrenceCauseCode, RelationshipActionId,
        BypassPresence
        """;

    private readonly SqliteConnectionFactory _factory;

    public MemberRepository(SqliteConnectionFactory factory) => _factory = factory;

    public async Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Members WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapMember(reader) : null;
    }

    public async Task<IReadOnlyList<Member>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Members ORDER BY Name COLLATE NOCASE;";

        var list = new List<Member>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(MapMember(reader));
        return list;
    }

    public Task UpsertAsync(Member member, CancellationToken cancellationToken = default)
        => UpsertRangeAsync([member], cancellationToken);

    public async Task UpsertRangeAsync(IEnumerable<Member> members, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(members);
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var member in members)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO Members (
                    Id, Name, Status, AccessAllowed, ValidUntil, UpdatedAt, PhotoUrl, Cpf, LastSyncAt,
                    ReasonCode, OperationalStatus, FinancialStatus, AccessStatus, AccessDecisionKind,
                    ToleranceUsed, ToleranceOccurrenceId, OccurrenceCauseCode, RelationshipActionId,
                    BypassPresence)
                VALUES (
                    $id, $name, $status, $allowed, $validUntil, $updatedAt, $photoUrl, $cpf, $lastSync,
                    $reason, $operational, $financial, $access, $decisionKind,
                    $tolUsed, $occId, $occCause, $relId, $bypass)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    Status = excluded.Status,
                    AccessAllowed = excluded.AccessAllowed,
                    ValidUntil = excluded.ValidUntil,
                    UpdatedAt = excluded.UpdatedAt,
                    PhotoUrl = excluded.PhotoUrl,
                    Cpf = excluded.Cpf,
                    LastSyncAt = excluded.LastSyncAt,
                    ReasonCode = excluded.ReasonCode,
                    OperationalStatus = excluded.OperationalStatus,
                    FinancialStatus = excluded.FinancialStatus,
                    AccessStatus = excluded.AccessStatus,
                    AccessDecisionKind = excluded.AccessDecisionKind,
                    ToleranceUsed = excluded.ToleranceUsed,
                    ToleranceOccurrenceId = excluded.ToleranceOccurrenceId,
                    OccurrenceCauseCode = excluded.OccurrenceCauseCode,
                    RelationshipActionId = excluded.RelationshipActionId,
                    BypassPresence = excluded.BypassPresence;
                """;
            BindMember(command, member);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Member>> SearchAsync(
        string query,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
            return Array.Empty<Member>();

        var digits = DigitsOnly(q) ?? string.Empty;
        take = Math.Clamp(take, 1, 100);
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM Members
            WHERE Name LIKE $q
               OR ($digits != '' AND IFNULL(Cpf, '') LIKE $digits)
            ORDER BY Name COLLATE NOCASE
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$q", "%" + q + "%");
        command.Parameters.AddWithValue("$digits", digits.Length == 0 ? "" : "%" + digits + "%");
        command.Parameters.AddWithValue("$take", take);

        var list = new List<Member>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(MapMember(reader));
        return list;
    }

    public async Task<IReadOnlySet<Guid>> ListFaceMemberIdsAsync(
        IEnumerable<Guid> memberIds,
        CancellationToken cancellationToken = default)
    {
        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var names = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            names[i] = $"$id{i}";
            command.Parameters.AddWithValue(names[i], ids[i].ToString("D"));
        }

        command.CommandText = $"SELECT MemberId FROM MemberFaces WHERE MemberId IN ({string.Join(", ", names)});";
        var found = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            found.Add(Guid.Parse(reader.GetString(0)));
        return found;
    }

    public async Task SaveFaceAsync(MemberFace face, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(face);
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM MemberFaces WHERE MemberId = $memberId;";
            delete.Parameters.AddWithValue("$memberId", face.MemberId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO MemberFaces (Id, MemberId, FaceTemplate, ModelVersion, CreatedAt, UpdatedAt)
                VALUES ($id, $memberId, $template, $model, $createdAt, $updatedAt);
                """;
            insert.Parameters.AddWithValue(
                "$id",
                face.Id == Guid.Empty ? Guid.NewGuid().ToString("D") : face.Id.ToString("D"));
            insert.Parameters.AddWithValue("$memberId", face.MemberId.ToString("D"));
            insert.Parameters.AddWithValue("$template", face.Template);
            insert.Parameters.AddWithValue("$model", face.ModelVersion);
            insert.Parameters.AddWithValue(
                "$createdAt",
                ToIso(face.CreatedAt == default ? DateTime.UtcNow : face.CreatedAt)!);
            insert.Parameters.AddWithValue("$updatedAt", ToIso(DateTime.UtcNow)!);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemberFace?> GetFaceAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, MemberId, FaceTemplate, ModelVersion, CreatedAt
            FROM MemberFaces WHERE MemberId = $memberId;
            """;
        command.Parameters.AddWithValue("$memberId", memberId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new MemberFace
        {
            Id = Guid.Parse(reader.GetString(0)),
            MemberId = Guid.Parse(reader.GetString(1)),
            Template = (byte[])reader[2],
            ModelVersion = reader.GetString(3),
            CreatedAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    public async Task RemoveFaceAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MemberFaces WHERE MemberId = $memberId;";
        command.Parameters.AddWithValue("$memberId", memberId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BindMember(SqliteCommand command, Member member)
    {
        command.Parameters.AddWithValue("$id", member.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", member.Name);
        command.Parameters.AddWithValue("$status", member.Status.ToString());
        command.Parameters.AddWithValue("$allowed", member.AccessAllowed ? 1 : 0);
        command.Parameters.AddWithValue("$validUntil", (object?)ToIso(member.ValidUntil) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", ToIso(member.UpdatedAt)!);
        command.Parameters.AddWithValue("$photoUrl", (object?)member.PhotoUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$cpf", (object?)DigitsOnly(member.Cpf) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastSync", ToIso(DateTime.UtcNow)!);
        command.Parameters.AddWithValue("$reason", (object?)member.ReasonCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$operational", (object?)member.OperationalStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$financial", (object?)member.FinancialStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$access", (object?)member.AccessStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$decisionKind", (object?)member.AccessDecisionKind ?? DBNull.Value);
        command.Parameters.AddWithValue("$tolUsed", member.ToleranceUsed ? 1 : 0);
        command.Parameters.AddWithValue(
            "$occId",
            member.ToleranceOccurrenceId is null ? DBNull.Value : member.ToleranceOccurrenceId.Value.ToString("D"));
        command.Parameters.AddWithValue("$occCause", (object?)member.OccurrenceCauseCode ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$relId",
            member.RelationshipActionId is null ? DBNull.Value : member.RelationshipActionId.Value.ToString("D"));
        command.Parameters.AddWithValue("$bypass", member.BypassPresence ? 1 : 0);
    }

    private static Member MapMember(SqliteDataReader reader)
    {
        var m = new Member
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            Status = Enum.Parse<MemberStatus>(reader.GetString(2), ignoreCase: true),
            AccessAllowed = reader.GetInt64(3) != 0,
            ValidUntil = reader.IsDBNull(4)
                ? null
                : DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            PhotoUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
            Cpf = reader.IsDBNull(7) ? null : reader.GetString(7)
        };

        if (reader.FieldCount > 8 && !reader.IsDBNull(8)) m.ReasonCode = reader.GetString(8);
        if (reader.FieldCount > 9 && !reader.IsDBNull(9)) m.OperationalStatus = reader.GetString(9);
        if (reader.FieldCount > 10 && !reader.IsDBNull(10)) m.FinancialStatus = reader.GetString(10);
        if (reader.FieldCount > 11 && !reader.IsDBNull(11)) m.AccessStatus = reader.GetString(11);
        if (reader.FieldCount > 12 && !reader.IsDBNull(12)) m.AccessDecisionKind = reader.GetString(12);
        if (reader.FieldCount > 13 && !reader.IsDBNull(13)) m.ToleranceUsed = reader.GetInt64(13) != 0;
        if (reader.FieldCount > 14 && !reader.IsDBNull(14)) m.ToleranceOccurrenceId = Guid.Parse(reader.GetString(14));
        if (reader.FieldCount > 15 && !reader.IsDBNull(15)) m.OccurrenceCauseCode = reader.GetString(15);
        if (reader.FieldCount > 16 && !reader.IsDBNull(16)) m.RelationshipActionId = Guid.Parse(reader.GetString(16));
        if (reader.FieldCount > 17 && !reader.IsDBNull(17)) m.BypassPresence = reader.GetInt64(17) != 0;
        return m;
    }

    private static string? DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static string? ToIso(DateTime? value) => value?.ToUniversalTime().ToString("O");
}
