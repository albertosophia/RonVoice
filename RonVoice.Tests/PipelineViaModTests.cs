using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// O caminho de verdade: a frase vira um pedido no arquivo que o mod le', e nao
/// uma tecla. E' o que passa do teto do F24 e o que faz o VR obedecer.
///
/// A diferenca que importa nao e' so' chegar mais longe: por tecla nunca se
/// soube se o jogo agiu. Aqui se sabe, entao "nao funcionou" tem que sair da
/// tela como frase — e' esse fio que estes testes seguram.
/// </summary>
public class PipelineViaModTests : IDisposable
{
    readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ronvoice-pipe-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* nada a fazer */ }
    }

    string OrderFile => Path.Combine(_dir, CommandMailbox.OrderFileName);
    string ReceiptFile => Path.Combine(_dir, CommandMailbox.ReceiptFileName);

    /// <summary>O mod de mentira: responde o que mandarem, ou fica calado.</summary>
    (VoicePipeline Pipeline, FakeSpeechEngine Engine, RecordingSender Sender, List<object> Events)
        Build(string? resposta = "ok", bool dryRun = false)
    {
        var engine = new FakeSpeechEngine();
        var sender = new RecordingSender();
        var map = CommandMap.Load(CommandMapTests.MapPath);
        var caixa = new CommandMailbox(_dir);

        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => true, () => false),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, KeybindReader.Read(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini")))
            {
                Mode = SendMode.Mailbox,
            },
            sender,
            delivery: new MailboxDelivery(caixa)
            {
                Timeout = TimeSpan.FromMilliseconds(100),
                Poll = TimeSpan.FromMilliseconds(10),
                Sleep = _ =>
                {
                    if (resposta is not null)
                        File.WriteAllText(ReceiptFile, $"{caixa.LastPosted}|{resposta}");
                },
            })
        {
            DryRun = dryRun,
        };

        var events = new List<object>();
        pipeline.Heard += r => events.Add(r);
        pipeline.Matched += i => events.Add(i);
        pipeline.Rejected += r => events.Add(r);
        pipeline.Sent += s => events.Add(s);
        pipeline.Start();
        return (pipeline, engine, sender, events);
    }

    [Fact]
    public void ThePhraseBecomesARequestInTheFileTheModReads()
    {
        var (_, engine, _, _) = Build();

        engine.Emit("open and clear");

        Assert.Contains("door.open.clear", File.ReadAllText(OrderFile));
    }

    /// <summary>
    /// Nenhuma tecla. Se ainda saisse tecla junto, a ordem sairia DUAS vezes —
    /// uma pelo menu, outra pelo mod.
    /// </summary>
    [Fact]
    public void NoKeyIsPressedOnThisPath()
    {
        var (_, engine, sender, _) = Build();

        engine.Emit("open and clear");

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void WhenTheModObeysNothingIsRejected()
    {
        var (_, engine, _, events) = Build();

        engine.Emit("open and clear");

        Assert.DoesNotContain(events, e => e is Rejection);
        Assert.Contains(events, e => e is KeySequence);
    }

    /// <summary>
    /// O mod recusou. A pessoa precisa ver o motivo — "sem porta mirada" manda
    /// mirar a porta; silencio manda achar que o programa quebrou.
    /// </summary>
    [Fact]
    public void WhenTheModRefusesTheReasonReachesTheScreen()
    {
        var (_, engine, _, events) = Build(resposta: "sem porta mirada");

        engine.Emit("open and clear");

        var recusa = Assert.IsType<Rejection>(events.Last());
        Assert.Equal(RejectionReason.Unresolvable, recusa.Reason);
        Assert.Contains("sem porta mirada", recusa.Detail);
    }

    [Fact]
    public void WhenTheModRefusesNothingIsReportedAsSent()
    {
        var (_, engine, _, events) = Build(resposta: "recusado");

        engine.Emit("open and clear");

        Assert.DoesNotContain(events, e => e is KeySequence);
    }

    /// <summary>
    /// Jogo fechado, mod desligado: ninguem responde. E' o caso de todo dia e o
    /// unico em que calar seria mentir.
    /// </summary>
    [Fact]
    public void SilenceFromTheModReachesTheScreenToo()
    {
        var (_, engine, _, events) = Build(resposta: null);

        engine.Emit("open and clear");

        var recusa = Assert.IsType<Rejection>(events.Last());
        Assert.Contains("não respondeu", recusa.Detail);
    }

    /// <summary>
    /// A aba de teste roda no meio da missao. Ela nao pode mandar ordem, e por
    /// aqui "mandar" e' escrever o arquivo — entao o arquivo nao pode existir.
    /// </summary>
    [Fact]
    public void TheTestTabNeverSendsAnOrder()
    {
        var (_, engine, _, events) = Build(dryRun: true);

        engine.Emit("open and clear");

        Assert.False(File.Exists(OrderFile));
        Assert.Contains(events, e => e is Intent);
    }

    /// <summary>
    /// Cinco ordens sao tecla direta no jogo: nao passam pelo menu, entao nao ha'
    /// menu para o mod pular, e elas nem estao na tabela dele. Mandar essas pela
    /// caixa e' pedir uma coisa que o mod nao conhece — a ordem morre no recibo,
    /// e o "execute" e' das mais faladas que existem.
    /// </summary>
    [Theory]
    [InlineData("execute")]
    [InlineData("hands up")]
    public void OrdersThatAreAlreadyAKeyStayOnTheKey(string frase)
    {
        var (_, engine, sender, _) = Build();

        engine.Emit(frase);

        Assert.NotEmpty(sender.Sent);
        Assert.False(File.Exists(OrderFile));
    }

    [Fact]
    public void TheElementTravelsWithTheOrder()
    {
        var (_, engine, _, _) = Build();

        engine.Emit("red open and clear");

        Assert.Contains("|red|", File.ReadAllText(OrderFile));
    }
}
