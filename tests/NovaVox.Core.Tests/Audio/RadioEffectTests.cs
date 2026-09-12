using NovaVox.Core.Audio;
using Xunit;

namespace NovaVox.Core.Tests.Audio;

public class RadioEffectTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novavox-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static short[] SineWave(int n, int sampleRate, double freq, double amplitude)
    {
        var samples = new short[n];
        for (int i = 0; i < n; i++)
            samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * freq * i / sampleRate));
        return samples;
    }

    // Référence calculée en rejouant l'algorithme exact de apply_radio_effect
    // (app.py) en Python sur les mêmes 40 échantillons — voir historique de
    // session pour la commande utilisée.
    [Fact]
    public void Apply_MatchesPythonReferenceOutput()
    {
        var input = SineWave(40, 16000, 440, 3000);
        var expected = new short[]
        {
            0, 254, 592, 928, 1224, 1460, 1624, 1712, 1722, 1658, 1523, 1323, 1065, 760, 417, 50,
            -330, -712, -1082, -1427, -1739, -2005, -2218, -2370, -2457, -2475, -2423, -2303, -2118,
            -1873, -1574, -1231, -853, -452, -39, 372, 772, 1148, 1488, 1783,
        };

        var result = RadioEffect.Apply(input, 16000);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Apply_EmptyInputReturnsEmpty()
    {
        Assert.Empty(RadioEffect.Apply(Array.Empty<short>(), 16000));
    }

    [Fact]
    public void ApplyToWavFile_RoundTripsThroughDisk()
    {
        var path = Path.Combine(_dir, "voice.wav");
        var input = SineWave(200, 16000, 440, 3000);
        WavPcm16Mono.Write(path, input, 16000);

        RadioEffect.ApplyToWavFile(path);

        var result = WavPcm16Mono.Read(path);
        var expectedProcessed = RadioEffect.Apply(input, 16000);
        Assert.Equal(expectedProcessed, result.Samples);
        Assert.Equal(16000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.Equal(16, result.BitsPerSample);
    }

    [Fact]
    public void ApplyToWavFile_SilentlyIgnoresMissingFile()
    {
        // Ne doit pas lever d'exception, comme côté Python (fichier illisible -> retour silencieux).
        RadioEffect.ApplyToWavFile(Path.Combine(_dir, "does-not-exist.wav"));
    }
}
