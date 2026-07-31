using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

/// <summary>
/// A aba de teste virou um fluxo contínuo: você fala, a linha sobe. O que
/// importa aqui é a CLASSIFICAÇÃO — dois vermelhos genéricos mandariam a pessoa
/// caçar problema de pronúncia em casos onde ele entendeu perfeitamente.
/// </summary>
public class TestViewModelTests
{
    static TestViewModel Vm() => new(CommandMap.Load(CommandMapTests.MapPath));

    [Fact]
    public void ItStartsEmptyAndSaysSo()
    {
        var vm = Vm();

        Assert.Empty(vm.Entries);
        Assert.True(vm.HasNothingYet);
    }

    [Fact]
    public void TheNewestLineIsOnTop()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(RejectionReason.NoMatch, "primeira"));
        vm.Rejected(new Rejection(RejectionReason.NoMatch, "segunda"));

        Assert.Equal("segunda", vm.Entries[0].Heard);
        Assert.Equal("primeira", vm.Entries[1].Heard);
    }

    /// <summary>
    /// É um monitor, não um histórico: lista sem teto comeria memória numa
    /// sessão longa, e ninguém rola até a fala número trezentos.
    /// </summary>
    [Fact]
    public void ItKeepsOnlyTheLastFifty()
    {
        var vm = Vm();
        for (var i = 0; i < TestViewModel.MaxEntries + 20; i++)
            vm.Rejected(new Rejection(RejectionReason.NoMatch, $"fala {i}"));

        Assert.Equal(TestViewModel.MaxEntries, vm.Entries.Count);
        Assert.Equal($"fala {TestViewModel.MaxEntries + 19}", vm.Entries[0].Heard);
    }

    [Fact]
    public void ClearingEmptiesTheList()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(RejectionReason.NoMatch, "qualquer coisa"));

        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.Entries);
        Assert.True(vm.HasNothingYet);
    }

    // ---- os cinco estados ----

    [Fact]
    public void AMatchedOrderIsGreenAndShowsTheNameAndTheKey()
    {
        var vm = Vm();
        vm.Matched("open with flashbang",
                   new Intent(null, "door.open.flashbang", false), "F15");

        var row = vm.Entries[0];
        Assert.True(row.IsOk);
        Assert.Equal("Abrir a porta com flash", row.Title);
        Assert.Equal("F15", row.Keys);
        Assert.Equal("open with flashbang", row.Heard);
    }

    [Fact]
    public void TheElementAndTheQueueAreSaidToo()
    {
        var vm = Vm();
        vm.Matched("red team open with flashbang",
                   new Intent("red", "door.open.flashbang", true), "F7 + F15");

        Assert.Contains("red", vm.Entries[0].Title);
        Assert.Contains("enfileirada", vm.Entries[0].Title);
    }

    [Fact]
    public void SelectingOnlyATeamIsStillASuccess()
    {
        var vm = Vm();
        vm.Matched("red team", new Intent("red", null, false), "F7");

        Assert.True(vm.Entries[0].IsOk);
        Assert.Contains("red", vm.Entries[0].Title);
    }

    /// <summary>
    /// O caso que não pode ser vermelho: ele ENTENDEU, e o mod é que não tem a
    /// ordem. Vermelho mandaria a pessoa caçar pronúncia sem problema nenhum.
    /// </summary>
    [Fact]
    public void AnOrderTheModLacksIsItsOwnStateNotAFailure()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(
            RejectionReason.Unresolvable, "ram and clear",
            "o mod RoNSpeech não tem equivalente para door.breach.ram.clear — "
            + "essa ordem só funciona pelo menu, na tela"));

        var row = vm.Entries[0];
        Assert.True(row.IsNotInMod);
        Assert.False(row.IsBad);
        Assert.Equal("Aríete na porta e limpar", row.Title);
    }

    /// <summary>
    /// Também entendeu: a margem recusou de propósito. O que resolve é dizer a
    /// frase de outro jeito, não falar mais claro.
    /// </summary>
    [Fact]
    public void AnAmbiguousMatchSaysWhatItWasCloseTo()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(RejectionReason.Ambiguous, "empilha", "door.stack.auto"));

        var row = vm.Entries[0];
        Assert.True(row.IsAmbiguous);
        Assert.False(row.IsBad);
        Assert.Contains("Empilhar na porta", row.Title);
    }

    [Fact]
    public void WordsThatAreNotACommandAreRed()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(RejectionReason.NoMatch, "quero um café"));

        Assert.True(vm.Entries[0].IsBad);
        Assert.Equal("quero um café", vm.Entries[0].Heard);
    }

    [Fact]
    public void SomethingOutOfTheVocabularyIsRedToo()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(RejectionReason.Unknown, "aaaah eee"));

        Assert.True(vm.Entries[0].IsBad);
    }

    [Fact]
    public void LowConfidenceSaysThatIsWhatHappened()
    {
        var vm = Vm();
        vm.Rejected(new Rejection(RejectionReason.LowConfidence, "stack up"));

        Assert.True(vm.Entries[0].IsBad);
        Assert.Contains("certeza", vm.Entries[0].Title);
    }

    /// <summary>
    /// Sem o mapa — o caminho dos testes e de qualquer uso sem catálogo — o id
    /// cru é melhor que branco: mesmo assim diz do que se trata.
    /// </summary>
    [Fact]
    public void WithoutTheMapItFallsBackToTheId()
    {
        var vm = new TestViewModel();
        vm.Matched("stack up", new Intent(null, "door.stack.auto", false), "Eight");

        Assert.Contains("door.stack.auto", vm.Entries[0].Title);
    }

    [Fact]
    public void ListeningIsVisibleBecauseAStillListLooksLikeADeadMicrophone()
    {
        var vm = Vm();
        Assert.False(vm.Listening);

        vm.Listening = true;
        Assert.True(vm.Listening);
    }
}
