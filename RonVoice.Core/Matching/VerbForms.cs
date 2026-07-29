namespace RonVoice.Core.Matching;

/// <summary>
/// As 371 frases em português foram escritas todas no imperativo informal
/// ("abre com flash"). Muita gente comanda no formal — "abra com flash" — e
/// isso não casava com nada: o jogador falava, o elemento era selecionado e
/// nenhuma ordem saía, sem erro em lugar nenhum.
///
/// A tabela é usada nos dois sentidos, e essa é a razão de existir:
/// <list type="bullet">
/// <item><see cref="Fold"/> traz o formal de volta ao informal antes de
/// pontuar, então nenhuma frase é duplicada no JSON, o catálogo continua
/// enxuto e os pesos IDF do matcher não mudam.</item>
/// <item><see cref="Variant"/> vai no sentido oposto para a gramática do
/// reconhecedor. Sem isso a dobra seria inútil: a gramática do Vosk é
/// fechada, e uma palavra que não está nela nunca é emitida — o "abra"
/// jamais chegaria ao matcher para ser dobrado.</item>
/// </list>
/// </summary>
public static class VerbForms
{
    /// <summary>
    /// (como está no mapa, como muita gente fala). Só português: em inglês
    /// não há essa dobra, e três formas formais — complete, execute, prepare —
    /// são palavras inglesas do próprio mapa. Dobrar sem olhar o idioma
    /// transformaria "prepare to open" em "prepara" e quebraria o inglês.
    /// </summary>
    /// <remarks>
    /// O lado informal precisa bater LETRA POR LETRA com o que está no
    /// ron_commands.json, inclusive na ausência de acento: o mapa escreve
    /// "avanca", "calca", "lanca" e "poe" sem cedilha nem til. Escrever
    /// "avança" aqui não dá erro — a chave simplesmente nunca casa e a
    /// variante desaparece calada. TheInformalSideMatchesTheMap prende isso.
    /// </remarks>
    static readonly (string Informal, string Formal)[] PtPairs =
    [
        ("abre", "abra"), ("aguarda", "aguarde"), ("algema", "algeme"),
        ("amarra", "amarre"), ("arromba", "arrombe"), ("assegura", "assegure"),
        ("atira", "atire"), ("avanca", "avance"), ("calca", "calce"),
        ("chuta", "chute"), ("cobre", "cubra"), ("completa", "complete"),
        ("confirma", "confirme"), ("deita", "deite"), ("desarma", "desarme"),
        ("desliza", "deslize"), ("destranca", "destranque"), ("detona", "detone"),
        ("divide", "divida"), ("empilha", "empilhe"), ("entra", "entre"),
        ("escaneia", "escaneie"), ("espera", "espere"), ("espia", "espie"),
        ("executa", "execute"), ("explode", "exploda"), ("fatia", "fatie"),
        ("fecha", "feche"), ("fica", "fique"), ("forma", "forme"),
        ("gaseia", "gaseie"), ("joga", "jogue"), ("lanca", "lance"),
        ("leva", "leve"), ("limpa", "limpe"), ("manda", "mande"),
        ("marca", "marque"), ("move", "mova"), ("muda", "mude"),
        ("neutraliza", "neutralize"), ("olha", "olhe"), ("para", "pare"),
        ("poe", "ponha"), ("posiciona", "posicione"), ("prende", "prenda"),
        ("prepara", "prepare"), ("reagrupa", "reagrupe"), ("revista", "reviste"),
        ("segue", "siga"), ("segura", "segure"), ("separa", "separe"),
        ("solta", "solte"), ("tira", "tire"), ("trava", "trave"),
        ("traz", "traga"), ("vai", "vá"), ("varre", "varra"),
        ("vasculha", "vasculhe"), ("vigia", "vigie"),
    ];

    /// <summary>Só para os testes conferirem a tabela contra o mapa.</summary>
    public static IEnumerable<string> PtInformalForms() => PtPairs.Select(p => p.Informal);

    // "dar" ficou de fora: o imperativo formal é "dê", que sem acento é "de" —
    // a preposição mais comum do português e stopword do matcher. Dobrar
    // "de" para "da" estragaria toda frase que tem a preposição.

    /// <summary>Sem acento, como o matcher vê: "abra" -> "abre", "va" -> "vai".</summary>
    static readonly Dictionary<string, string> ToInformal = Build(
        p => Single(TextNormalizer.Tokenize(p.Formal)),
        p => Single(TextNormalizer.Tokenize(p.Informal)));

    /// <summary>Com acento, como a gramática precisa: "põe" -> "ponha".</summary>
    static readonly Dictionary<string, string> ToFormal = Build(
        p => Single(TextNormalizer.TokenizeKeepingAccents(p.Informal)),
        p => Single(TextNormalizer.TokenizeKeepingAccents(p.Formal)));

    static Dictionary<string, string> Build(
        Func<(string Informal, string Formal), string> key,
        Func<(string Informal, string Formal), string> value)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in PtPairs) d[key(pair)] = value(pair);
        return d;
    }

    static string Single(IReadOnlyList<string> tokens) => tokens.Count == 1
        ? tokens[0]
        : throw new InvalidOperationException(
            $"forma verbal com mais de uma palavra: {string.Join(' ', tokens)}");

    public static bool AppliesTo(string language) =>
        string.Equals(language, "pt", StringComparison.Ordinal);

    /// <summary>
    /// Troca cada forma formal pela que existe no mapa. Devolve a mesma lista
    /// quando nada muda, que é o caso da esmagadora maioria das falas.
    /// </summary>
    public static IReadOnlyList<string> Fold(IReadOnlyList<string> tokens, string language)
    {
        if (!AppliesTo(language)) return tokens;

        string[]? folded = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!ToInformal.TryGetValue(tokens[i], out var informal)) continue;
            folded ??= [.. tokens];
            folded[i] = informal;
        }
        return folded ?? tokens;
    }

    /// <summary>
    /// Normalização canônica: o que duas frases equivalentes têm em comum.
    /// É o que as checagens de colisão comparam — sem a dobra, "abra com
    /// flash" numa ordem e "abre com flash" em outra passariam como frases
    /// distintas e deixariam as duas mudas.
    /// </summary>
    public static string Canonical(string text, string language) =>
        string.Join(' ', Fold(TextNormalizer.Tokenize(text), language));

    /// <summary>
    /// A frase no imperativo formal, ou null se não houver verbo para trocar.
    /// Uma variante por frase, com todas as trocas de uma vez: a gramática do
    /// Vosk compõe entre entradas, então basta a palavra existir em algum
    /// lugar dela para poder ser ouvida em qualquer arranjo. Gerar o produto
    /// cartesiano das misturas só inflaria a gramática.
    /// </summary>
    public static string? Variant(string phrase, string language)
    {
        if (!AppliesTo(language)) return null;

        var tokens = TextNormalizer.TokenizeKeepingAccents(phrase);
        string[]? changed = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!ToFormal.TryGetValue(tokens[i], out var formal)) continue;
            changed ??= [.. tokens];
            changed[i] = formal;
        }
        return changed is null ? null : string.Join(' ', changed);
    }
}
