using RonVoice.Core.Commands;
using RonVoice.Core.Config;
using RonVoice.Core.Matching;

namespace RonVoice.App.ViewModels;

/// <summary>
/// A tela inicial. O primeiro problema de quem instala não é depurar
/// reconhecimento: é não saber o que pode falar. São 70 ordens e 770 frases.
/// </summary>
public sealed class CommandsViewModel : ObservableBase
{
    readonly IReadOnlyList<OrderRowViewModel> _all;
    string _search = "";

    public CommandsViewModel(
        CommandMap map,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? custom = null,
        IReadOnlyList<PhraseIssue>? issues = null)
    {
        Issues = issues ?? [];

        _all = map.Orders.Values
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => new OrderRowViewModel(
                o, custom is not null && custom.TryGetValue(o.Id, out var c) ? c : null))
            .ToList();
        Groups = Group(_all);
        SendCommand = new RelayCommand(_ => { }, _ => false);
        ReloadCommand = new RelayCommand(_ => { }, _ => false);
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
        set
        {
            if (!Set(ref _search, value)) return;
            Groups = Group(Filter(value));
            Raise(nameof(Groups));
            Raise(nameof(TotalShown));
            Raise(nameof(CountText));
        }
    }

    public IReadOnlyList<CommandGroupViewModel> Groups { get; private set; }

    public int TotalShown => Groups.Sum(g => g.Count);

    public string CountText =>
        TotalShown == _all.Count
            ? $"{TotalShown} ordens"
            : $"{TotalShown} de {_all.Count} ordens";

    IReadOnlyList<OrderRowViewModel> Filter(string search)
    {
        // Mesma normalização do matcher: busca sem acento e sem caixa, para
        // "posição" e "posicao" acharem a mesma coisa.
        var needle = string.Join(' ', TextNormalizer.Tokenize(search));
        if (needle.Length == 0) return _all;

        return _all
            .Where(o => o.SearchableText().Any(t =>
                string.Join(' ', TextNormalizer.Tokenize(t))
                      .Contains(needle, StringComparison.Ordinal)))
            .ToList();
    }

    static IReadOnlyList<CommandGroupViewModel> Group(IReadOnlyList<OrderRowViewModel> rows) =>
        rows.GroupBy(o => o.Context)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CommandGroupViewModel(g.Key, g.ToList()))
            .ToList();
}
