using NovaVox.Core.Speech;

namespace NovaVox.Core.Tests.Speech;

public class OrbGagsTests
{
    [Fact]
    public void Sounds_AreShortAndAudible()
    {
        var random = new Random(3);
        foreach (var sound in new[] { OrbGags.Fart(random), OrbGags.Snore(random) })
        {
            Assert.InRange(sound.Length, OrbGags.SampleRate / 3, OrbGags.SampleRate * 2);
            var peak = sound.Max(s => Math.Abs((int)s));
            Assert.InRange(peak, 1000, short.MaxValue); // audible mais pas saturé en permanence
        }
    }

    [Fact]
    public void PickGag_SnoresWhenAsleepAndNeverRepeats()
    {
        var random = new Random(1);
        Assert.Equal(OrbGag.Snore, OrbGags.PickGag(random, asleep: true, last: null));
        OrbGag? last = null;
        for (var i = 0; i < 100; i++)
        {
            var gag = OrbGags.PickGag(random, asleep: false, last);
            Assert.NotEqual(last, gag);
            Assert.NotEqual(OrbGag.Snore, gag);
            last = gag;
        }
    }

    [Fact]
    public void Lines_AreNotEmpty()
    {
        foreach (var lines in new[] { OrbGags.FartLines, OrbGags.SneezeLines, OrbGags.YawnLines, OrbGags.StarCitizenLines })
            Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
    }
}
