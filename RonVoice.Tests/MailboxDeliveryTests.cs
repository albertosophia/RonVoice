using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

/// <summary>
/// Mandar por tecla e' torcer: o SendInput entrega ao Windows e nunca conta se
/// o jogo agiu. Pela caixa de correio da' para saber — o mod responde. Esta
/// classe e' onde essa resposta vira algo que a tela pode mostrar.
///
/// O silencio e' o caso importante. Mod desligado, jogo fechado, mod travado:
/// nos tres nao vem recibo, e nos tres o certo e' dizer que nao veio, em vez de
/// deixar a pessoa falando com a parede.
/// </summary>
public class MailboxDeliveryTests : IDisposable
{
    readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ronvoice-entrega-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* nada a fazer */ }
    }

    /// <summary>Um mod de mentira: responde depois de tantas esperas.</summary>
    sealed class ModFalso
    {
        readonly string _receipt;
        readonly int _esperasAteResponder;
        readonly string _resposta;
        int _esperas;

        public ModFalso(string dir, string resposta, int esperasAteResponder = 1)
        {
            _receipt = Path.Combine(dir, CommandMailbox.ReceiptFileName);
            _resposta = resposta;
            _esperasAteResponder = esperasAteResponder;
        }

        public int Esperas => _esperas;

        public void Responde(int sequence)
        {
            if (++_esperas == _esperasAteResponder)
                File.WriteAllText(_receipt, $"{sequence}|{_resposta}");
        }
    }

    MailboxDelivery Entrega(ModFalso? mod, out CommandMailbox caixa)
    {
        var c = new CommandMailbox(_dir);
        caixa = c;
        return new MailboxDelivery(c)
        {
            Timeout = TimeSpan.FromMilliseconds(200),
            Poll = TimeSpan.FromMilliseconds(10),
            Sleep = _ => mod?.Responde(c.LastPosted),
        };
    }

    static Intent Ordem(string id = "hold") => new(null, id, false);

    [Fact]
    public void TheOrderReachesTheFileTheModReads()
    {
        var entrega = Entrega(new ModFalso(_dir, "ok"), out var caixa);

        entrega.Deliver(Ordem("door.breach.kick.gas"));

        var linha = File.ReadAllText(Path.Combine(_dir, CommandMailbox.OrderFileName));
        Assert.Contains("door.breach.kick.gas", linha);
        Assert.Equal(1, caixa.LastPosted);
    }

    [Fact]
    public void WhenTheModSaysOkTheOrderWorked()
    {
        var entrega = Entrega(new ModFalso(_dir, "ok"), out _);

        var r = entrega.Deliver(Ordem());

        Assert.True(r.Ok);
        Assert.Null(r.Problem);
    }

    /// <summary>
    /// O mod recusa com o motivo — "sem porta mirada". Isso tem que chegar
    /// inteiro na tela: e' a diferenca entre a pessoa mirar a porta e a pessoa
    /// achar que o programa quebrou.
    /// </summary>
    [Fact]
    public void WhenTheModRefusesTheReasonComesBack()
    {
        var entrega = Entrega(new ModFalso(_dir, "sem porta mirada"), out _);

        var r = entrega.Deliver(Ordem("door.breach.kick.gas"));

        Assert.False(r.Ok);
        Assert.Equal("sem porta mirada", r.Problem);
    }

    /// <summary>
    /// Ninguem respondeu. E' o caso de todo dia — mod desligado, jogo fechado —
    /// e o unico em que calar seria mentir.
    /// </summary>
    [Fact]
    public void SilenceIsReportedAsSilence()
    {
        var entrega = Entrega(mod: null, out _);

        var r = entrega.Deliver(Ordem());

        Assert.False(r.Ok);
        Assert.Contains("não respondeu", r.Problem);
    }

    [Fact]
    public void ItStopsWaitingOnceTheAnswerArrives()
    {
        var mod = new ModFalso(_dir, "ok", esperasAteResponder: 2);
        var entrega = Entrega(mod, out _);

        entrega.Deliver(Ordem());

        // O mod respondeu na segunda espera; a leitura seguinte achou o recibo e
        // saiu sem esperar de novo. O tempo limite daria vinte esperas.
        Assert.Equal(2, mod.Esperas);
    }

    /// <summary>
    /// Recibo do pedido ANTERIOR nao vale para este. Sem isto, uma ordem que o
    /// mod ignorou herdaria o "ok" da anterior — o silencio de volta, disfarcado.
    /// </summary>
    [Fact]
    public void AnOldReceiptDoesNotAnswerANewOrder()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, CommandMailbox.ReceiptFileName), "1|ok");

        var entrega = Entrega(mod: null, out _);
        entrega.Deliver(Ordem());          // vira o pedido 1...
        var r = entrega.Deliver(Ordem());  // ...e este e' o 2, sem recibo

        Assert.False(r.Ok);
    }

    /// <summary>
    /// O elemento dito por voz viaja junto: "vermelho, arromba" nao pode virar
    /// ordem para o esquadrao inteiro.
    /// </summary>
    [Fact]
    public void TheElementTravelsWithTheOrder()
    {
        var entrega = Entrega(new ModFalso(_dir, "ok"), out _);

        entrega.Deliver(new Intent("red", "door.breach.kick.gas", false));

        var linha = File.ReadAllText(Path.Combine(_dir, CommandMailbox.OrderFileName));
        Assert.Contains("|red|", linha);
    }
}
