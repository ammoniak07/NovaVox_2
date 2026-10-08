namespace NovaVox.Core.GameLog;

public readonly record struct TimeInterval(DateTimeOffset Start, DateTimeOffset End);

public static class StatsLiveIntervals
{
    public static void Add(List<TimeInterval> intervals, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) return;
        if (intervals.Count > 0 && intervals[^1].End == start)
            intervals[^1] = intervals[^1] with { End = end };
        else
            intervals.Add(new TimeInterval(start, end));
    }

    public static bool Contains(IReadOnlyList<TimeInterval>? intervals, DateTimeOffset ts) =>
        intervals is not null && intervals.Any(i => i.Start <= ts && ts <= i.End);
}
