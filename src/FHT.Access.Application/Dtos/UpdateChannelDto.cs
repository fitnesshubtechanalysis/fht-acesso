namespace FHT.Access.Application.Dtos;

/// <summary>
/// Canal de atualização retornado pelo endpoint da Gestão.
/// </summary>
public sealed class UpdateChannelDto
{
    /// <summary>Versão semântica disponível no canal, ex.: "1.2.3".</summary>
    public string? LatestVersion { get; init; }

    /// <summary>URL de download do pacote Velopack (.zip).</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>Notas resumidas do release.</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>Obrigatória: o totem deve atualizar o mais rápido possível.</summary>
    public bool Mandatory { get; init; }

    /// <summary>Hora a partir da qual pode aplicar (horário local, 0–23). Igual a ApplyBeforeHour = 24h.</summary>
    public int ApplyAfterHour { get; init; } = 0;

    /// <summary>Hora até a qual pode aplicar (horário local, 0–23). Igual a ApplyAfterHour = 24h.</summary>
    public int ApplyBeforeHour { get; init; } = 0;
}
