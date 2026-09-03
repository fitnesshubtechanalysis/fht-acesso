using Microsoft.Data.Sqlite;

namespace FHT.Access.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _factory;

    public DatabaseInitializer(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public void EnsureCreated()
        => InitializeAsync().GetAwaiter().GetResult();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Members (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Status TEXT NOT NULL,
                AccessAllowed INTEGER NOT NULL DEFAULT 0,
                ValidUntil TEXT NULL,
                UpdatedAt TEXT NOT NULL,
                PhotoUrl TEXT NULL,
                Cpf TEXT NULL,
                LastSyncAt TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS MemberFaces (
                Id TEXT NOT NULL PRIMARY KEY,
                MemberId TEXT NOT NULL,
                FaceTemplate BLOB NOT NULL,
                ModelVersion TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL,
                FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_MemberFaces_MemberId ON MemberFaces(MemberId);

            CREATE TABLE IF NOT EXISTS AccessPermissions (
                Id TEXT NOT NULL PRIMARY KEY,
                MemberId TEXT NOT NULL,
                Allowed INTEGER NOT NULL DEFAULT 0,
                ValidUntil TEXT NULL,
                ReasonCode TEXT NULL,
                FOREIGN KEY (MemberId) REFERENCES Members(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AccessEvents (
                Id TEXT NOT NULL PRIMARY KEY,
                MemberId TEXT NULL,
                Direction TEXT NOT NULL,
                Status TEXT NOT NULL,
                PassageConfirmed INTEGER NOT NULL DEFAULT 0,
                SyncStatus TEXT NOT NULL,
                OccurredAt TEXT NOT NULL,
                Source TEXT NOT NULL,
                DeviceId TEXT NULL,
                DenialReason TEXT NULL,
                SyncedAt TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AccessEvents_SyncStatus ON AccessEvents(SyncStatus);

            CREATE TABLE IF NOT EXISTS PendingSync (
                Id TEXT NOT NULL PRIMARY KEY,
                Kind TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                LastError TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_PendingSync_CreatedAt ON PendingSync(CreatedAt);

            CREATE TABLE IF NOT EXISTS Devices (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                UnitId TEXT NOT NULL,
                Serial TEXT NULL,
                IpAddress TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SyncState (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                LastMembersSyncAt TEXT NULL,
                LastEventsSyncAt TEXT NULL,
                Cursor TEXT NULL
            );

            INSERT OR IGNORE INTO SyncState (Id) VALUES (1);

            CREATE TABLE IF NOT EXISTS Logs (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Level TEXT NOT NULL,
                Message TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PresenceStates (
                PersonId TEXT NOT NULL PRIMARY KEY,
                UnitId TEXT NOT NULL,
                State TEXT NOT NULL,
                ActiveVisitId TEXT NULL,
                PendingAttemptId TEXT NULL,
                LastConfirmedDirection TEXT NULL,
                LastConfirmedAt TEXT NULL,
                Version INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AccessAttempts (
                Id TEXT NOT NULL PRIMARY KEY,
                PersonId TEXT NOT NULL,
                UnitId TEXT NOT NULL,
                TurnstileSerial TEXT NULL,
                RequestedDirection TEXT NOT NULL,
                Status TEXT NOT NULL,
                Source TEXT NOT NULL,
                DeviceId TEXT NULL,
                IdempotencyKey TEXT NOT NULL,
                AccessEventId TEXT NULL,
                RecognizedAt TEXT NOT NULL,
                ReleasedAt TEXT NULL,
                PassageConfirmedAt TEXT NULL,
                FailureReason TEXT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_AccessAttempts_IdempotencyKey
                ON AccessAttempts(IdempotencyKey);

            CREATE TABLE IF NOT EXISTS Visits (
                Id TEXT NOT NULL PRIMARY KEY,
                PersonId TEXT NOT NULL,
                UnitId TEXT NOT NULL,
                EntryAttemptId TEXT NULL,
                ExitAttemptId TEXT NULL,
                EnteredAt TEXT NULL,
                ExitedAt TEXT NULL,
                Status TEXT NOT NULL,
                CloseReason TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Visits_Person_Status ON Visits(PersonId, Status);

            CREATE TABLE IF NOT EXISTS PresenceCorrections (
                Id TEXT NOT NULL PRIMARY KEY,
                PersonId TEXT NOT NULL,
                UnitId TEXT NOT NULL,
                PreviousState TEXT NOT NULL,
                NewState TEXT NOT NULL,
                OperatorId TEXT NOT NULL,
                Reason TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var addedCpf = await EnsureColumnAsync(connection, "Members", "Cpf", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        foreach (var (col, type) in new (string Col, string Type)[]
        {
            ("ReasonCode", "TEXT"),
            ("OperationalStatus", "TEXT"),
            ("FinancialStatus", "TEXT"),
            ("AccessStatus", "TEXT"),
            ("AccessDecisionKind", "TEXT"),
            ("ToleranceUsed", "INTEGER"),
            ("ToleranceOccurrenceId", "TEXT"),
            ("OccurrenceCauseCode", "TEXT"),
            ("RelationshipActionId", "TEXT"),
            ("BypassPresence", "INTEGER"),
        })
        {
            await EnsureColumnAsync(connection, "Members", col, type, cancellationToken).ConfigureAwait(false);
        }

        if (addedCpf)
        {
            await using var reset = connection.CreateCommand();
            reset.CommandText = "UPDATE SyncState SET LastMembersSyncAt = NULL WHERE Id = 1;";
            await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var index = connection.CreateCommand())
        {
            index.CommandText = """
                CREATE INDEX IF NOT EXISTS IX_Members_Cpf ON Members(Cpf);
                CREATE INDEX IF NOT EXISTS IX_Members_Name ON Members(Name COLLATE NOCASE);
                """;
            await index.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await EnsureColumnAsync(connection, "AccessEvents", "AttemptId", "TEXT", cancellationToken)
            .ConfigureAwait(false);
        await EnsureColumnAsync(connection, "AccessEvents", "VisitId", "TEXT", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string type,
        CancellationToken cancellationToken)
    {
        var exists = false;
        await using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return false;

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
