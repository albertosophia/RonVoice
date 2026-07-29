using RonVoice.Core.Commands;

namespace RonVoice.App.ViewModels;

/// <summary>Uma ordem no catálogo, já no formato que a tela mostra.</summary>
public sealed class OrderRowViewModel(OrderDefinition order)
{
    public string Id => order.Id;
    public string Context => order.Context;

    /// <summary>
    /// As 25 ordens marcadas `confidence: "verify"` nunca foram confirmadas em
    /// jogo. Sem o aviso, quem usar vai concluir que estão quebradas.
    /// </summary>
    public bool NeedsVerification =>
        string.Equals(order.Confidence, "verify", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> PhrasesEn =>
        order.Phrases.TryGetValue("en", out var p) ? p : [];

    public IReadOnlyList<string> PhrasesPt =>
        order.Phrases.TryGetValue("pt", out var p) ? p : [];

    public string PathText => string.Join(' ', order.Path);

    /// <summary>Frases em inglês numa linha só, para caber na lista.</summary>
    public string PhrasesEnText => string.Join("  ·  ", PhrasesEn);

    /// <summary>Frases em português numa linha só, para caber na lista.</summary>
    public string PhrasesPtText => string.Join("  ·  ", PhrasesPt);

    internal IEnumerable<string> SearchableText()
    {
        yield return Id;
        foreach (var p in PhrasesEn) yield return p;
        foreach (var p in PhrasesPt) yield return p;
    }
}

public sealed class CommandGroupViewModel(string context, IReadOnlyList<OrderRowViewModel> orders)
{
    public string Context => context;
    public IReadOnlyList<OrderRowViewModel> Orders => orders;
    public int Count => orders.Count;

    /// <summary>Cabeçalho do grupo, já pronto para a tela.</summary>
    public string Header => Context switch
    {
        "door" => $"Porta — mire numa porta  ({Count})",
        "person" => $"Pessoa — mire num suspeito ou civil  ({Count})",
        "default" => $"Geral  ({Count})",
        "any" => $"Qualquer situação  ({Count})",
        _ => $"{Context}  ({Count})",
    };
}
