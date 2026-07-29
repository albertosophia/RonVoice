using RonVoice.Cli.Commands;

var command = args.Length > 0 ? args[0] : "help";
var rest = args.Skip(1).ToArray();

return command switch
{
    "test" => TestCommand.Run(rest),
    "keymap" => KeymapCommand.Run(rest),
    "corpus" => CorpusCommand.Run(rest),
    "send" => SendCommand.Run(rest),
    "synth" => SynthCommand.Run(rest),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine("""
        ronvoice test "<frase>" [--lang en|pt]   casa a frase e imprime a sequência
        ronvoice keymap [--ini <caminho>]        imprime os binds resolvidos
        ronvoice corpus [--out <pasta>]          regenera corpus/{en,pt}.tsv
        ronvoice send "<frase>" [--dry-run] [--force] [--delay <segundos>] [--process <nome>]   envia ao jogo
        ronvoice synth --out <pasta> [--lang en|pt] [--phrase "<texto>"] [--limit N]   gera WAVs de teste
        """);
    return 1;
}
