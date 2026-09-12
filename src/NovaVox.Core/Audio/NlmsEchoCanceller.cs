namespace NovaVox.Core.Audio;

/// <summary>
/// Filtre adaptatif NLMS mono-canal (port de NLMSEchoCanceller, aec.py).
///
/// <paramref name="filterLength"/> (via le constructeur) : nombre de
/// coefficients du filtre, c'est-à-dire la durée maximale de "mémoire" du
/// trajet écho qu'il peut apprendre à prédire (ex. 1600 échantillons à
/// 16 kHz = 100 ms). <c>mu</c> : pas d'adaptation (0 &lt; mu &lt;= 1). Plus
/// élevé = apprentissage plus rapide mais plus instable ; plus bas = plus
/// lent mais plus stable.
/// </summary>
public sealed class NlmsEchoCanceller
{
    public int FilterLength { get; }
    private readonly double _mu;
    private readonly double _eps;
    private double[] _weights;

    // Les (FilterLength - 1) derniers échantillons de référence du bloc
    // précédent, pour que le tout premier échantillon d'un nouveau bloc
    // dispose déjà de son historique complet.
    private double[] _refTail;

    public NlmsEchoCanceller(int filterLength = 1600, double mu = 0.4, double eps = 1e-6)
    {
        FilterLength = filterLength;
        _mu = mu;
        _eps = eps;
        _weights = new double[FilterLength];
        _refTail = new double[Math.Max(0, FilterLength - 1)];
    }

    public void Reset()
    {
        _weights = new double[FilterLength];
        _refTail = new double[Math.Max(0, FilterLength - 1)];
    }

    /// <summary>
    /// micBlock/refBlock : mêmes longueur, alignés dans le temps. Retourne
    /// le signal micro débarrassé de l'écho estimé — même longueur, même
    /// échelle.
    /// </summary>
    public double[] ProcessBlock(double[] micBlock, double[] refBlock)
    {
        int l = FilterLength;
        int n = micBlock.Length;
        if (n == 0 || refBlock.Length != n) return micBlock;

        var extendedRef = new double[_refTail.Length + n];
        Array.Copy(_refTail, extendedRef, _refTail.Length);
        Array.Copy(refBlock, 0, extendedRef, _refTail.Length, n);

        var output = new double[n];
        var w = _weights;
        for (int i = 0; i < n; i++)
        {
            double echoEstimate = 0.0;
            for (int k = 0; k < l; k++) echoEstimate += w[k] * extendedRef[i + k];

            double err = micBlock[i] - echoEstimate;
            output[i] = err;

            double normSq = _eps;
            for (int k = 0; k < l; k++) normSq += extendedRef[i + k] * extendedRef[i + k];

            double factor = _mu * err / normSq;
            for (int k = 0; k < l; k++) w[k] += factor * extendedRef[i + k];
        }

        _weights = w;
        _refTail = l > 1 ? extendedRef[^(l - 1)..] : Array.Empty<double>();
        return output;
    }
}
