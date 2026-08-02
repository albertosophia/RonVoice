using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Commands;

public sealed class ResolveException(string message) : Exception(message);

public enum SendMode
{
    /// <summary>Abre o menu SWAT e navega. Não precisa de mod nenhum.</summary>
    Menu,

    /// <summary>
    /// Uma tecla do mod UE4SS RoNSpeech, que chama as funções do jogo direto.
    /// É o que funciona em VR, onde o menu abre mas não aceita os dígitos.
    ///
    /// Chega a 32 das 70 ordens, e não tem como chegar mais longe: o Windows
    /// para no F24. Fica como caminho de recuo enquanto o <see cref="Mailbox"/>
    /// não estiver conferido em jogo.
    /// </summary>
    RonSpeech,

    /// <summary>
    /// Deixa o pedido num arquivo que o RonVoiceMod lê. Sem tecla nenhuma, e
    /// por isso sem teto: alcança as 65 ordens que não são tecla direta.
    ///
    /// É também o único caminho que responde. SendInput entrega ao Windows e
    /// nunca conta se o jogo agiu; o mod devolve recibo, então "não funcionou"
    /// vira uma frase na tela em vez de silêncio.
    /// </summary>
    Mailbox,
}

/// <summary>
/// Intent + binds do jogo -> KeySequence. Quando a resolução é incerta, lança:
/// nunca inventa uma tecla plausível.
/// </summary>
public sealed class CommandResolver
{
    /// <summary>
    /// O perfil VoiceAttack original segura o clique do meio por 0.1s, contra
    /// 0.033s das teclas. Constante, não vem do JSON.
    /// </summary>
    public const int MouseHoldMs = 100;

    readonly CommandMap _map;
    readonly IReadOnlyDictionary<string, string> _binds;
    readonly KeybindDefaults _defaults;
    readonly bool _holdMenuOpen;

    /// <param name="holdMenuOpen">
    /// Mantém a tecla do menu PRESSIONADA durante a navegação, soltando só no
    /// fim, em vez de clicar e soltar antes dos dígitos.
    ///
    /// Existe para o VR. No desktop clicar e soltar funciona — o menu fica
    /// travado aberto — e está provado por 412 testes e por jogo real. Em VR o
    /// menu abre, o teclado comprovadamente chega (uma ordem de tecla pura
    /// funciona), e ainda assim os dígitos não escolhem nada. A leitura é que
    /// ali o menu radial é segure-e-aponte: soltar o botão o fecha, ou o deixa
    /// num estado em que número não é caminho válido.
    /// </param>
    public CommandResolver(
        CommandMap map,
        IReadOnlyDictionary<string, string> binds,
        KeybindDefaults? defaults = null,
        bool holdMenuOpen = false)
    {
        _map = map;
        _binds = binds;
        _defaults = defaults ?? map.Defaults;
        _holdMenuOpen = holdMenuOpen;
    }

    /// <summary>
    /// "on my command" no mod é o 9 apertado ANTES da tecla do comando. Não é o
    /// mesmo mecanismo do menu, que segura LShift em volta do último passo.
    /// </summary>
    const string RonSpeechQueueKey = "Nine";

