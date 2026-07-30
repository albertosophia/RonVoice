namespace RonVoice.App.ViewModels;

/// <summary>
/// Semântica, não estética. Cor semântica é separada do acento do app: âmbar é
/// identidade, isto aqui é estado.
/// </summary>
public enum ChipLevel
{
    /// <summary>Informação. Nem boa nem ruim: o microfone, o modelo, o modo.</summary>
    Neutral,

    /// <summary>Está funcionando agora.</summary>
    Good,

    /// <summary>
    /// Falha de verdade, que impede o app de funcionar. Reservado: ausência de
    /// um recurso NÃO entra aqui, senão a cor perde o significado.
    /// </summary>
    Bad,
}

/// <param name="Label">O que é, em minúsculas — "microfone", "envio".</param>
/// <param name="Value">
/// O valor, quando há um. Renderizado em monoespaçado porque quase sempre é um
/// nome de dispositivo, de tecla ou de idioma, que se compara caractere a
/// caractere quando algo está errado.
/// </param>
public sealed record StatusChip(string Label, string? Value = null,
                               ChipLevel Level = ChipLevel.Neutral);
