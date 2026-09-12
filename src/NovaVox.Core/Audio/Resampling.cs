namespace NovaVox.Core.Audio;

public static class Resampling
{
    /// <summary>
    /// Ré-échantillonnage simple par interpolation linéaire (port de
    /// resample_linear dans aec.py). Pas de filtrage anti-repliement :
    /// suffisant puisque le signal de référence ne sert qu'à alimenter le
    /// filtre d'annulation d'écho, pas à être écouté directement.
    /// </summary>
    public static double[] ResampleLinear(double[] samples, int origSr, int targetSr)
    {
        int n = samples.Length;
        if (n == 0 || origSr == targetSr) return samples;

        double duration = n / (double)origSr;
        int targetLen = Math.Max(1, (int)Math.Round(duration * targetSr));
        var result = new double[targetLen];

        for (int i = 0; i < targetLen; i++)
        {
            double pos = targetLen == 1 ? 0.0 : (double)i * (n - 1) / (targetLen - 1);
            int idx0 = (int)Math.Floor(pos);
            int idx1 = Math.Min(idx0 + 1, n - 1);
            double frac = pos - idx0;
            result[i] = samples[idx0] + (samples[idx1] - samples[idx0]) * frac;
        }
        return result;
    }
}
