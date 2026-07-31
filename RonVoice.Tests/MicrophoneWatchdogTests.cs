using System.Windows.Threading;
using RonVoice.App;

namespace RonVoice.Tests;

/// <summary>
/// O vigia do microfone. Existe porque a captura morria em silêncio: o
/// dispositivo sumia, o áudio parava, e o app seguia dizendo "escutando" — a
/// única saída era fechar e abrir.
/// </summary>
[Collection(WpfCollection.Name)]
public class MicrophoneWatchdogTests
{
    static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(120);
    static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// Roda o vigia na thread da interface e bombeia o dispatcher enquanto
    /// espera — DispatcherTimer não dispara sem alguém processando a fila.
    /// </summary>
    static List<string> Watch(TimeSpan duration, Action<MicrophoneWatchdog>? act = null)
    {
        var reasons = new List<string>();
        var done = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            var dog = new MicrophoneWatchdog(reasons.Add, Silence, Retry);
            act?.Invoke(dog);

            var stop = new DispatcherTimer { Interval = duration };
            stop.Tick += (_, _) =>
            {
                stop.Stop();
                dog.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            };
            stop.Start();

            Dispatcher.Run();
            done.Set();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait(TimeSpan.FromSeconds(10));

        return reasons;
    }

    /// <summary>
    /// O caso relatado: o áudio simplesmente para de chegar, sem evento nenhum
    /// do NAudio. Chegar bloco é o único sinal confiável de que está vivo.
    /// </summary>
    [Fact]
    public void SilenceOfCallbacksIsNoticed()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(400));

        Assert.NotEmpty(reasons);
        Assert.Contains("não entrega áudio", reasons[0]);
    }

    [Fact]
    public void AudioArrivingKeepsItQuiet()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(400), dog =>
        {
            // Bate mais rápido que a janela de silêncio, como o áudio real faz.
            var beat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            beat.Tick += (_, _) => dog.Heard();
            beat.Start();
        });

        Assert.Empty(reasons);
    }

    /// <summary>
    /// Enquanto o app troca o dispositivo de propósito, a própria troca
    /// dispararia um "morreu" — e o vigia brigaria com quem está consertando.
    /// </summary>
    [Fact]
    public void PausedItSaysNothing()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(400), dog => dog.Running = false);

        Assert.Empty(reasons);
    }

    [Fact]
    public void ResumingRestartsTheClockInsteadOfFiringImmediately()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(100), dog =>
        {
            dog.Running = false;
            dog.Running = true;
        });

        Assert.Empty(reasons);
    }

    /// <summary>
    /// O NAudio avisando e o relógio percebendo acusam a MESMA morte. Duas
    /// reaberturas ao mesmo tempo brigariam pelo dispositivo.
    /// </summary>
    [Fact]
    public void TheSameDeathIsOnlyReportedOnce()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(80), dog =>
        {
            dog.Stopped(new InvalidOperationException("dispositivo sumiu"));
            dog.Stopped(new InvalidOperationException("dispositivo sumiu"));
            dog.Stopped(new InvalidOperationException("dispositivo sumiu"));
        });

        Assert.Single(reasons);
    }

    /// <summary>
    /// Quando o NAudio diz o motivo, ele chega inteiro até a barra: é o único
    /// jeito de descobrirmos, na próxima vez, o que derruba o microfone.
    /// </summary>
    [Fact]
    public void TheReasonFromNAudioIsCarriedThrough()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(80),
            dog => dog.Stopped(new InvalidOperationException("dispositivo sumiu")));

        Assert.Single(reasons);
        Assert.Contains("dispositivo sumiu", reasons[0]);
    }

    [Fact]
    public void AStopWithNoReasonStillReports()
    {
        var reasons = Watch(TimeSpan.FromMilliseconds(80), dog => dog.Stopped(null));

        Assert.Single(reasons);
        Assert.Contains("parou", reasons[0]);
    }

    /// <summary>
    /// Três segundos: folga para um engasgo, e curto o bastante para o jogador
    /// não terminar a missão falando sozinho.
    /// </summary>
    [Fact]
    public void TheShippedWindowIsThreeSeconds() =>
        Assert.Equal(3, MicrophoneWatchdog.Silence.TotalSeconds);
}
