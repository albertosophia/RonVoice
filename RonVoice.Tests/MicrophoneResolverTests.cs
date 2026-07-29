using RonVoice.Core.Audio;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

/// <summary>
/// Índices de WaveIn se deslocam quando um dispositivo entra ou sai. Entrar em
/// VR faz exatamente isso, e o índice salvo passa a apontar para outro
/// microfone — o app grava silêncio e não há erro em lugar nenhum.
/// </summary>
public class MicrophoneResolverTests
{
    /// <summary>A lista da máquina do autor, que é o pior caso: 12 dispositivos.</summary>
    static readonly string[] Desktop =
    [
        "Microfone (Virtual Desktop Audi",
        "Microfone (Steam Streaming Micr",
        "CABLE Output (VB-Audio Virtual ",
        "Voicemeeter Out A2 (VB-Audio Vo",
        "Voicemeeter Out B1 (VB-Audio Vo",
        "Voicemeeter Out A3 (VB-Audio Vo",
        "Microfone (WIND)",
        "Voicemeeter Out A4 (VB-Audio Vo",
    ];

    [Fact]
    public void FindsTheDeviceByName()
    {
        var choice = MicrophoneResolver.Resolve(Desktop, "Microfone (WIND)");

        Assert.Equal(6, choice.Index);
        Assert.Equal("Microfone (WIND)", choice.Name);
        Assert.Null(choice.Problem);
    }

    /// <summary>
    /// O caso do VR: dois dispositivos saem da enumeração e tudo depois deles
    /// anda para trás. Pelo índice, o 6 viraria outro microfone; pelo nome,
    /// continua sendo o mesmo.
    /// </summary>
    [Fact]
    public void SurvivesTheEnumerationShiftingWhenDevicesDisappear()
    {
        string[] afterShift =
        [
            "CABLE Output (VB-Audio Virtual ",
            "Voicemeeter Out A2 (VB-Audio Vo",
            "Voicemeeter Out B1 (VB-Audio Vo",
            "Voicemeeter Out A3 (VB-Audio Vo",
            "Microfone (WIND)",
            "Voicemeeter Out A4 (VB-Audio Vo",
        ];

        var choice = MicrophoneResolver.Resolve(afterShift, "Microfone (WIND)", 6);

        Assert.Equal(4, choice.Index);
        Assert.Equal("Microfone (WIND)", choice.Name);
        Assert.Null(choice.Problem);
    }

    /// <summary>
    /// Sem o aviso, o usuário fala e nada acontece — e conclui que o
    /// reconhecimento é ruim, quando o app está gravando de outro dispositivo.
    /// </summary>
    [Fact]
    public void SaysSoLoudlyWhenTheRequestedDeviceIsGone()
    {
        var choice = MicrophoneResolver.Resolve(Desktop, "Microfone (Headset do VR)", 0);

        Assert.NotNull(choice.Problem);
        Assert.Contains("Microfone (Headset do VR)", choice.Problem);
        Assert.Contains(choice.Name, choice.Problem);
    }

    [Fact]
    public void AnOldSettingsFileWithOnlyAnIndexStillWorks()
    {
        var choice = MicrophoneResolver.Resolve(Desktop, null, 6);

        Assert.Equal(6, choice.Index);
        Assert.Equal("Microfone (WIND)", choice.Name);
        Assert.Null(choice.Problem);
    }

    [Fact]
    public void AnIndexPastTheEndFallsBackToTheFirstDeviceInsteadOfThrowing()
    {
        var choice = MicrophoneResolver.Resolve(Desktop, null, 99);

        Assert.Equal(0, choice.Index);
        Assert.Equal(Desktop[0], choice.Name);
    }

    [Fact]
    public void NoDevicesAtAllIsReportedNotGuessed()
    {
        var choice = MicrophoneResolver.Resolve([], "Microfone (WIND)");

        Assert.Equal(-1, choice.Index);
        Assert.NotNull(choice.Problem);
    }

    [Fact]
    public void TheNameComparisonIgnoresCase() =>
        Assert.Equal(6, MicrophoneResolver.Resolve(Desktop, "microfone (wind)").Index);

    // ---- persistencia ----

    /// <summary>
    /// O nome tem que sobreviver ao disco, senão a próxima abertura volta a
    /// depender da posição.
    /// </summary>
    [Fact]
    public void TheChosenNameRoundTripsThroughSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ronvoice-mic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (_, path, _) = SettingsStore.Load(dir);
            SettingsStore.Save(
                AppSettings.Default with
                {
                    MicrophoneDevice = 6,
                    MicrophoneName = "Microfone (WIND)",
                },
                path);

            var (loaded, _, _) = SettingsStore.Load(dir);
            Assert.Equal("Microfone (WIND)", loaded.MicrophoneName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SettingsWrittenBeforeThisFeatureLoadWithoutAName()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ronvoice-mic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (_, path, _) = SettingsStore.Load(dir);
            File.WriteAllText(path, """
                {
                  "language": "pt",
                  "microphoneDevice": 6,
                  "mode": "AlwaysOn",
                  "confidenceThreshold": 0
                }
                """);

            var (loaded, _, _) = SettingsStore.Load(dir);
            Assert.Equal(6, loaded.MicrophoneDevice);
            Assert.Null(loaded.MicrophoneName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
