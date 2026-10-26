using NovaVox.Core.Audio;
using NovaVox.Core.Config;
using Xunit;

namespace NovaVox.Core.Tests.Config;

public class OrbConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novavox_orb_" + Guid.NewGuid().ToString("N"));

    public OrbConfigTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var config = new OrbConfigStore(_dir).Load();
        Assert.False(config.Enabled);
        Assert.Equal(OrbConfig.DefaultSize, config.Size);
        Assert.Equal(OrbConfig.DefaultIdleColor, config.IdleColor);
        Assert.True(config.ShowCaption);
        Assert.False(config.ShowMouth);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new OrbConfigStore(_dir);
        store.Save(new OrbConfig { Enabled = true, X = 120, Y = -40, Size = 220, IdleColor = "#22c55e", ShowCaption = false, Opacity = 45, Roam = true, RoamSpeedMin = 300, RoamSpeedMax = 1000, HideAtRest = true, DepthEffect = false, Gags = false, SpatialSound = false, GagMinutes = 30, FarDistance = 3.5, ForbiddenZones = new() { new OrbZone(0.1, 0.2, 0.3, 0.4) }, RestSize = 50, ThinkingColor = "#123456", ListeningColor = "#abcdef", ShowMouth = true, ShowEars = true, ShowCore = false, ShowVoiceWave = false, ShowListenRings = false, ListenRingsInside = false, ShowThinkingFx = false, ShowReactions = false });

        var loaded = store.Load();
        Assert.True(loaded.Enabled);
        Assert.Equal(120, loaded.X);
        Assert.Equal(-40, loaded.Y);
        Assert.Equal(220, loaded.Size);
        Assert.Equal("#22c55e", loaded.IdleColor);
        Assert.False(loaded.ShowCaption);
        Assert.Equal(45, loaded.Opacity);
        Assert.True(loaded.Roam);
        Assert.Equal(300, loaded.RoamSpeedMin);
        Assert.Equal(1000, loaded.RoamSpeedMax);
        Assert.True(loaded.HideAtRest);
        Assert.False(loaded.DepthEffect);
        Assert.False(loaded.Gags);
        Assert.False(loaded.SpatialSound);
        Assert.Equal(30, loaded.GagMinutes);
        Assert.Equal(3.5, loaded.FarDistance);
        Assert.Equal(new OrbZone(0.1, 0.2, 0.3, 0.4), Assert.Single(loaded.ForbiddenZones));
        Assert.Equal(50, loaded.RestSize);
        Assert.Equal("#123456", loaded.ThinkingColor);
        Assert.Equal("#abcdef", loaded.ListeningColor);
        Assert.True(loaded.ShowMouth);
        Assert.True(loaded.ShowEars);
        Assert.False(loaded.ShowCore);
        Assert.False(loaded.ShowVoiceWave);
        Assert.False(loaded.ShowListenRings);
        Assert.False(loaded.ListenRingsInside);
        Assert.False(loaded.ShowThinkingFx);
        Assert.False(loaded.ShowReactions);
    }

    [Fact]
    public void Load_OpacityBelowMinimum_IsClamped()
    {
        File.WriteAllText(Path.Combine(_dir, OrbConfigStore.FileName), """{"opacity": 0}""");
        Assert.Equal(OrbConfig.MinOpacity, new OrbConfigStore(_dir).Load().Opacity);
    }

    [Fact]
    public void Load_InvalidValues_FallBack()
    {
        File.WriteAllText(Path.Combine(_dir, OrbConfigStore.FileName), """{"size": 5000, "idle_color": "bleu"}""");
        var loaded = new OrbConfigStore(_dir).Load();
        Assert.Equal(OrbConfig.MaxSize, loaded.Size);
        Assert.Equal(OrbConfig.DefaultIdleColor, loaded.IdleColor);
    }

    [Fact]
    public void Envelope_FollowsLoudness()
    {
        // 100 ms de silence puis 100 ms de son, 1 kHz mono.
        var pcm = new byte[400];
        for (var i = 100; i < 200; i++)
        {
            short s = (short)(i % 2 == 0 ? 8000 : -8000);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        var env = AudioEnvelope.Compute(pcm, sampleRate: 1000, channels: 1, frameMs: 50);

        Assert.Equal(4, env.Length);
        Assert.Equal(0f, env[0]);
        Assert.Equal(0f, env[1]);
        Assert.Equal(1.0, env[2], 3);
        Assert.Equal(1.0, env[3], 3);
    }

    [Fact]
    public void Envelope_Silence_IsAllZero() =>
        Assert.All(AudioEnvelope.Compute(new byte[2000], 1000, 1, 50), v => Assert.Equal(0f, v));
}
