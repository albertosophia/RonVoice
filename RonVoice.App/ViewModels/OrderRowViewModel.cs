using System.Collections.ObjectModel;
using RonVoice.Core.Commands;

namespace RonVoice.App.ViewModels;

/// <summary>Uma ordem no catálogo, já no formato que a tela mostra.</summary>
public sealed class OrderRowViewModel : ObservableBase
{
    readonly OrderDefinition _order;
    string _draft = "";
    string? _draftError;

    readonly bool _sendingViaMod;

    public OrderRowViewModel(
        OrderDefinition order,
        IEnumerable<string>? customPhrases = null,
        bool sendingViaMod = false)
    {
        _order = order;
        _sendingViaMod = sendingViaMod;
        CustomPhrases = new ObservableCollection<string>(customPhrases ?? []);
        CustomPhrases.CollectionChanged += (_, _) =>
        {
            Raise(nameof(HasCustomPhrases));
            Raise(nameof(CustomPhrasesText));
        };
    }

    public string Id => _order.Id;
    public string Context => _order.Context;

    /// <summary>
    /// As 25 ordens marcadas `confidence: "verify"` nunca foram confirmadas em
    /// jogo. Sem o aviso, quem usar vai concluir que estão quebradas.
    /// </summary>
    public bool NeedsVerification =>
        string.Equals(_order.Confidence, "verify", StringComparison.OrdinalIgnoreCase);

    /// <summary>O mod UE4SS tem tecla para esta ordem.</summary>
    public bool SupportsRonSpeech => _order.RonSpeechKeys is { Count: > 0 };

    /// <summary>
    /// Está ligado o modo do mod E esta ordem não existe nele — ou seja, falar
    /// esta frase agora não faz nada. Um bool só, porque é o que a tela precisa
    /// perguntar, e sem ele o silêncio pareceria bug.
    /// </summary>
    public bool UnavailableInCurrentMode => _sendingViaMod && !SupportsRonSpeech;

    public IReadOnlyList<string> PhrasesEn =>
        _order.Phrases.TryGetValue("en", out var p) ? p : [];

    public IReadOnlyList<string> PhrasesPt =>
        _order.Phrases.TryGetValue("pt", out var p) ? p : [];

    public string PathText => string.Join(' ', _order.Path);

    public string PhrasesEnText => string.Join("  ·  ", PhrasesEn);
    public string PhrasesPtText => string.Join("  ·  ", PhrasesPt);

    /// <summary>Frases que o usuário acrescentou. Editáveis pela tela.</summary>
    public ObservableCollection<string> CustomPhrases { get; }

    public bool HasCustomPhrases => CustomPhrases.Count > 0;

    public string CustomPhrasesText => string.Join("  ·  ", CustomPhrases);

    /// <summary>O que está sendo digitado no campo "adicionar frase".</summary>
    public string Draft
    {
        get => _draft;
        set { if (Set(ref _draft, value)) DraftError = null; }
    }

    /// <summary>
    /// Por que a frase digitada não pode entrar. Aparece no campo antes de
    /// gravar: descobrir depois significaria duas ordens mudas sem erro nenhum.
    /// </summary>
    public string? DraftError
    {
        get => _draftError;
        set { if (Set(ref _draftError, value)) Raise(nameof(HasDraftError)); }
    }

    public bool HasDraftError => DraftError is not null;

    /// <summary>Ligados pelo CommandsViewModel, que sabe gravar.</summary>
    public RelayCommand AddCommand { get; set; } = new(_ => { }, _ => false);
    public RelayCommand RemoveCommand { get; set; } = new(_ => { }, _ => false);

    internal IEnumerable<string> SearchableText()
    {
        yield return Id;
        foreach (var p in PhrasesEn) yield return p;
        foreach (var p in PhrasesPt) yield return p;
        foreach (var p in CustomPhrases) yield return p;
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
