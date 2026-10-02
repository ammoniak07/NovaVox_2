using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class ShipTimeTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Process_Entered_SetsCurrentShipAndReturnsNull()
    {
        var tracker = new ShipTimeTracker();

        var closed = tracker.Process(T0, "Drake Cutter", entered: true);

        Assert.Null(closed);
        Assert.Equal("Drake Cutter", tracker.CurrentShip);
    }

    [Fact]
    public void Process_LeftAfterEntering_ReturnsElapsedSeconds()
    {
        var tracker = new ShipTimeTracker();
        tracker.Process(T0, "Drake Cutter", entered: true);

        var closed = tracker.Process(T0.AddSeconds(90), "Drake Cutter", entered: false);

        Assert.Equal(("Drake Cutter", 90.0), closed);
        Assert.Null(tracker.CurrentShip);
    }

    [Fact]
    public void Process_EnteredDifferentShipWithoutLeaving_ClosesPreviousIntervalAndSwitches()
    {
        var tracker = new ShipTimeTracker();
        tracker.Process(T0, "Drake Cutter", entered: true);

        var closed = tracker.Process(T0.AddSeconds(60), "RSI Constellation Taurus", entered: true);

        Assert.Equal(("Drake Cutter", 60.0), closed);
        Assert.Equal("RSI Constellation Taurus", tracker.CurrentShip);
    }

    [Fact]
    public void Process_LeftWithoutHavingEntered_ReturnsNull()
    {
        var tracker = new ShipTimeTracker();

        var closed = tracker.Process(T0, "Drake Cutter", entered: false);

        Assert.Null(closed);
        Assert.Null(tracker.CurrentShip);
    }

    [Fact]
    public void Flush_NoCurrentShip_ReturnsNull()
    {
        var tracker = new ShipTimeTracker();

        Assert.Null(tracker.Flush(T0));
    }

    [Fact]
    public void Flush_CreditsElapsedSoFarWithoutClearingCurrentShip()
    {
        var tracker = new ShipTimeTracker();
        tracker.Process(T0, "Drake Cutter", entered: true);

        var closed = tracker.Flush(T0.AddSeconds(30));

        Assert.Equal(("Drake Cutter", 30.0), closed);
        Assert.Equal("Drake Cutter", tracker.CurrentShip); // toujours dans le même vaisseau, juste un nouveau point de départ
    }

    [Fact]
    public void Flush_CalledTwice_NeverCreditsTheSameIntervalTwice()
    {
        var tracker = new ShipTimeTracker();
        tracker.Process(T0, "Drake Cutter", entered: true);

        tracker.Flush(T0.AddSeconds(30));
        var secondFlush = tracker.Flush(T0.AddSeconds(30)); // même horodatage : rien de nouveau à créditer

        Assert.Null(secondFlush);
    }

    [Fact]
    public void Process_SameTimestampAsAnchor_ReturnsNullRatherThanZeroSecondsEntry()
    {
        var tracker = new ShipTimeTracker();
        tracker.Process(T0, "Drake Cutter", entered: true);

        var closed = tracker.Process(T0, "Drake Cutter", entered: false);

        Assert.Null(closed);
    }
}
