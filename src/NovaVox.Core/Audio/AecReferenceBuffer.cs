namespace NovaVox.Core.Audio;

/// <summary>
/// Tampon circulaire thread-safe des derniers échantillons du signal de
/// référence (le son actuellement joué par le PC, capturé en "loopback"),
/// déjà ré-échantillonnés à la fréquence utilisée par la reconnaissance
/// vocale et réduits à un seul canal (mono). Port de AecReferenceBuffer
/// (aec.py).
/// </summary>
public sealed class AecReferenceBuffer
{
    private readonly int _maxSamples;
    private readonly object _lock = new();
    private double[] _buffer = Array.Empty<double>();

    public AecReferenceBuffer(int sampleRate, double maxSeconds = 3.0)
    {
        _maxSamples = Math.Max(1, (int)(maxSeconds * sampleRate));
    }

    public void Push(double[]? monoSamples)
    {
        if (monoSamples is null || monoSamples.Length == 0) return;
        lock (_lock)
        {
            var combined = new double[_buffer.Length + monoSamples.Length];
            Array.Copy(_buffer, combined, _buffer.Length);
            Array.Copy(monoSamples, 0, combined, _buffer.Length, monoSamples.Length);

            int excess = combined.Length - _maxSamples;
            _buffer = excess > 0 ? combined[excess..] : combined;
        }
    }

    /// <summary>
    /// Retourne les n derniers échantillons disponibles, ou null si le
    /// tampon n'en contient pas encore assez — l'appelant doit alors
    /// laisser passer le bloc micro tel quel plutôt que de risquer un
    /// mauvais alignement.
    /// </summary>
    public double[]? PullLatest(int n)
    {
        lock (_lock)
        {
            if (_buffer.Length < n) return null;
            return _buffer[^n..];
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer = Array.Empty<double>();
        }
    }
}
