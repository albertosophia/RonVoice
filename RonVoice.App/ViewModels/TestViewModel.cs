using System.Collections.ObjectModel;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;

namespace RonVoice.App.ViewModels;

/// <summary>
/// O que aconteceu com uma fala. Cinco estados, não dois: "entendi mas o mod
/// não tem essa ordem" e "ficou entre duas ordens" seriam mentira pintados de
/// vermelho — nos dois ele ENTENDEU, e mandar a pessoa falar mais claro seria
/// mandá-la caçar um problema que não existe.
/// </summary>
public enum TestOutcome
{
    /// <summary>Casou e resolveu. Verde.</summary>
    Ok,
    /// <summary>Casou, mas o mod RoNSpeech não tem essa ordem. Cinza.</summary>
    NotInMod,
    /// <summary>Ficou entre duas ordens e a margem recusou. Âmbar.</summary>
    Ambiguous,
    /// <summary>Ouviu palavras, mas não é comando. Vermelho.</summary>
    NotACommand,
    /// <summary>Veio fora do vocabulário: ruído, ou palavra que ele não conhece.</summary>
    NotUnderstood,
}

/// <param name="Heard">O texto exato que o reconhecedor devolveu.</param>
/// <param name="Title">A ordem, pelo nome legível, quando houve uma.</param>
/// <param name="Keys">A tecla que sairia. É o que se depura quando nada acontece.</param>
public sealed record TestEntry(
    string Time, string Heard, TestOutcome Outcome, string Title, string Keys)
{
    public bool HasTitle => Title.Length > 0;
    public bool HasKeys => Keys.Length > 0;

    public bool IsOk => Outcome == TestOutcome.Ok;
    public bool IsNotInMod => Outcome == TestOutcome.NotInMod;
    public bool IsAmbiguous => Outcome == TestOutcome.Ambiguous;
    public bool IsBad => Outcome is TestOutcome.NotACommand or TestOutcome.NotUnderstood;
}

/// <summary>
/// A aba de teste é um fluxo contínuo, não uma gravação com veredito no fim.
/// Você fala, a linha sobe. Sem botão de parar: escutar é o estado normal
/// enquanto a aba está aberta.
/// </summary>
public sealed class TestViewModel : ObservableBase
{
    /// <summary>
    /// É um monitor, não um histórico. Lista sem teto comeria memória numa
    /// sessão longa, e ninguém rola até a fala número trezentos.
    /// </summary>
    public const int MaxEntries = 50;

    readonly CommandMap? _map;
    double _level;
    bool _listening;

    public TestViewModel(CommandMap? map = null) => _map = map;

    public ObservableCollection<TestEntry> Entries { get; } = [];

    public double Level { get => _level; set => Set(ref _level, value); }

    /// <summary>
    /// Ligado enquanto a aba está aberta. Fica na tela porque, sem isso, uma
    /// lista parada é indistinguível de um microfone morto.
    /// </summary>
    public bool Listening
    {
        get => _listening;
        set { if (Set(ref _listening, value)) Raise(nameof(HasNothingYet)); }
    }

    public bool HasNothingYet => Entries.Count == 0;

    public RelayCommand ClearCommand => _clear ??= new RelayCommand(_ =>
    {
        Entries.Clear();
        Raise(nameof(HasNothingYet));
    });

    RelayCommand? _clear;

    public void Add(TestEntry entry)
    {
        // Mais recente no topo: é para onde o olho vai depois de falar.
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
        Raise(nameof(HasNothingYet));
    }

    /// <summary>Casou e resolveu — a tecla é o que sairia se não fosse teste.</summary>
    public void Matched(string heard, Intent intent, string keys) =>
        Add(new TestEntry(Now(), heard, TestOutcome.Ok, Describe(intent), keys));

    public void Rejected(Rejection rejection)
    {
        var (outcome, title) = rejection.Reason switch
        {
            // A mensagem do resolvedor já diz "o mod RoNSpeech não tem
            // equivalente para X". Só essa recusa é ausência; as outras são
            // teclas que não sabemos mandar, e aí é falha mesmo.
            RejectionReason.Unresolvable when rejection.Detail?.Contains("RoNSpeech") == true
                => (TestOutcome.NotInMod, Name(OrderIdIn(rejection.Detail))),

            RejectionReason.Unresolvable
                => (TestOutcome.NotACommand, rejection.Detail ?? "não consegui resolver a tecla"),

            RejectionReason.Ambiguous
                => (TestOutcome.Ambiguous,
                    rejection.Detail is { Length: > 0 } closest
                        ? $"ficou entre esta e outra: {Name(closest)}"
                        : "ficou entre duas ordens"),

            RejectionReason.LowConfidence
                => (TestOutcome.NotUnderstood, "entendi com pouca certeza"),

            RejectionReason.Unknown
                => (TestOutcome.NotUnderstood, ""),

            _ => (TestOutcome.NotACommand, ""),
        };

        Add(new TestEntry(Now(), rejection.Text, outcome, title, ""));
    }

    /// <summary>
    /// Nome legível da ordem. Cair no id é melhor que ficar em branco: mesmo
    /// cru, ele diz do que se trata.
    /// </summary>
    string Name(string? orderId) =>
        orderId is { Length: > 0 } && _map?.Orders.GetValueOrDefault(orderId) is { } order
            ? order.Title
            : orderId ?? "";

    string Describe(Intent intent)
    {
        var name = Name(intent.OrderId);
        if (intent.OrderId is null && intent.Element is { } only)
            return $"time {only} selecionado";

        var parts = new List<string> { name };
        if (intent.Element is { } element) parts.Add($"time {element}");
        if (intent.Queue) parts.Add("enfileirada");
        return string.Join("  ·  ", parts);
    }

    /// <summary>A mensagem do resolvedor traz o id no meio da frase.</summary>
    static string? OrderIdIn(string detail) =>
        detail.Split(' ').FirstOrDefault(w => w.Contains('.') && !w.EndsWith('.'));

    static string Now() => DateTime.Now.ToString("HH:mm:ss");
}
