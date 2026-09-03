namespace FHT.Access.Domain.Enums;

public enum UpdateUiState
{
    /// <summary>Nenhuma atualização pendente.</summary>
    None,

    /// <summary>Versão nova disponível, aguardando janela para instalar.</summary>
    Available,

    /// <summary>Countdown ativo — kiosk travado, instalação em breve.</summary>
    Countdown,

    /// <summary>Baixando o pacote.</summary>
    Downloading,

    /// <summary>Aplicando e reiniciando.</summary>
    Applying,
}
