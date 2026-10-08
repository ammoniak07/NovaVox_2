using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class StatsLiveIntervalsTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-20T18:00:00Z");

    [Fact]
    public void Add_ContiguousIntervals_AreMergedIntoOne()
    {
        var intervals = new List<TimeInterval>();

        StatsLiveIntervals.Add(intervals, T0, T0.AddMinutes(1));
        StatsLiveIntervals.Add(intervals, T0.AddMinutes(1), T0.AddMinutes(2));

        Assert.Equal(new[] { new TimeInterval(T0, T0.AddMinutes(2)) }, intervals);
    }

    [Fact]
    public void Add_GapBetweenIntervals_KeepsThemSeparate()
    {
        var intervals = new List<TimeInterval>();

        StatsLiveIntervals.Add(intervals, T0, T0.AddMinutes(1));
        StatsLiveIntervals.Add(intervals, T0.AddHours(2), T0.AddHours(3));

        Assert.Equal(2, intervals.Count);
        Assert.True(StatsLiveIntervals.Contains(intervals, T0.AddMinutes(1)));
        Assert.False(StatsLiveIntervals.Contains(intervals, T0.AddHours(1)));
    }
}
