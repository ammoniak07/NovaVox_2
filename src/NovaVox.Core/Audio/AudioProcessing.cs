namespace NovaVox.Core.Audio;

/// <summary>
/// Traitement du flux micro PCM 16 bits mono — port de apply_mic_gain/
/// compute_rms (app.py). Python utilisait `audioop` (implémentation C)
/// avec repli manuel ; .NET n'a pas d'équivalent à préférer, donc
/// l'implémentation directe ci-dessous est la seule voie.
/// </summary>
public static class AudioProcessing
{
    /// <summary>
    /// Multiplie l'amplitude d'un bloc audio PCM 16 bits mono par
    /// <paramref name="gain"/> (1.0 = inchangé), avec saturation aux
    /// bornes int16 pour éviter tout dépassement/retournement de signe.
    /// </summary>
    public static byte[] ApplyMicGain(byte[] data, double gain)
    {
        if (gain == 1.0 || data.Length == 0) return data;

        var result = new byte[data.Length];
        int sampleCount = data.Length / 2;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            double scaled = sample * gain;
            short clamped = scaled switch
            {
                > short.MaxValue => short.MaxValue,
                < short.MinValue => short.MinValue,
                _ => (short)scaled,
            };
            result[i * 2] = (byte)(clamped & 0xFF);
            result[i * 2 + 1] = (byte)((clamped >> 8) & 0xFF);
        }
        return result;
    }

    /// <summary>Calcule le niveau RMS (énergie) d'un bloc audio PCM 16 bits mono.</summary>
    public static long ComputeRms(byte[] data)
    {
        if (data.Length < 2) return 0;
        int sampleCount = data.Length / 2;
        double sumSquares = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            sumSquares += (double)sample * sample;
        }
        return (long)Math.Sqrt(sumSquares / sampleCount);
    }
}

/// <summary>
/// Enveloppe sonore d'un clip PCM 16 bits : un niveau entre 0 et 1 par
/// tranche de <c>frameMs</c> millisecondes, pour animer la bulle au rythme
/// de la voix pendant la lecture (voir OrbWindow).
/// </summary>
public static class AudioEnvelope
{
    public static float[] Compute(byte[] pcm16, int sampleRate, int channels, int frameMs = 30)
    {
        if (pcm16.Length < 2 || sampleRate <= 0 || channels <= 0 || frameMs <= 0) return Array.Empty<float>();
        var samplesPerFrame = Math.Max(1, sampleRate * channels * frameMs / 1000);
        var totalSamples = pcm16.Length / 2;
        var frameCount = (totalSamples + samplesPerFrame - 1) / samplesPerFrame;
        var levels = new float[frameCount];
        var peak = 0.0;
        for (var f = 0; f < frameCount; f++)
        {
            var start = f * samplesPerFrame;
            var end = Math.Min(totalSamples, start + samplesPerFrame);
            double sum = 0;
            for (var i = start; i < end; i++)
            {
                short s = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
                sum += (double)s * s;
            }
            var rms = Math.Sqrt(sum / Math.Max(1, end - start));
            levels[f] = (float)rms;
            if (rms > peak) peak = rms;
        }
        // Normalisé sur le pic du clip : une voix basse ou forte anime la
        // bulle autant ; un clip silencieux reste à 0.
        if (peak < 1) return new float[frameCount];
        for (var f = 0; f < frameCount; f++) levels[f] = (float)Math.Clamp(levels[f] / peak, 0, 1);
        return levels;
    }
}
