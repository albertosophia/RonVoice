using System.Text.RegularExpressions;
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

/// <summary>
/// A tabela que diz COMO pedir cada ordem ao jogo vive em Lua, nao aqui: so' o
/// Lua consegue segurar uma classe de blueprint e os booleanos que a funcao do
/// jogo recebe. Um int no mapa nao daria conta.
///
/// O perigo e' o id. O C# manda "door.breach.kick.gas" pela caixa de correio; o
/// Lua procura essa chave na tabela. Se as duas pontas discordarem de uma letra,
/// a ordem simplesmente nunca acontece — sem erro, sem aviso, no meio da missao.
/// As duas linguagens nao compilam juntas: este teste e' o unico lugar onde elas
/// se encontram.
/// </summary>
public class ModOrdersTests
{
    /// <summary>
    /// O orders.lua nao e' copiado para a saida do build: ele viaja com o mod,
    /// nao com o app. Entao subimos ate' achar a solucao.
    /// </summary>
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RonVoice.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir.FullName;
    }

    static readonly string LuaPath = Path.Combine(
        RepoRoot(), "tools", "RonVoiceMod", "Scripts", "orders.lua");

    /// <summary>
    /// Ordem cujo caminho comeca em KEY: ja' e' uma tecla direta — nao passa
    /// pelo menu, entao nao ha' menu para o mod pular. Passa-la pelo mod seria
    /// dar a volta para chegar no mesmo lugar. A regra sai do mapa, e nao de uma
    /// lista escrita a mao aqui, que envelheceria calada.
    /// </summary>
    static bool StaysOnKeys(OrderDefinition o) => o.Path[0].StartsWith("KEY:");

    public sealed record LuaOrder(string Id, string Body)
    {
        public string? Field(string name) =>
            Regex.Match(Body, $@"\b{name}\s*=\s*([^,}}]+)") is { Success: true } m
                ? m.Groups[1].Value.Trim()
                : null;

        public bool NeedsCheckingInGame => Field("verify") == "true";
    }

    static List<LuaOrder> Read()
    {
        var texto = File.ReadAllText(LuaPath);
        return Regex.Matches(texto, @"\[""([^""]+)""\]\s*=\s*\{([^}]*)\}")
            .Select(m => new LuaOrder(m.Groups[1].Value, m.Groups[2].Value))
            .ToList();
    }

    static LuaOrder Get(string id) => Read().Single(o => o.Id == id);

    [Fact]
    public void EveryIdInTheLuaTableIsARealOrder()
    {
        var mapa = CommandMap.Load(CommandMapTests.MapPath).Orders;

        var inventados = Read().Select(o => o.Id).Where(id => !mapa.ContainsKey(id)).ToList();

        Assert.Empty(inventados);
    }

    [Fact]
    public void EveryOrderThatIsNotThePlayersIsInTheLuaTable()
    {
        var naTabela = Read().Select(o => o.Id).ToHashSet();

        var faltando = CommandMap.Load(CommandMapTests.MapPath).Orders.Values
            .Where(o => !StaysOnKeys(o) && !naTabela.Contains(o.Id))
            .Select(o => o.Id)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(faltando);
    }

    /// <summary>
    /// E o contrario: uma ordem de tecla na tabela do mod significaria duas
    /// rotas para a mesma coisa, e uma delas sem ninguem olhando.
    /// </summary>
    [Fact]
    public void NoKeyOrderIsInTheLuaTable()
    {
        var naTabela = Read().Select(o => o.Id).ToHashSet();

        var sobrando = CommandMap.Load(CommandMapTests.MapPath).Orders.Values
            .Where(o => StaysOnKeys(o) && naTabela.Contains(o.Id))
            .Select(o => o.Id)
            .ToList();

        Assert.Empty(sobrando);
    }

    [Fact]
    public void TheLuaTableNeverRepeatsAnId()
    {
        var repetidos = Read().GroupBy(o => o.Id).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToList();

        Assert.Empty(repetidos);
    }

    /// <summary>
    /// EDoorBreachType, colhido do jogo: Open=1 Move=2 Kick=3 Shotgun=4 Ram=5
    /// C2=6 Leader=7. Os valores 1, 2, 4 e 6 estao provados — sao os que o
    /// RoNSpeech passa, e o mod funciona em jogo. 3, 5 e 7 vem do mesmo enum,
    /// pela mesma leitura.
    /// </summary>
    [Theory]
    [InlineData("door.open.clear", "1")]
    [InlineData("door.breach.kick.clear", "3")]
    [InlineData("door.breach.shotgun.clear", "4")]
    [InlineData("door.breach.ram.clear", "5")]
    [InlineData("door.breach.c2.clear", "6")]
    [InlineData("door.breach.leader.clear", "7")]
    public void EachBreachFamilyCarriesItsOwnType(string id, string tipo) =>
        Assert.Equal(tipo, Get(id).Field("breach"));

    /// <summary>
    /// A granada e' uma classe de blueprint, achada por caminho. Errar o caminho
    /// devolve nil e a ordem sai SEM granada — arrombar sem o gas que voce pediu.
    /// Por isso os quatro caminhos ficam presos aqui.
    /// </summary>
    [Theory]
    [InlineData("door.breach.kick.clear", null)]
    [InlineData("door.breach.kick.flashbang", "FLASHBANG")]
    [InlineData("door.breach.kick.stinger", "STINGER")]
    [InlineData("door.breach.kick.gas", "GAS")]
    public void TheGrenadeGoesWithTheOrder(string id, string? granada) =>
        Assert.Equal(granada, Get(id).Field("grenade"));

    [Theory]
    [InlineData("FLASHBANG", "Grenade_Flashbang_V2")]
    [InlineData("STINGER", "Grenade_Stinger_V2")]
    [InlineData("GAS", "Grenade_CSGas_V2")]
    public void EachGrenadePathIsTheOneTheGameAnswersTo(string nome, string blueprint)
    {
        var caminho = Regex.Match(File.ReadAllText(LuaPath),
            $@"local\s+{nome}\s*=\s*""([^""]+)""").Groups[1].Value;

        Assert.Equal($"/Game/Blueprints/Items/WeaponsRevised/{blueprint}.{blueprint}_C", caminho);
    }

    /// <summary>
    /// Os dois booleanos da funcao do jogo sao HIPOTESE: o RoNSpeech os liga por
    /// tecla modificadora e nunca diz o que significam. Lancador e granada do
    /// lider dependem deles. Enquanto ninguem confirmar em jogo, tem que estar
    /// marcado — um palpite que se passa por certeza e' pior que um buraco.
    /// </summary>
    [Theory]
    [InlineData("door.breach.kick.launcher")]
    [InlineData("door.breach.kick.leader")]
    [InlineData("door.breach.ram.launcher")]
    [InlineData("door.breach.ram.leader")]
    public void WhatIsStillAGuessSaysSo(string id) =>
        Assert.True(Get(id).NeedsCheckingInGame, $"{id} devia estar marcada verify");

    /// <summary>
    /// E o contrario: o que esta provado nao pode estar marcado, senao a marca
    /// vira ruido e ninguem olha mais para ela.
    /// </summary>
    [Theory]
    [InlineData("door.open.clear")]
    [InlineData("door.breach.shotgun.clear")]
    [InlineData("door.breach.c2.flashbang")]
    public void WhatIsProvenIsNotMarked(string id) =>
        Assert.False(Get(id).NeedsCheckingInGame, $"{id} nao precisa de verify");

    /// <summary>
    /// Toda entrada diz qual funcao do jogo chamar. Sem isso o Lua nao sabe o
    /// que fazer com ela.
    /// </summary>
    [Fact]
    public void EveryEntrySaysWhichCallToMake()
    {
        var sem = Read().Where(o => o.Field("call") is null).Select(o => o.Id).ToList();

        Assert.Empty(sem);
    }
}
