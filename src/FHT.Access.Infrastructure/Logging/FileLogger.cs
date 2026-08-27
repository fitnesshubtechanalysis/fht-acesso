using FHT.Access.Application.Abstractions;
using FHT.Access.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FHT.Access.Infrastructure.Logging;

public sealed class FileLogger : IDiagnosticLog
{
    private readonly string _logFilePath;
    private readonly SqliteConnectionFactory? _sqlite;
    private readonly object _sync = new();

    public FileLogger(string? logDirectory = null, SqliteConnectionFactory? sqlite = null)
    {
        var dir = logDirectory
                  ?? Path.Combine(
                      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                      "FHT",
                      "Access",
                      "logs");
        Directory.CreateDirectory(dir);
        _logFilePath = Path.Combine(dir, $"access-{DateTime.UtcNow:yyyyMMdd}.log");
        _sqlite = sqlite;
    }

    public string LogFilePath => _logFilePath;

    public void Log(LogLevel level, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {message}";
        lock (_sync)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }

        if (_sqlite is not null)
        {
            try
            {
                InsertLogRow(level, message);
            }
            catch
            {
                // File log is primary; DB insert is best-effort.
            }
        }
    }

    public void Information(string message) => Log(LogLevel.Information, message);
    public void Warning(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);

    private void InsertLogRow(LogLevel level, string message)
    {
        using var connection = _sqlite!.Create();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Logs (Level, Message, CreatedAt)
            VALUES ($level, $message, $createdAt);
            """;
        command.Parameters.AddWithValue("$level", level.ToString());
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
