using RonVoice.Core.Matching;

namespace RonVoice.Core.Pipeline;

/// <param name="Ok">Se o jogo executou a ordem.</param>
/// <param name="Problem">
/// O que houve, quando não deu. Vem do mod quando ele recusou — "sem porta
/// mirada" — e daqui quando ninguém respondeu.
/// </param>
public sealed record Delivery(bool Ok, string? Problem = null);

/// <summary>
/// Entrega a ordem pelo arquivo que o mod lê, e espera a resposta.
///
/// Mandar por tecla é torcer: o SendInput entrega ao Windows e nunca conta se o
/// jogo agiu. Por aqui dá para saber, e é por isso que o caminho existe além do
/// limite do F24 — o mod responde, então "não funcionou" vira uma frase em vez
/// de um silêncio.
/// </summary>
public sealed class MailboxDelivery
{
    readonly CommandMailbox _mailbox;

    public MailboxDelivery(CommandMailbox mailbox) => _mailbox = mailbox;

    /// <summary>
    /// Quanto se espera pelo mod. O gancho dele roda junto com a câmera e lê no
    /// máximo a cada 50 ms; meio segundo dá folga de sobra sem travar a fala
    /// seguinte quando o jogo está fechado.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>Trocável no teste, para o mod de mentira responder na hora.</summary>
    public Action<TimeSpan> Sleep { get; init; } = Thread.Sleep;

    public Delivery Deliver(Intent intent)
    {
        var sequence = _mailbox.Post(intent);

        var fim = Poll * (int)Math.Max(1, Timeout / Poll);
        for (var esperado = TimeSpan.Zero; esperado <= fim; esperado += Poll)
        {
            // Só vale o recibo DESTE pedido: o anterior herdaria o "ok" dele e
            // traria de volta o silêncio, disfarçado de sucesso.
            if (_mailbox.ReadReceipt() is { } recibo && recibo.Sequence == sequence)
                return recibo.Ok ? new Delivery(true) : new Delivery(false, recibo.Status);

            Sleep(Poll);
        }

        return new Delivery(false,
            "o mod não respondeu — o jogo está aberto e o RonVoiceMod ligado?");
    }
}
