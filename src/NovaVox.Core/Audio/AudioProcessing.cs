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
