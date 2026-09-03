namespace FHT.Access.Application.Abstractions;

/// <summary>
/// Abstração sobre o mecanismo concreto de update (Velopack).
/// Permite manter Application sem dependência de plataforma.
/// </summary>
public interface IAppUpdater
{
    /// <summary>Versão atual instalada.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Verifica se existe versão mais nova na <paramref name="feedUrl"/>.
    /// Retorna null se não houver.
    /// </summary>
    Task<string?> CheckForUpdateAsync(string feedUrl, CancellationToken ct = default);

    /// <summary>
    /// Baixa a atualização, reportando progresso (0–100).
    /// Deve ser chamado após <see cref="CheckForUpdateAsync"/> retornar não-null.
    /// </summary>
    Task DownloadUpdateAsync(
        string feedUrl,
        IProgress<int> progress,
        CancellationToken ct = default);

    /// <summary>Aplica e reinicia o app. Não retorna.</summary>
    void ApplyAndRestart();
}