    /// <summary>
    /// Resolve pelo mod UE4SS RoNSpeech: uma tecla por ordem, sem abrir menu.
    /// É o caminho que funciona em VR, onde o menu abre mas não aceita dígitos.
    ///
    /// Lança quando o mod não cobre a ordem. Cair no caminho do menu como
    /// reserva seria o pior dos dois mundos: em VR ele não funciona, e o jogador
    /// veria o menu abrir sozinho e nada acontecer — de novo sem erro.
    /// </summary>
    public KeySequence ResolveViaRonSpeech(Intent intent)
    {
        var steps = new List<KeyStep>();
        var hold = _map.Timing.KeyHoldMs;
        var gap = _map.Timing.GapBetweenKeysMs;

        // A seleção de elemento é a MESMA tecla nos dois caminhos: o mod escuta
        // F5/F6/F7 e guarda o time nele, e o jogo também seleciona. Nada muda.
        if (intent.Element is { } element)
            steps.Add(new KeyStep(StepKind.Press, ResolveElement(element), hold, gap));

        if (intent.OrderId is not { } orderId)
        {
            if (steps.Count == 0)
                throw new ResolveException("intent vazio: sem elemento e sem ordem");
            return new KeySequence(steps);
        }

        if (!_map.Orders.TryGetValue(orderId, out var order))
            throw new ResolveException($"ordem desconhecida: {orderId}");

        if (order.RonSpeechKeys is not { Count: > 0 } keys)
            throw new ResolveException(
                $"o mod RoNSpeech não tem equivalente para {orderId} — "
                + "essa ordem só funciona pelo menu, na tela");

        var names = new List<string>();

        // O 9 vem antes de tudo. Nas ordens de formação o 9 já significa a
        // própria formação, e enfileirar não faz sentido ali — repetir a tecla
        // trocaria o comando em vez de enfileirá-lo.
        if (intent.Queue && !keys.Contains(RonSpeechQueueKey, StringComparer.OrdinalIgnoreCase))
            names.Add(RonSpeechQueueKey);

        names.AddRange(keys);

        foreach (var name in names)
        {
            if (!KeyCatalog.TryResolve(name, out var token))
                throw new ResolveException(
                    $"não sabemos mandar a tecla {name}, que {orderId} precisa no RoNSpeech");
            steps.Add(new KeyStep(StepKind.Press, token, hold, gap));
        }

        return new KeySequence(steps);
    }

    /// <summary>
    /// Trocável a quente: mudar de modo na aba Configuração não pode exigir
    /// reabrir o app, e o pipeline guarda este resolvedor.
    /// </summary>
    public SendMode Mode { get; set; } = SendMode.Menu;

    /// <summary>
    /// A ordem que dispara as engatilhadas. É tecla direta no jogo (Z, o comando
    /// padrão da mira) — mas quando há gatilho armado no mod, é para lá que ela
    /// tem que ir, porque a fila vive no mod.
    /// </summary>
    public const string ExecuteOrderId = "confirm.default";

    /// <summary>
    /// Se a ordem já é uma tecla do jogo, e não um caminho pelo menu. Estas não
    /// passam por mod nenhum em modo nenhum: não há menu para pular, e elas nem
    /// aparecem na tabela do RonVoiceMod.
    /// </summary>
    public bool IsDirectKey(string? orderId) =>
        orderId is { } id
        && _map.Orders.GetValueOrDefault(id) is { Path.Count: > 0 } order
        && order.Path[0].StartsWith("KEY:", StringComparison.Ordinal);

    public KeySequence Resolve(Intent intent)
    {
        if (Mode == SendMode.RonSpeech) return ResolveViaRonSpeech(intent);

        var steps = new List<KeyStep>();
        var hold = _map.Timing.KeyHoldMs;
        var gap = _map.Timing.GapBetweenKeysMs;
        InputToken? heldMenu = null;

        if (intent.Element is { } element)
            steps.Add(new KeyStep(
                StepKind.Press, ResolveElement(element), hold, gap));

        if (intent.OrderId is not { } orderId)
        {
            if (steps.Count == 0)
                throw new ResolveException("intent vazio: sem elemento e sem ordem");
            return new KeySequence(steps);
        }

        if (!_map.Orders.TryGetValue(orderId, out var order))
            throw new ResolveException($"ordem desconhecida: {orderId}");

        for (var i = 0; i < order.Path.Count; i++)
        {
            var token = ResolvePathToken(order.Path[i]);
            var isLast = i == order.Path.Count - 1;

            // "é o passo que abre o menu?", não "resolveu para um botão de
            // mouse?". Os 100 ms de hold e os 60 ms de settle são do clique que
            // abre o menu — quem rebindar OpenSwatCommand para o teclado precisa
            // deles igual, e um dígito que caia num botão de mouse não precisa.
            var isMenu = order.Path[i] == "MENU";

            if (isMenu && _holdMenuOpen)
            {
                // Desce e NÃO solta. O Up entra depois de toda a navegação; o
                // SendInputSender já solta o que ficou descido se a sequência
                // abortar no meio, então o botão não fica preso.
                steps.Add(new KeyStep(
                    StepKind.Down, token, 0, _map.Timing.MenuOpenSettleMs));
                heldMenu = token;
            }
            else if (isLast && intent.Queue)
            {
                var shift = ResolveAction(ActionNames.HoldGoCode, _defaults.HoldCommand);
                steps.Add(new KeyStep(StepKind.Down, shift, 0, 0));
                steps.Add(new KeyStep(StepKind.Press, token, hold, gap));
                steps.Add(new KeyStep(StepKind.Up, shift, 0, 0));
            }
            else
            {
                steps.Add(new KeyStep(
                    StepKind.Press, token,
                    isMenu ? MouseHoldMs : hold,
                    isMenu ? _map.Timing.MenuOpenSettleMs : gap));
            }
        }

        if (heldMenu is not null)
            steps.Add(new KeyStep(StepKind.Up, heldMenu, 0, 0));

        // O clique de fechamento pertence ao modificador de fila, não à ordem.
        // Já nasce com GapAfterMs=0: é sempre o último passo, não precisa de
        // espera depois dele. O mesmo vale para os passos Down/Up do
        // modificador de fila, construídos com 0/0 acima. Passos comuns
        // (Press de uma tecla normal) mantêm o gap configurado no JSON mesmo
        // quando calham de ser o último da sequência — não há um passo
        // sintético "final" que zere isso de propósito.
        if (intent.Queue && order.CloseMenu)
            steps.Add(new KeyStep(
                StepKind.Press,
                ResolveAction(ActionNames.OpenSwatCommand, _defaults.SwatCommandMenu),
                MouseHoldMs, 0));

        return new KeySequence(steps);
    }

