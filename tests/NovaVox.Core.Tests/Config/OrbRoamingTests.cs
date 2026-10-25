using NovaVox.Core.Config;

namespace NovaVox.Core.Tests.Config;

public class OrbRoamingTests
{
    private static readonly Box Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void IsAllowed_RejectsOutsideScreenAndForbiddenZones()
    {
        var zone = new Box(800, 400, 300, 300);
        Assert.True(OrbRoaming.IsAllowed(new Box(100, 100, 200, 200), Screen, new[] { zone }));
        Assert.False(OrbRoaming.IsAllowed(new Box(1800, 100, 200, 200), Screen, new[] { zone }));
        Assert.False(OrbRoaming.IsAllowed(new Box(700, 300, 200, 200), Screen, new[] { zone }));
    }

    [Fact]
    public void IsPathClear_DetectsZoneInTheWay()
    {
        var wall = new Box(900, 0, 100, 1080);
        var from = new Box(100, 400, 200, 200);
        Assert.False(OrbRoaming.IsPathClear(from, from.MovedTo(1500, 400), Screen, new[] { wall }));
        Assert.True(OrbRoaming.IsPathClear(from, from.MovedTo(500, 800), Screen, new[] { wall }));
    }

    [Fact]
    public void PickTarget_StaysAllowedAndFarEnough()
    {
        var zones = new[] { new Box(0, 0, 1920, 300), new Box(600, 500, 700, 580) };
        var current = new Box(100, 400, 200, 200);
        var random = new Random(42);
        for (var i = 0; i < 50; i++)
        {
            var pick = OrbRoaming.PickTarget(current, Screen, zones, random, minDistance: 150);
            Assert.NotNull(pick);
            Assert.True(OrbRoaming.IsAllowed(pick!.Value.Target, Screen, zones));
            if (!pick.Value.Teleport) Assert.True(OrbRoaming.IsPathClear(current, pick.Value.Target, Screen, zones));
        }
    }

    [Fact]
    public void PickTarget_WallForcesTeleport()
    {
        var wall = new Box(900, 0, 100, 1080);
        var current = new Box(100, 400, 200, 200);
        var random = new Random(1);
        // Côté gauche trop petit pour un déplacement de 900 px : seule la droite convient, derrière le mur.
        var pick = OrbRoaming.PickTarget(current, Screen, new[] { wall }, random, minDistance: 900, attempts: 500);
        Assert.NotNull(pick);
        Assert.True(pick!.Value.Teleport);
    }

    [Fact]
    public void PickTarget_NullWhenNoRoom()
    {
        var everything = new Box(0, 0, 1920, 1080);
        Assert.Null(OrbRoaming.PickTarget(new Box(0, 0, 100, 100), Screen, new[] { everything }, new Random(3), 10));
    }

    [Fact]
    public void Zone_RoundTripsThroughScreen()
    {
        var screen = new Box(1920, 0, 2560, 1440);
        var zone = OrbZone.FromScreen(new Box(1920 + 640, 720, -640, 360), screen);
        Assert.NotNull(zone);
        Assert.Equal(0.0, zone!.X, 3);
        Assert.Equal(0.5, zone.Y, 3);
        Assert.Equal(0.25, zone.Width, 3);
        var back = zone.ToScreen(screen);
        Assert.Equal(1920, back.X, 3);
        Assert.Equal(640, back.Width, 3);
        Assert.Null(OrbZone.FromScreen(new Box(10, 10, 2, 2), screen));
    }
}
