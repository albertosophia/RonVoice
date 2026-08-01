using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

/// <summary>
/// A caixa de correio entre o RonVoice e o mod dentro do jogo.
///
/// O formato e' um CONTRATO entre duas linguagens: o Lua do outro lado nao
/// compila junto e nao quebra quando eu mudo isto. Estes testes sao o unico
/// lugar onde as duas pontas se encontram, entao eles prendem o formato letra
/// por letra.
/// </summary>
public class CommandMailboxTests : IDisposable
{
    readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"ronvoice-mailbox-{Guid.NewGuid():N}");

    CommandMailbox Box() => new(_dir);

    string OrderText() => File.ReadAllText(Path.Combine(_dir, CommandMailbox.OrderFileName));

    /// <summary>
    /// Faz o papel do mod. Cria a pasta porque no jogo real o mod pode escrever
    /// o recibo antes de o RonVoice ter postado qualquer coisa.
    /// </summary>
    void WriteReceipt(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, CommandMailbox.ReceiptFileName), text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ---- o formato, que o Lua precisa entender ----

    [Fact]
    public void APlainOrderIsOneLineWithFourFields()
    {
        Box().Post(new Intent(null, "door.stack.auto", false));

        Assert.Equal("1|door.stack.auto|-|0", OrderText());
    }

    [Fact]
    public void TheElementAndTheQueueTravelInTheSameLine()
    {
        Box().Post(new Intent("red", "door.open.flashbang", true));

        Assert.Equal("1|door.open.flashbang|red|1", OrderText());
    }

    /// <summary>Selecionar time e' uma ordem sem ordem: o id vira "-".</summary>
    [Fact]
    public void SelectingOnlyATeamHasNoOrderId()
    {
        Box().Post(new Intent("blue", null, false));

        Assert.Equal("1|-|blue|0", OrderText());
    }

    [Fact]
    public void ThereIsNoTrailingNewlineToConfuseTheParserOnTheOtherSide() =>
        Assert.DoesNotContain('\n', OrderText4());

    string OrderText4()
    {
        Box().Post(new Intent(null, "hold", false));
        return OrderText();
    }

    // ---- a sequencia ----

    /// <summary>
    /// E' um EVENTO, nao um estado. Falar a mesma coisa duas vezes tem que
    /// produzir dois pedidos, senao o mod nao teria como saber que houve um
    /// segundo — o conteudo seria identico.
    /// </summary>
    [Fact]
    public void TheSameOrderTwiceStillCountsAsTwo()
    {
        var box = Box();
        var first = box.Post(new Intent(null, "door.stack.auto", false));
        var second = box.Post(new Intent(null, "door.stack.auto", false));

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.StartsWith("2|", OrderText());
    }

    [Fact]
    public void PostingRemembersTheLastNumber()
    {
        var box = Box();
        Assert.Equal(0, box.LastPosted);

        box.Post(new Intent(null, "hold", false));
        Assert.Equal(1, box.LastPosted);
    }

    // ---- escrita atomica ----

    /// <summary>
    /// O mod le' vinte vezes por segundo. Sem gravar por temporario, ele
    /// eventualmente pega meia linha — e uma ordem cortada e' pior que
    /// nenhuma. O temporario nao pode sobrar.
    /// </summary>
    [Fact]
    public void NoTemporaryFileIsLeftBehind()
    {
        Box().Post(new Intent(null, "hold", false));

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void TheDirectoryIsCreatedOnDemand()
    {
        Assert.False(Directory.Exists(_dir));

        Box().Post(new Intent(null, "hold", false));

        Assert.True(Directory.Exists(_dir));
    }

    // ---- recibos ----

    [Fact]
    public void NoReceiptYetReadsAsNothing() => Assert.Null(Box().ReadReceipt());

    [Fact]
    public void AReceiptIsReadBack()
    {
        WriteReceipt("7|ok");

        var receipt = Box().ReadReceipt();

        Assert.Equal(7, receipt!.Sequence);
        Assert.True(receipt.Ok);
    }

    /// <summary>
    /// O recibo carrega o MOTIVO quando o mod nao executou. E' o que separa
    /// "o mod nao tem essa ordem" de "o mod nao esta rodando" — que sem isso
    /// sao o mesmo silencio.
    /// </summary>
    [Fact]
    public void AReceiptCanSayWhyItDidNotRun()
    {
        WriteReceipt("7|unsupported");

        var receipt = Box().ReadReceipt();

        Assert.False(receipt!.Ok);
        Assert.Equal("unsupported", receipt.Status);
    }

    [Fact]
    public void ATrailingNewlineFromLuaIsTolerated()
    {
        WriteReceipt("7|ok\n");

        Assert.True(Box().ReadReceipt()!.Ok);
    }

    /// <summary>
    /// Recibo ilegivel conta como ausente. Inventar uma leitura otimista aqui
    /// traria de volta o silencio que esta classe existe para acabar.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("|ok")]
    [InlineData("7|")]
    [InlineData("sete|ok")]
    public void AGarbledReceiptCountsAsNoReceipt(string text)
    {
        WriteReceipt(text);

        Assert.Null(Box().ReadReceipt());
    }

    // ---- "o mod respondeu?" ----

    [Fact]
    public void WithoutAReceiptTheLastOrderIsNotAcknowledged()
    {
        var box = Box();
        box.Post(new Intent(null, "hold", false));

        Assert.False(box.LastWasAcknowledged());
    }

    [Fact]
    public void TheModAnsweringTheLastOrderCountsAsAcknowledged()
    {
        var box = Box();
        var sequence = box.Post(new Intent(null, "hold", false));
        WriteReceipt($"{sequence}|ok");

        Assert.True(box.LastWasAcknowledged());
    }

    /// <summary>
    /// Um recibo de uma ordem ANTERIOR nao vale pela atual: e' assim que um mod
    /// que travou no meio pareceria vivo.
    /// </summary>
    [Fact]
    public void AnOlderReceiptDoesNotCoverANewerOrder()
    {
        var box = Box();
        box.Post(new Intent(null, "hold", false));
        WriteReceipt("1|ok");
        box.Post(new Intent(null, "cover", false));

        Assert.False(box.LastWasAcknowledged());
    }

    /// <summary>
    /// Recusar tambem e' responder. O mod respondeu, entao ele esta vivo — o
    /// que a barra precisa dizer e' outra coisa, nao "mod nao responde".
    /// </summary>
    [Fact]
    public void ARefusalStillCountsAsTheModBeingAlive()
    {
        var box = Box();
        var sequence = box.Post(new Intent(null, "door.breach.ram.clear", false));
        WriteReceipt($"{sequence}|unsupported");

        Assert.True(box.LastWasAcknowledged());
        Assert.False(box.ReadReceipt()!.Ok);
    }

    // ---- abrir de novo ----

    /// <summary>
    /// Um recibo velho com numero alto faria o primeiro pedido da sessao nova
    /// parecer respondido antes de o mod sequer acordar.
    /// </summary>
    [Fact]
    public void ResetClearsWhatTheLastSessionLeft()
    {
        var box = Box();
        box.Post(new Intent(null, "hold", false));
        WriteReceipt("99|ok");

        box.Reset();

        Assert.Equal(0, box.LastPosted);
        Assert.Null(box.ReadReceipt());
        Assert.False(box.LastWasAcknowledged());
    }

    [Fact]
    public void ResetOnAnEmptyDirectoryIsHarmless()
    {
        Box().Reset();   // nao pode lancar
        Assert.False(Directory.Exists(_dir));
    }

    /// <summary>
    /// Os dois lados precisam achar o mesmo lugar sem configuracao: o RonVoice
    /// nem sempre sabe onde o jogo esta instalado, e o Lua monta este mesmo
    /// caminho com os.getenv("LOCALAPPDATA").
    /// </summary>
    [Fact]
    public void TheDefaultPlaceIsUnderLocalAppData()
    {
        Assert.EndsWith(Path.Combine("RonVoice"), CommandMailbox.DefaultDirectory);
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CommandMailbox.DefaultDirectory);
    }
}
