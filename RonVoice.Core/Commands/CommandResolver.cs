using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Commands;

public sealed class ResolveException(string message) : Exception(message);

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

    public CommandResolver(
        CommandMap map,
        IReadOnlyDictionary<string, string> binds,
        KeybindDefaults? defaults = null)
    {
        _map = map;
        _binds = binds;
        _defaults = defaults ?? map.Defaults;
    }

    public KeySequence Resolve(Intent intent)
    {
        var steps = new List<KeyStep>();
        var hold = _map.Timing.KeyHoldMs;
        var gap = _map.Timing.GapBetweenKeysMs;

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

            if (isLast && intent.Queue)
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
