namespace NovaVox.Core.Audio;

/// <summary>
/// Effet léger de "communication radio/vaisseau" (filtre passe-bande façon
/// petit haut-parleur + écho court + saturation douce) — port de
/// apply_radio_effect (app.py), opérant ici sur des échantillons PCM 16
/// bits mono déjà en mémoire plutôt que directement sur un fichier .wav
/// (voir <see cref="WavPcm16Mono"/> pour la lecture/écriture).
/// </summary>
public static class RadioEffect
{
    public static short[] Apply(short[] samples, int sampleRate)
    {
        int n = samples.Length;
        if (n == 0) return samples;

        // 1) Passe-haut à un pôle : enlève le grave, effet "petit haut-parleur".
        const double alphaHp = 0.90;
        double prevIn = 0.0, prevOut = 0.0;
        var band = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = samples[i];
            double y = alphaHp * (prevOut + x - prevIn);
            band[i] = y;
            prevIn = x;
            prevOut = y;
        }

        // 2) Passe-bas à un pôle : adoucit l'aigu laissé trop strident par le passe-haut.
        const double alphaLp = 0.55;
        double prev = 0.0;
        for (int i = 0; i < n; i++)
        {
            prev += alphaLp * (band[i] - prev);
            band[i] = prev;
        }

        // 3) Écho court façon "canal de communication", puis légère saturation
        //    pour un grain radio, avec saturation aux bornes int16.
        int delaySamples = Math.Max(1, (int)(sampleRate * 0.018)); // ~18 ms
        const double feedback = 0.16;
        var output = new short[n];
        for (int i = 0; i < n; i++)
        {
            double v = band[i];
            if (i >= delaySamples) v += feedback * band[i - delaySamples];

            if (v > 9000) v = 9000 + (v - 9000) * 0.3;
            else if (v < -9000) v = -9000 + (v + 9000) * 0.3;

            if (v > short.MaxValue) v = short.MaxValue;
            else if (v < short.MinValue) v = short.MinValue;

            output[i] = (short)v;
        }
        return output;
    }

    /// <summary>
    /// Applique l'effet directement sur un fichier .wav — port intégral de
    /// apply_radio_effect(wav_path) : ne traite que le cas PCM 16 bits
    /// mono (le seul produit par Piper), reste silencieux sur tout fichier
    /// illisible ou d'un autre format plutôt que de planter.
    /// </summary>
    public static void ApplyToWavFile(string wavPath)
    {
        WavPcmData data;
        try
        {
            data = WavPcm16Mono.Read(wavPath);
        }
        catch
        {
            return;
        }

        if (data.BitsPerSample != 16 || data.Channels != 1) return;

        var processed = Apply(data.Samples, data.SampleRate);

        try
        {
            WavPcm16Mono.Write(wavPath, processed, data.SampleRate);
        }
        catch
        {
            // Best effort, comme côté Python.
        }
    }
}
