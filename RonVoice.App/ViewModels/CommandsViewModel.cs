using RonVoice.Core.Commands;
using RonVoice.Core.Config;
using RonVoice.Core.Matching;

namespace RonVoice.App.ViewModels;

/// <summary>O que o catálogo está mostrando.</summary>
public enum Availability
{
    /// <summary>As que o modo de envio atual alcança.</summary>
    Working,
    /// <summary>As que ele não alcança — ausentes, não quebradas.</summary>
    Pending,
    All,
}

/// <summary>
/// A tela inicial. O primeiro problema de quem instala não é depurar
/// reconhecimento: é não saber o que pode falar. São 70 ordens e 770 frases.
/// </summary>
public sealed class CommandsViewModel : ObservableBase
{
    readonly IReadOnlyList<OrderRowViewModel> _all;
    string _search = "";

    readonly CommandMap _map;
    readonly string? _storePath;
    readonly string _language;
    readonly Dictionary<string, List<string>> _store;

    /// <param name="storePath">
    /// Caminho do minhas_frases.json. Quando nulo, a edição fica desligada —
    /// é o modo dos testes e de qualquer uso sem pasta gravável.
    /// </param>
    public CommandsViewModel(
        CommandMap map,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? custom = null,
        IReadOnlyList<PhraseIssue>? issues = null,
        string? storePath = null,
        string language = "en",
        bool sendingViaMod = false)
    {
        SendingViaMod = sendingViaMod;
        _map = map;
        _storePath = storePath;
        _language = language;
        _store = storePath is null ? [] : CustomPhraseStore.Read(storePath);
        Issues = issues ?? [];

        _all = map.Orders.Values
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => Build(o, custom))
            .ToList();
        Groups = Group(Filter(""));
        SendCommand = new RelayCommand(_ => { }, _ => false);
        ReloadCommand = new RelayCommand(_ => { }, _ => false);
    }

    /// <summary>A edição só existe quando há onde gravar.</summary>
    public bool CanEdit => _storePath is not null;

    /// <summary>
    /// As ordens estão saindo pelo mod UE4SS em vez do menu. Muda o catálogo:
    /// as que o mod não cobre precisam aparecer marcadas, senão falar uma delas
    /// não faz nada e parece bug.
    /// </summary>
    public bool SendingViaMod { get; }

    public int UnavailableCount => _all.Count(o => o.UnavailableInCurrentMode);

    public bool HasUnavailable => UnavailableCount > 0;

    public string UnavailableText =>
        $"{UnavailableCount} ordens ainda não têm tecla no mod RoNSpeech. Não estão "
        + "quebradas — o mod não as implementa.";

    public int WorkingCount => _all.Count - UnavailableCount;
    public int TotalCount => _all.Count;

    /// <summary>
    /// Abre em Funcionam de propósito. Com o mod obrigatório, 38 das 70 ordens
    /// não têm equivalente, e mostrar as 70 de cara deixaria mais da metade da
    /// tela inicial marcada — sem que nada ali esteja quebrado. A lacuna fica
    /// contada e a um clique, em vez de gritando.
    /// </summary>
    public Availability Shown
    {
        get => _shown;
        set
        {
            if (!Set(ref _shown, value)) return;
            Regroup();
        }
    }

    Availability _shown = Availability.Working;

    public bool ShowingWorking => Shown == Availability.Working;
    public bool ShowingPending => Shown == Availability.Pending;
    public bool ShowingAll => Shown == Availability.All;

    /// <summary>Só faz sentido quando há ordens fora do alcance do modo atual.</summary>
    public bool CanFilterByAvailability => HasUnavailable;

    public RelayCommand ShowCommand => _showCommand ??= new RelayCommand(p =>
        Shown = Enum.Parse<Availability>((string)p!, ignoreCase: true));

    RelayCommand? _showCommand;

    void Regroup()
    {
        Groups = Group(Filter(_search));
        Raise(nameof(Groups));
        Raise(nameof(TotalShown));
        Raise(nameof(CountText));
        Raise(nameof(ShowingWorking));
        Raise(nameof(ShowingPending));
        Raise(nameof(ShowingAll));
    }

    OrderRowViewModel Build(
        OrderDefinition order, IReadOnlyDictionary<string, IReadOnlyList<string>>? custom)
    {
        var row = new OrderRowViewModel(
            order,
            custom is not null && custom.TryGetValue(order.Id, out var c) ? c : null,
            SendingViaMod);

        row.AddCommand = new RelayCommand(_ => AddPhrase(row), _ => CanEdit);
        row.RemoveCommand = new RelayCommand(
            p => RemovePhrase(row, (string)p!), _ => CanEdit);
        return row;
    }

    /// <summary>
    /// Valida antes de gravar. Descobrir a colisão depois significaria duas
    /// ordens mudas sem erro nenhum — foi o que já aconteceu neste projeto.
    /// </summary>
    void AddPhrase(OrderRowViewModel row)
    {
        if (_storePath is null) return;

        var phrase = row.Draft.Trim();
        var rejection = CustomPhraseStore.Reject(_map, row.Id, phrase, _language, _store);
        if (rejection is not null) { row.DraftError = rejection; return; }

        CustomPhraseStore.Add(_storePath, row.Id, phrase, _store);
        row.CustomPhrases.Add(phrase);
        row.Draft = "";
        row.DraftError = null;
        PendingRestart = true;
    }

    void RemovePhrase(OrderRowViewModel row, string phrase)
    {
        if (_storePath is null) return;

        CustomPhraseStore.Remove(_storePath, row.Id, phrase, _store);
        row.CustomPhrases.Remove(phrase);
        PendingRestart = true;
    }

    bool _pendingRestart;

    /// <summary>
    /// Fica verdadeiro depois da primeira edição. A gramática do reconhecedor é
    /// montada na abertura e é imutável na vida do VoskRecognizer, então a
    /// frase nova aparece no catálogo na hora mas só é ouvida ao reabrir.
    /// </summary>
    public bool PendingRestart
    {
        get => _pendingRestart;
        private set => Set(ref _pendingRestart, value);
    }

    /// <summary>
    /// O que foi recusado do minhas_frases.json. Aparece na tela, não num log:
    /// quem escreveu o arquivo precisa ver que uma linha dele não entrou.
    /// </summary>
    public IReadOnlyList<PhraseIssue> Issues { get; }

    public bool HasIssues => Issues.Count > 0;

    public string IssuesText => string.Join('\n', Issues.Select(i => $"· {i.Message}"));

    /// <summary>Relê o minhas_frases.json sem fechar o app. Ligado na integração.</summary>
    public RelayCommand ReloadCommand { get; set; }

    /// <summary>
    /// "Enviar ao jogo" de cada linha. Nasce desabilitado e é substituído na
    /// integração, que é quem sabe minimizar a janela e devolver o foco ao jogo —
    /// sem isso o ForegroundGuard recusaria, porque quem está em foco é o app.
    /// </summary>
    public RelayCommand SendCommand { get; set; }

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) Regroup(); }
    }

    public IReadOnlyList<CommandGroupViewModel> Groups { get; private set; }

    public int TotalShown => Groups.Sum(g => g.Count);

    /// <summary>
    /// Conta contra o universo do filtro, não contra as 70: em "Ainda não",
    /// dizer "3 de 70" faria pensar que 67 estão escondidas por busca.
    /// </summary>
    public string CountText
    {
        get
        {
            var universe = Shown switch
            {
                Availability.Working => WorkingCount,
                Availability.Pending => UnavailableCount,
                _ => _all.Count,
            };
            return TotalShown == universe
                ? $"{TotalShown} ordens"
                : $"{TotalShown} de {universe} ordens";
        }
    }

    /// <summary>
    /// Cada palavra digitada precisa aparecer em algum lugar da ordem, em
    /// qualquer posição e em qualquer campo.
    ///
    /// Antes era substring contígua sobre um campo só de cada vez, e isso
    /// falhava no jeito que as pessoas realmente buscam: "flash porta" não
    /// achava "abre a porta com flash" porque as palavras estão fora de ordem, e
    /// "porta flash" não achava nada porque nenhum campo isolado tem as duas.
    ///
    /// A dobra de formas verbais entra junto, então "abra" acha "abre".
    /// </summary>
    IReadOnlyList<OrderRowViewModel> Filter(string search)
    {
        var pool = Shown switch
        {
            Availability.Working => _all.Where(o => !o.UnavailableInCurrentMode),
            Availability.Pending => _all.Where(o => o.UnavailableInCurrentMode),
            _ => _all,
        };

        var terms = VerbForms.Fold(TextNormalizer.Tokenize(search), _language);
        if (terms.Count > 0) pool = pool.Where(o => Matches(o, terms));

        return pool.ToList();
    }

    bool Matches(OrderRowViewModel row, IReadOnlyList<string> terms)
    {
        // Um saco de palavras por ordem, montado uma vez por linha.
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in row.SearchableText())
            foreach (var token in VerbForms.Fold(TextNormalizer.Tokenize(text), _language))
                words.Add(token);

        // Prefixo, não igualdade: quem digita "empil" ainda está no meio da
        // palavra e já quer ver o resultado.
        return terms.All(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal)));
    }

    static IReadOnlyList<CommandGroupViewModel> Group(IReadOnlyList<OrderRowViewModel> rows) =>
        rows.GroupBy(o => o.Context)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CommandGroupViewModel(g.Key, g.ToList()))
            .ToList();
}
