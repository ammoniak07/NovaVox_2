using NovaVox.Core.Audio;
using Xunit;

namespace NovaVox.Core.Tests.Audio;

public class AecTests
{
    // Valeurs de référence calculées avec aec.py (NLMSEchoCanceller) pour
    // garantir la parité de comportement bit-à-bit avec l'implémentation
    // Python (voir historique de session pour la commande utilisée).
    [Fact]
    public void ProcessBlock_MatchesPythonReferenceOutput()
    {
        var nlms = new NlmsEchoCanceller(filterLength: 3, mu: 0.4, eps: 1e-6);

        var out1 = nlms.ProcessBlock(new double[] { 1.0, 2.0, 3.0 }, new double[] { 1.0, 2.0, 3.0 });
        AssertClose(new[] { 1.0, 1.2000007999992, 1.0320008415993833 }, out1);

        var out2 = nlms.ProcessBlock(new double[] { 4.0, 5.0, 6.0 }, new double[] { 4.0, 5.0, 6.0 });
        AssertClose(new[] { 0.7542863826934632, 0.49402214054188676, 0.2904374642556684 }, out2);
    }

    [Fact]
    public void ProcessBlock_ReturnsInputUnchangedOnLengthMismatch()
    {
        var nlms = new NlmsEchoCanceller(filterLength: 4);
        var mic = new double[] { 1.0, 2.0 };
        var result = nlms.ProcessBlock(mic, new double[] { 1.0, 2.0, 3.0 });
        Assert.Same(mic, result);
    }

    [Fact]
    public void ProcessBlock_ConvergesToReduceIdenticalEcho()
    {
        // Écho parfait (mic == ref, sans délai) : l'erreur résiduelle doit
        // diminuer nettement au fil des blocs successifs.
        var nlms = new NlmsEchoCanceller(filterLength: 32, mu: 0.4);
        var rng = new Random(42);
        double firstBlockEnergy = 0, lastBlockEnergy = 0;
        for (int block = 0; block < 50; block++)
        {
            var signal = new double[64];
            for (int i = 0; i < signal.Length; i++) signal[i] = rng.NextDouble() * 2 - 1;
            var output = nlms.ProcessBlock(signal, signal);
            var energy = output.Sum(v => v * v);
            if (block == 0) firstBlockEnergy = energy;
            if (block == 49) lastBlockEnergy = energy;
        }
        Assert.True(lastBlockEnergy < firstBlockEnergy * 0.05);
    }

    [Fact]
    public void ReferenceBuffer_ReturnsNullUntilEnoughSamples()
    {
        var buffer = new AecReferenceBuffer(sampleRate: 16000, maxSeconds: 1.0);
        buffer.Push(new double[] { 1.0, 2.0, 3.0 });
        Assert.Null(buffer.PullLatest(5));
        Assert.Equal(new[] { 2.0, 3.0 }, buffer.PullLatest(2));
    }

    [Fact]
    public void ReferenceBuffer_DropsOldestSamplesBeyondCapacity()
    {
        var buffer = new AecReferenceBuffer(sampleRate: 4, maxSeconds: 1.0); // capacité = 4 échantillons
        buffer.Push(new double[] { 1, 2, 3, 4 });
        buffer.Push(new double[] { 5, 6 });
        Assert.Equal(new double[] { 3, 4, 5, 6 }, buffer.PullLatest(4));
    }

    [Fact]
    public void ResampleLinear_MatchesPythonReference()
    {
        var result = Resampling.ResampleLinear(new double[] { 0.0, 10.0, 20.0, 30.0 }, origSr: 4, targetSr: 2);
        Assert.Equal(new[] { 0.0, 30.0 }, result);
    }

    [Fact]
    public void ResampleLinear_NoOpWhenSameRate()
    {
        var samples = new double[] { 1.0, 2.0, 3.0 };
        Assert.Same(samples, Resampling.ResampleLinear(samples, 16000, 16000));
    }

    private static void AssertClose(double[] expected, double[] actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) < tolerance, $"index {i}: {expected[i]} vs {actual[i]}");
    }
}
