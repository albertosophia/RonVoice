using RonVoice.Core.Matching;

namespace RonVoice.Core.Pipeline;

/// <param name="Sequence">O número do pedido a que este recibo responde.</param>
/// <param name="Status">
/// O que o mod fez. "ok" quando executou; qualquer outra coisa é o motivo de
/// não ter executado, dito por quem sabe — e é isso que impede a ordem
/// silenciosa: sem recibo, "mod não instalado", "mod travado" e "deu certo"
/// são idênticos na tela.
/// </param>
public sealed record Receipt(int Sequence, string Status)
{
    public bool Ok => string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A caixa de correio entre o RonVoice e o mod dentro do jogo.
///
/// Existe porque o teclado acabou: os virtual-keys de função vão até F24 e as
/// doze da faixa alta já estão em uso. Modificadores não servem — Ctrl é
/// agachar, Shift é andar e Alt é inclinar, então cada ordem mexeria no
/// personagem.
///
/// Então a ordem deixa de ser uma tecla e passa a ser o id dela, escrito num
/// arquivo que o mod lê de dentro do processo do jogo. Lá dentro ele chama a
/// função do jogo direto, sem menu e sem tecla — que é também por que este
/// caminho funciona em VR e não precisa de elevação.
///
/// FORMATO — o Lua do outro lado depende disto, então é deliberadamente burro.
/// Uma linha, sem JSON, sem varrer diretório: a leitura acontece vinte vezes
/// por segundo DENTRO do laço do jogo.
///
///     pedido:  17|door.breach.ram.clear|red|1
///              sequência | ordem | elemento ou "-" | fila 0 ou 1
///
///     recibo:  17|ok
///              sequência | "ok" ou o motivo
/// </summary>
public sealed class CommandMailbox
{
    public const string OrderFileName = "order.txt";
    public const string ReceiptFileName = "receipt.txt";

    readonly string _orderPath;
    readonly string _receiptPath;
    int _sequence;

    /// <summary>
    /// Em %LOCALAPPDATA%\RonVoice por padrão, e não ao lado do jogo: os dois
    /// lados precisam achar o mesmo lugar sem configuração, e o RonVoice nem
    /// sempre sabe onde o jogo está instalado.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RonVoice");

    public CommandMailbox(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        _orderPath = Path.Combine(Directory, OrderFileName);
        _receiptPath = Path.Combine(Directory, ReceiptFileName);
    }

    public string Directory { get; }

    /// <summary>O número do último pedido escrito. Zero antes do primeiro.</summary>
    public int LastPosted => _sequence;

    /// <summary>
    /// Escreve o pedido e devolve o número dele.
    ///
    /// O número existe porque isto é um EVENTO, não um estado: falar "empilha"
    /// duas vezes seguidas escreveria o mesmo conteúdo, e o mod não teria como
    /// saber que houve uma segunda ordem.
    /// </summary>
    public int Post(Intent intent)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var sequence = ++_sequence;
        var line = $"{sequence}|{intent.OrderId ?? "-"}|{intent.Element ?? "-"}|"
                   + (intent.Queue ? "1" : "0");

        // Grava por temporário e renomeia. Sem isso o mod eventualmente lê meia
        // linha, e uma ordem cortada é pior que ordem nenhuma.
        var temp = _orderPath + ".tmp";
        File.WriteAllText(temp, line);
        File.Move(temp, _orderPath, overwrite: true);

        return sequence;
    }

    /// <summary>
    /// O último recibo, ou null quando não há nenhum ou está ilegível.
    ///
    /// Recibo corrompido é tratado como ausente de propósito: quem chama vai
    /// dizer "o mod não respondeu", que é verdade e é acionável. Inventar uma
    /// leitura otimista aqui traria de volta exatamente o silêncio que esta
    /// classe existe para acabar.
    /// </summary>
    public Receipt? ReadReceipt()
    {
        try
        {
            if (!File.Exists(_receiptPath)) return null;

            var parts = File.ReadAllText(_receiptPath).Trim().Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var sequence)) return null;

            var status = parts[1].Trim();
            return status.Length == 0 ? null : new Receipt(sequence, status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// O mod já respondeu ao último pedido? Falso enquanto ele não responder —
    /// é o que a barra usa para dizer que o mod não está respondendo.
    /// </summary>
    public bool LastWasAcknowledged() => ReadReceipt() is { } r && r.Sequence >= _sequence;

    /// <summary>
    /// Apaga o que ficou de execuções anteriores. Chamado ao abrir: um recibo
    /// velho com número alto faria o primeiro pedido parecer respondido antes
    /// mesmo de o mod acordar.
    /// </summary>
    public void Reset()
    {
        _sequence = 0;
        foreach (var path in new[] { _orderPath, _receiptPath })
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
