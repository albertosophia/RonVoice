using System.Diagnostics;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Manda uma bateria de ordens escolhidas para isolar QUAL camada come o input,
/// gravando tudo em arquivo.
///
/// Existe por causa do VR. Fora do VR tudo funciona; dentro, nada funciona — e
/// não dá para investigar pelo console, porque com o headset na cara ninguém lê
/// a tela, e alt-tabear para ler muda o foco que se está medindo.
///
/// As ordens não são arbitrárias. Umas são tecla pura; outras precisam abrir o
/// menu SWAT, que neste projeto costuma estar num clique de mouse. Se as de
/// teclado passarem e as de mouse não, a camada culpada está identificada.
/// </summary>
public static class ProbeCommand
{
    /// <param name="Phrase">O que mandar.</param>
    /// <param name="Why">Por que esta ordem está na bateria.</param>
    /// <param name="Observe">O que olhar no jogo para saber se funcionou.</param>
    /// <param name="SettleMs">
    /// Espera entre abrir o menu e o primeiro dígito, ou null para o valor do
    /// mapa. A bateria varre vários porque o valor certo em VR é uma medição,
    /// não um palpite: se o dígito chega antes do menu aceitar input, ele é
    /// engolido e o menu fica aberto à mercê de para onde o jogador olha.
    /// </param>
    /// <param name="HoldMenu">
    /// Manter o botão do menu pressionado durante a navegação, em vez de clicar
    /// e soltar antes dos dígitos.
    /// </param>
    sealed record Probe(
        string Phrase, string Why, string Observe,
        int? SettleMs = null, bool HoldMenu = false);

    /// <summary>
    /// A varredura de tempo (60/300/800 ms) já foi feita e não mudou nada, então
    /// saiu: latência está descartada. O que sobra é a FORMA da sequência, e as
    /// duas formas ficam lado a lado com o mesmo tempo para a comparação ser
    /// sobre uma variável só.
    /// </summary>
    static readonly Probe[] Battery =
    [
        new("fire select", "controle: tecla pura (X), nenhum menu",
            "o indicador de modo de tiro no HUD muda"),
        new("stack up", "FORMA A: clica e solta o menu, depois os digitos (atual)",
            "o esquadrão se posiciona (MIRE NUMA PORTA)", 300),
        new("stack up", "FORMA B: SEGURA o menu, digita, e solta no fim",
            "o esquadrão se posiciona (MIRE NUMA PORTA)", 300, HoldMenu: true),
        new("open the door", "FORMA B numa ordem de um digito só",
            "a porta é aberta (MIRE NUMA PORTA)", 300, HoldMenu: true),
    ];

    public static int Run(string[] args)
    {
        var lang = Cli.Option(args, "--lang") ?? "en";
        // Sem isto não há como exercitar a sonda sem despejar teclas na janela
        // que estiver em foco, o que é um efeito colateral de verdade.
        var dryRun = Cli.Flag(args, "--dry-run");
        var delay = Cli.Option(args, "--seconds") is { } s && int.TryParse(s, out var d) ? d : 25;
        var gap = Cli.Option(args, "--gap") is { } g && int.TryParse(g, out var gv) ? gv : 8;
        var path = Cli.Option(args, "--out")
            ?? Path.Combine(AppContext.BaseDirectory, "sonda.txt");

        using var file = new StreamWriter(path, append: false) { AutoFlush = true };
        void Log(string line = "")
        {
            Console.WriteLine(line);
            file.WriteLine(line);
        }

        var map = Cli.LoadMap(lang);
        var iniPath = KeybindReader.FindDefaultIniPath();
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        Log("=== ambiente ===");
        Log($"elevado        : {ForegroundGuard.IsElevated()}");
        Log($"Input.ini      : {iniPath ?? "(não encontrado — usando defaults)"}");
        Log($"idioma         : {lang}");
        Log($"tecla do menu  : {binds.GetValueOrDefault(ActionNames.OpenSwatCommand) ?? "(default)"}");
        Log();
        Log($"Vai mandar {Battery.Length} ordens, uma a cada {gap}s, começando em {delay}s.");
        Log("Ponha o headset agora. Fique de frente para uma porta.");
        Log();

        for (var i = delay; i > 0; i--)
        {
            Console.Write($"\r{i,3}s… ");
            Thread.Sleep(1000);
        }
        Console.WriteLine("\r        ");

        var clock = Stopwatch.StartNew();
        var matcher = new PhraseMatcher(map, lang);

        foreach (var probe in Battery)
        {
            // Cada entrada resolve com os SEUS tempos, para a varredura de
            // espera do menu ser uma medição e não um palpite.
            var resolver = new CommandResolver(
                probe.SettleMs is { } ms
                    ? map.WithTiming(map.Timing with { MenuOpenSettleMs = ms })
                    : map,
                binds,
                defaults: null,
                holdMenuOpen: probe.HoldMenu);

            Log($"--- {clock.Elapsed.TotalSeconds,5:0.0}s  \"{probe.Phrase}\"");
            Log($"    porque : {probe.Why}");
            Log($"    olhe   : {probe.Observe}");

            // O foco é registrado NO INSTANTE do envio, não antes: é o único
            // jeito de saber se ele mudou entre a contagem e a ordem sair.
            var foreground = ForegroundGuard.ForegroundProcessName() ?? "(nenhum)";
            var isGame = ForegroundGuard.Matches(foreground);
            Log($"    foco   : {foreground} ({(isGame ? "é o jogo" : "NÃO é o jogo")})");

            var intent = matcher.Match(probe.Phrase);
            if (intent is null) { Log("    RESULTADO: a frase não casou com ordem nenhuma"); Log(); continue; }

            KeySequence seq;
            try { seq = resolver.Resolve(intent); }
            catch (ResolveException ex) { Log($"    RESULTADO: não resolve — {ex.Message}"); Log(); continue; }

            Log($"    ordem  : {intent.OrderId}");

            var sender = new SendInputSender(dryRun);
            try
            {
                sender.Send(seq);
                // Se chegou aqui, o Windows aceitou TODOS os eventos: o
                // SendInputSender lança exceção quando SendInput recusa um.
                Log($"    enviado: {string.Join(", ", sender.Log)}");
                Log(dryRun
                    ? "    RESULTADO: dry-run, nada saiu de verdade"
                    : "    RESULTADO: o Windows aceitou todos os eventos");
            }
            catch (InvalidOperationException ex)
            {
                Log($"    enviado: {string.Join(", ", sender.Log)}");
                Log($"    RESULTADO: O WINDOWS RECUSOU — {ex.Message}");
            }

            Log();
            Thread.Sleep(gap * 1000);
        }

        Log("=== fim ===");
        Log("Para cada ordem, anote se ACONTECEU no jogo. O que o arquivo NÃO");
        Log("sabe é isso: ele só registra até a borda do Windows.");
        Console.WriteLine();
        Console.WriteLine($"Arquivo: {path}");
        return 0;
    }
}
