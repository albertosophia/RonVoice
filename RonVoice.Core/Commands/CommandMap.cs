using System.Text.Json;

namespace RonVoice.Core.Commands;

/// <summary>
/// Carrega ron_commands.json. Fonte de verdade única do que o app entende e
/// do que consegue executar. Indexado por id — nunca por path, que é ambíguo.
/// </summary>
public sealed class CommandMap
{
    public IReadOnlyDictionary<string, OrderDefinition> Orders { get; }
    public IReadOnlyDictionary<string, ElementDefinition> Elements { get; }
    public ModifierDefinition Queue { get; }
    public KeybindDefaults Defaults { get; }
    public TimingSettings Timing { get; }

    CommandMap(
        IReadOnlyDictionary<string, OrderDefinition> orders,
        IReadOnlyDictionary<string, ElementDefinition> elements,
        ModifierDefinition queue,
        KeybindDefaults defaults,
        TimingSettings timing)
    {
        Orders = orders;
        Elements = elements;
        Queue = queue;
        Defaults = defaults;
        Timing = timing;
    }

    /// <summary>
    /// Cópia com outro conjunto de ordens, preservando o resto. Usado pela
    /// mesclagem das frases do usuário, que não pode alterar o mapa no lugar:
    /// o mapa de fábrica precisa continuar disponível para comparação.
    /// </summary>
    public CommandMap WithOrders(IReadOnlyDictionary<string, OrderDefinition> orders) =>
        new(orders, Elements, Queue, Defaults, Timing);

    /// <summary>
    /// Cópia com outros tempos. Existe para poder MEDIR o tempo certo em vez de
    /// escolhê-lo por intuição: em VR a latência entre o clique e o menu aceitar
    /// input é outra, e 60 ms podem chegar antes do menu estar pronto.
    /// </summary>
    public CommandMap WithTiming(TimingSettings timing) =>
        new(Orders, Elements, Queue, Defaults, timing);

    public static CommandMap Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var orders = new Dictionary<string, OrderDefinition>(StringComparer.Ordinal);
        foreach (var o in root.GetProperty("orders").EnumerateArray())
        {
            var id = o.GetProperty("id").GetString()!;
            if (orders.ContainsKey(id))
                throw new InvalidDataException($"id duplicado no mapa: {id}");

            orders[id] = new OrderDefinition(
                id,
                o.GetProperty("context").GetString()!,
                StringList(o.GetProperty("path")),
                o.TryGetProperty("close_menu", out var cm) && cm.GetBoolean(),
                o.GetProperty("confidence").GetString()!,
                PhraseMap(o.GetProperty("phrases")));
        }

        var elements = new Dictionary<string, ElementDefinition>(StringComparer.Ordinal);
        foreach (var e in root.GetProperty("elements").EnumerateObject())
            elements[e.Name] = new ElementDefinition(
                e.Name, e.Value.GetProperty("key").GetString()!, PhraseMap(e.Value));

        var queue = new ModifierDefinition(
            PhraseMap(root.GetProperty("modifiers").GetProperty("queue")));

        var kd = root.GetProperty("keybind_defaults");
        var defaults = new KeybindDefaults(
            kd.GetProperty("swat_command_menu").GetString()!,
            kd.GetProperty("default_command").GetString()!,
            kd.GetProperty("hold_command").GetString()!,
            kd.GetProperty("back").GetString()!,
            kd.GetProperty("select_gold").GetString()!,
            kd.GetProperty("select_blue").GetString()!,
            kd.GetProperty("select_red").GetString()!,
            StringList(kd.GetProperty("command_keys")),
            kd.GetProperty("interact_yell").GetString()!);

        var t = root.GetProperty("timing");
        var timing = new TimingSettings(
            t.GetProperty("key_hold_ms").GetInt32(),
            t.GetProperty("gap_between_keys_ms").GetInt32(),
            t.GetProperty("menu_open_settle_ms").GetInt32());

        return new CommandMap(orders, elements, queue, defaults, timing);
    }

    static IReadOnlyList<string> StringList(JsonElement arr) =>
        arr.EnumerateArray().Select(x => x.GetString()!).ToArray();

    /// <summary>Lê só as chaves "en" e "pt"; ignora "key", "how", "_nota".</summary>
    static IReadOnlyDictionary<string, IReadOnlyList<string>> PhraseMap(JsonElement obj)
    {
        var d = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var lang in new[] { "en", "pt" })
            if (obj.TryGetProperty(lang, out var v) && v.ValueKind == JsonValueKind.Array)
                d[lang] = StringList(v);
        return d;
    }
}
