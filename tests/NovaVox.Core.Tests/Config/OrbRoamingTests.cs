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
            if (!pick.Value.CrossesZone) Assert.True(OrbRoaming.IsPathClear(current, pick.Value.Target, Screen, zones));
        }
    }

    [Fact]
    public void PickTarget_WallIsCrossedWhenNoOtherWay()
    {
        var wall = new Box(900, 0, 100, 1080);
        var current = new Box(100, 400, 200, 200);
        var random = new Random(1);
        // Côté gauche trop petit pour un déplacement de 900 px : seule la droite convient, derrière le mur.
        var pick = OrbRoaming.PickTarget(current, Screen, new[] { wall }, random, minDistance: 900, attempts: 500);
        Assert.NotNull(pick);
        Assert.True(pick!.Value.CrossesZone);
    }

    [Fact]
    public void PickTarget_ExploreGoesToTheOtherSideOfAWall()
    {
        // Mur au milieu : en exploration, elle va de l'autre côté (loin), en traversant.
        var wall = new Box(860, 0, 200, 1080);
        var current = new Box(50, 440, 200, 200);
        var random = new Random(7);
        var otherSide = 0;
        for (var i = 0; i < 40; i++)
        {
            var pick = OrbRoaming.PickTarget(current, Screen, new[] { wall }, random, minDistance: 300, explore: true);
            Assert.NotNull(pick);
            Assert.True(OrbRoaming.IsAllowed(pick!.Value.Target, Screen, new[] { wall }));
            if (pick.Value.Target.X > 1060)
            {
                otherSide++;
                Assert.True(pick.Value.CrossesZone);
            }
        }
        Assert.True(otherSide > 30);
    }

    [Fact]
    public void PickTarget_NoCrossingWhenNotAllowed()
    {
        var wall = new Box(900, 0, 100, 1080);
        var current = new Box(100, 400, 200, 200);
        Assert.Null(OrbRoaming.PickTarget(current, Screen, new[] { wall }, new Random(1), minDistance: 900, attempts: 500, allowCrossing: false));
    }

    [Fact]
    public void PickSideChange_GoesAroundThroughTheOppositeEdges()
    {
        // Mur central sur toute la hauteur : pour passer à droite, elle sort à gauche et rentre par la droite.
        var wall = new Box(860, 0, 200, 1080);
        var current = new Box(300, 440, 200, 200);
        var random = new Random(5);
        for (var i = 0; i < 20; i++)
        {
            var plan = OrbRoaming.PickSideChange(current, Screen, new[] { wall }, random, minDistance: 300, margin: 50);
            Assert.NotNull(plan);
            var target = plan!.Target;
            var exit = plan.Exit;
            var entry = plan.Entry;
            Assert.True(OrbRoaming.IsAllowed(target, Screen, new[] { wall }));
            Assert.Empty(plan.Waypoints); // mur sur toute la hauteur : impossible de passer dessus/dessous
            if (target.X > 1060)
            {
                Assert.NotNull(exit);
                Assert.True(exit!.Value.Right <= 0);
                Assert.Equal(current.Y, exit.Value.Y, 3);
                Assert.True(entry!.Value.X >= 1920);
                Assert.Equal(target.Y, entry.Value.Y, 3);
            }
        }
    }

    [Fact]
    public void PickSideChange_DirectWhenNothingInTheWay()
    {
        var plan = OrbRoaming.PickSideChange(new Box(100, 100, 200, 200), Screen, Array.Empty<Box>(), new Random(2), minDistance: 300, margin: 50);
        Assert.NotNull(plan);
        Assert.Null(plan!.Exit);
        Assert.Null(plan.Entry);
        Assert.Empty(plan.Waypoints);
    }

    [Fact]
    public void PickSideChange_PassesAboveOrBelowShrinkingIfNeeded()
    {
        // Zone au centre avec 150 px libres en haut et en bas : trop étroit pour une bulle de 200 px,
        // elle passe en rétrécissant, ou fait le tour par les côtés.
        var zone = new Box(760, 150, 400, 780);
        var current = new Box(200, 440, 200, 200);
        var random = new Random(11);
        var corridors = 0;
        for (var i = 0; i < 30; i++)
        {
            var plan = OrbRoaming.PickSideChange(current, Screen, new[] { zone }, random, minDistance: 1000, margin: 50);
            Assert.NotNull(plan);
            if (plan!.Waypoints.Count == 0) continue;
            corridors++;
            Assert.Equal(2, plan.Waypoints.Count);
            Assert.True(plan.Scale < 0.75 && plan.Scale >= 0.3);
            // Les coins sont le long du haut ou du bas de l'écran.
            var cy = plan.Waypoints[0].CenterY;
            Assert.True(cy < 150 || cy > 930);
        }
        Assert.True(corridors > 10);
    }

    [Fact]
    public void PickTarget_NullWhenNoRoom()
    {
        var everything = new Box(0, 0, 1920, 1080);
        Assert.Null(OrbRoaming.PickTarget(new Box(0, 0, 100, 100), Screen, new[] { everything }, new Random(3), 10));
    }

    [Fact]
    public void EntryStart_ComesFromNearestFreeEdge()
    {
        var target = new Box(100, 400, 200, 200);
        var start = OrbRoaming.EntryStart(target, Screen, Array.Empty<Box>(), margin: 50);
        Assert.Equal(-250, start.X, 3);
        Assert.Equal(400, start.Y, 3);

        // Zone collée au bord gauche : on arrive par un autre bord, sans la traverser.
        var leftZone = new Box(0, 300, 90, 400);
        var other = OrbRoaming.EntryStart(target, Screen, new[] { leftZone }, margin: 50);
        Assert.False(OrbRoaming.CrossesZones(other, target, new[] { leftZone }));
        Assert.False(Screen.Intersects(other));
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
