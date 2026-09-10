using FHT.Access.Application.Dtos;

namespace FHT.Access.Application.Abstractions;

/// <summary>
/// Aplica configuração remota da Gestão em runtime e persiste appsettings.json.
/// Implementação na camada App (acesso a AppSettings + store).
/// </summary>
public interface IRemoteConfigApplier
{
    /// <summary>Última versão confirmada localmente (ack enviado ou carregada do disco).</summary>
    int LastAckedConfigVersion { get; }

    /// <summary>
    /// Se <paramref name="config"/>.ConfigVersion &gt; ack local, aplica allowlist, grava disco
    /// e atualiza runtime. Retorna snapshot reportável (sem secrets).
    /// </summary>
    RemoteConfigApplyResult ApplyIfNewer(DeviceRemoteConfigDto? config);
}

public sealed record RemoteConfigApplyResult(
    bool Applied,
    int ConfigVersion,
    IReadOnlyDictionary<string, object?> ReportedSettings);
