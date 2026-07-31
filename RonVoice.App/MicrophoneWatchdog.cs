using System.Diagnostics;
using System.Windows.Threading;

namespace RonVoice.App;

/// <summary>
/// Percebe quando a captura para de entregar áudio e manda reabrir.
///
/// Existe porque a captura morria em silêncio. O WaveInEvent para quando o
/// dispositivo some, o driver engasga, ou a enumeração muda — e numa máquina
/// com Voicemeeter e um headset de VR entrando e saindo, isso acontece. O app
/// continuava dizendo "escutando", porque o portão só sabe de foco e de mute,
/// não de áudio chegando. A única saída era fechar e abrir.
///
/// São dois modos de morte, e um deles não avisa:
/// <list type="bullet">
/// <item>o NAudio dispara RecordingStopped, agora com o motivo junto;</item>
/// <item>ou os blocos simplesmente param de chegar, sem evento nenhum. É por
/// isso que existe o relógio aqui: chegar áudio é o único sinal confiável de
/// que o microfone está vivo.</item>
/// </list>
/// </summary>
public sealed class MicrophoneWatchdog : IDisposable
{
    /// <summary>
    /// O WaveInEvent entrega um bloco a cada 50 ms. Três segundos é folga
    /// larga o bastante para não reagir a um engasgo, e curto o bastante para
    /// o jogador não terminar a missão falando sozinho.
    /// </summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(3);

    /// <summary>Espera antes de tentar de novo, para não virar laço apertado.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    readonly DispatcherTimer _timer;
    readonly Stopwatch _sinceAudio = Stopwatch.StartNew();
    readonly Action<string> _onDead;
    readonly TimeSpan _silence;
    readonly TimeSpan _retry;

    DateTime _lastAttempt = DateTime.MinValue;
    bool _running = true;

    /// <param name="onDead">
    /// Chamado com o motivo, na thread da interface. Quem recebe é que sabe
    /// reabrir o dispositivo e dizer na barra o que aconteceu.
    /// </param>
    /// <param name="silence">
    /// Injetável só para os testes: com os três segundos de produção, cada caso
    /// levaria quatro, e um teste lento é um teste que ninguém roda.
    /// </param>
    public MicrophoneWatchdog(
        Action<string> onDead, TimeSpan? silence = null, TimeSpan? retry = null)
    {
        _onDead = onDead;
        _silence = silence ?? Silence;
        _retry = retry ?? Retry;

        _timer = new DispatcherTimer
        {
            // Nunca mais lento que a própria janela de silêncio, senão a
            // deteccao demoraria o dobro do prometido.
            Interval = TimeSpan.FromMilliseconds(
                Math.Max(20, _silence.TotalMilliseconds / 3)),
        };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    /// <summary>Chamado a cada bloco de áudio. É o batimento.</summary>
    public void Heard() => _sinceAudio.Restart();

    /// <summary>
    /// Pausa o vigia enquanto o app está trocando o dispositivo de propósito —
    /// senão a própria troca dispararia um "morreu".
    /// </summary>
    public bool Running
    {
        get => _running;
        set
        {
            _running = value;
            if (value) _sinceAudio.Restart();
        }
    }

    /// <summary>O motivo relatado pelo NAudio, quando ele avisa.</summary>
    public void Stopped(Exception? error) =>
        Report(error is null ? "a captura parou" : $"a captura parou: {error.Message}");

    void Check()
    {
        if (!_running) return;
        if (_sinceAudio.Elapsed < _silence) return;

        Report($"o microfone não entrega áudio há {_sinceAudio.Elapsed.TotalSeconds:0}s");
    }

    void Report(string reason)
    {
        // Uma tentativa por vez: RecordingStopped e o relógio podem acusar a
        // mesma morte, e duas reaberturas ao mesmo tempo brigariam pelo
        // dispositivo.
        if (DateTime.UtcNow - _lastAttempt < _retry) return;
        _lastAttempt = DateTime.UtcNow;

        _sinceAudio.Restart();
        _onDead(reason);
    }

    public void Dispose() => _timer.Stop();
}
