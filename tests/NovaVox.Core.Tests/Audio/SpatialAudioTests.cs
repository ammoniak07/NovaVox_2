using NovaVox.Core.Audio;

namespace NovaVox.Core.Tests.Audio;

public class SpatialAudioTests
{
    [Fact]
    public void Pan_FollowsScreenPosition()
    {
        Assert.Equal(0, SpatialAudio.Pan(960, 0, 1920), 3);
        Assert.Equal(-0.85, SpatialAudio.Pan(0, 0, 1920), 3);
        Assert.Equal(0.85, SpatialAudio.Pan(1920, 0, 1920), 3);
        Assert.Equal(0.425, SpatialAudio.Pan(1920 + 1440, 1920, 1920), 3); // second écran
        Assert.Equal(0.85, SpatialAudio.Pan(99999, 0, 1920), 3);
    }

    [Fact]
    public void PanGains_ConstantPower()
    {
        foreach (var pan in new[] { -1.0, -0.5, 0, 0.3, 1 })
        {
            var (l, r) = SpatialAudio.PanGains(pan);
            Assert.Equal(1, l * l + r * r, 6);
        }
        var (left, right) = SpatialAudio.PanGains(-0.85);
        Assert.True(left > right);
    }

    [Fact]
    public void Gain_AndLowPass_DecreaseWithDistance()
    {
        Assert.Equal(1, SpatialAudio.Gain(1), 3);
        Assert.True(SpatialAudio.Gain(0.5) < 1 && SpatialAudio.Gain(0.25) >= 0.5);
        Assert.Equal(1, SpatialAudio.LowPassAlpha(1), 3);
        Assert.True(SpatialAudio.LowPassAlpha(0.25) >= 0.55);
    }
}