    InputToken ResolveElement(string element)
    {
        var fallback = element switch
        {
            "gold" => _defaults.SelectGold,
            "blue" => _defaults.SelectBlue,
            "red" => _defaults.SelectRed,
            _ => throw new ResolveException($"elemento desconhecido: {element}"),
        };
        return ResolveAction(ActionNames.ForElement(element), fallback);
    }

    InputToken ResolvePathToken(string token)
    {
        if (token == "MENU")
            return ResolveAction(ActionNames.OpenSwatCommand, _defaults.SwatCommandMenu);

        if (token.Length == 1 && token[0] is >= '1' and <= '9')
            return ResolveAction(
                ActionNames.ForDigit(token[0]),
                _defaults.CommandKeys[token[0] - '1']);

        if (token.StartsWith("KEY:", StringComparison.Ordinal))
        {
            var action = ActionNames.ForKeyToken(token);
            var literal = token["KEY:".Length..];
            var fallback = token switch
            {
                "KEY:DEFAULT_COMMAND" => _defaults.DefaultCommand,
                "KEY:INTERACT" => _defaults.InteractYell,
                _ => literal,
            };
            return action is null ? ResolveKeyName(fallback) : ResolveAction(action, fallback);
        }

        throw new ResolveException($"token de path desconhecido: {token}");
    }

    /// <summary>
    /// Bind real do jogo. As duas situações são diferentes e não podem ser
    /// coladas num único <c>&amp;&amp;</c>:
    /// <list type="bullet">
    /// <item>bind ausente do arquivo — cai no default do mapa, como a §7 da spec manda;</item>
    /// <item>bind presente mas fora do KeyCatalog — rejeita a ordem e nomeia a tecla.</item>
    /// </list>
    /// Cair no default no segundo caso manda uma tecla que o jogador
    /// explicitamente rebindou para outra coisa, sem exceção e sem log: é a
    /// falha "tecla errada, em silêncio". O jogo liga ações de SWAT à roda do
    /// mouse por padrão, e a roda é justamente o que não sabemos enviar.
    /// </summary>
    InputToken ResolveAction(string action, string fallbackKeyName)
    {
        if (!_binds.TryGetValue(action, out var bound))
            return ResolveKeyName(fallbackKeyName);

        return KeyCatalog.TryResolve(bound, out var token)
            ? token
            : throw new ResolveException(
                $"a ação {action} está ligada a {bound}, que não sabemos enviar");
    }

    static InputToken ResolveKeyName(string keyName) =>
        KeyCatalog.TryResolve(keyName, out var token)
            ? token
            : throw new ResolveException($"nome de tecla desconhecido: {keyName}");
}
