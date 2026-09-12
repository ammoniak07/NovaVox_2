using NovaVox.Core.Config;
using NovaVox.Core.Gemini;
using Xunit;

namespace NovaVox.Core.Tests.Gemini;

public class GeminiQuotaTests
{
    [Fact]
    public void SyncState_ResetsCounterOnNewDay()
    {
        var config = new AiConfig { GeminiModel = "gemini-3.6-flash", GeminiRequestDay = "2000-01-01", GeminiRequestCount = 42 };
        var (used, limit) = GeminiQuota.SyncState(config);
        Assert.Equal(0, used);
        Assert.Equal(500, limit);
        Assert.Equal(0, config.GeminiRequestCount);
        Assert.Equal(GeminiQuota.TodayIso(), config.GeminiRequestDay);
    }

    [Fact]
    public void SyncState_KeepsCounterOnSameDay()
    {
        var config = new AiConfig { GeminiModel = "gemini-3.6-flash", GeminiRequestDay = GeminiQuota.TodayIso(), GeminiRequestCount = 7 };
        var (used, limit) = GeminiQuota.SyncState(config);
        Assert.Equal(7, used);
        Assert.Equal(500, limit);
    }

    [Fact]
    public void RecordRequest_IncrementsCounter()
    {
        var config = new AiConfig { GeminiModel = "gemini-3.5-flash-lite", GeminiRequestDay = GeminiQuota.TodayIso(), GeminiRequestCount = 10 };
        var (used, limit) = GeminiQuota.RecordRequest(config);
        Assert.Equal(11, used);
        Assert.Equal(1500, limit);
        Assert.Equal(11, config.GeminiRequestCount);
    }

    [Fact]
    public void DailyLimitFor_UnknownModelFallsBackToDefault()
    {
        Assert.Equal(500, NovaVox.Core.Gemini.GeminiModels.DailyLimitFor("some-future-model"));
    }
}
