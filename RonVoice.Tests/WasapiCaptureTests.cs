using RonVoice.Core.Audio;

namespace RonVoice.Tests;

/// <summary>
/// O microfone em si depende de hardware e não é testável aqui. O que dá para
/// cobrir é a recusa clara: pedir um dispositivo que não existe tem que falhar
/// nomeando o problema, e não estourar dentro do NAudio depois.
/// </summary>
public class WasapiCaptureTests
{
    [Fact]
    public void RejectsADeviceIndexThatDoesNotExist()
    {
        var devices = WasapiCapture.ListDevices();
        if (devices.Count == 0) return;      // sem microfone nesta máquina: nada a provar

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WasapiCapture(devices.Count + 50));
        Assert.Contains("--list-devices", ex.Message);
    }

    [Fact]
    public void ListDevicesDoesNotThrow() => _ = WasapiCapture.ListDevices();
}
