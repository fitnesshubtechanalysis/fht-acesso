namespace FHT.Access.Application.Abstractions;

public interface IDiagnosticLog
{
    void Information(string message);
    void Warning(string message);
    void Error(string message);
}
