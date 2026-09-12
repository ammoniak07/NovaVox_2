using NovaVox.Core.Audio;
using Xunit;

namespace NovaVox.Core.Tests.Audio;

public class AudioProcessingTests
{
    private static byte[] Pack(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(samples[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return bytes;
    }

    private static short[] Unpack(byte[] bytes)
    {
        var samples = new short[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        return samples;
    }

    // Valeurs de référence calculées avec audioop.rms/audioop.mul (voir
    // historique de session) pour garantir la parité avec app.py.
    [Fact]
    public void ComputeRms_MatchesAudioopReference()
    {
        var data = Pack(1000, -2000, 32000, -32000, 500);
        Assert.Equal(20264, AudioProcessing.ComputeRms(data));
    }

    [Theory]
    [InlineData(1.5, new short[] { 1500, -3000, 32767, -32768, 750 })]
    [InlineData(2.5, new short[] { 2500, -5000, 32767, -32768, 1250 })]
    public void ApplyMicGain_MatchesAudioopReferenceWithSaturation(double gain, short[] expected)
    {
        var data = Pack(1000, -2000, 32000, -32000, 500);
        var result = AudioProcessing.ApplyMicGain(data, gain);
        Assert.Equal(expected, Unpack(result));
    }

    [Fact]
    public void ApplyMicGain_NoOpWhenGainIsOne()
    {
        var data = Pack(1000, -2000);
        Assert.Same(data, AudioProcessing.ApplyMicGain(data, 1.0));
    }

    [Fact]
    public void ComputeRms_ZeroForEmptyData()
    {
        Assert.Equal(0, AudioProcessing.ComputeRms(Array.Empty<byte>()));
    }
}
