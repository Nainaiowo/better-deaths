using System.Numerics;

namespace BetterDeaths;

public sealed class ReplayGeometryTests
{
    [Fact]
    public void WorldProjectionDoesNotPinOutsidePointsToArenaEdge()
    {
        var point = ReplayGeometry.WorldPointToScreen(
            0.0f,
            0.0f,
            Vector2.Zero,
            new Vector2(400.0f),
            80.0f,
            120.0f,
            80.0f,
            120.0f,
            1.0f,
            Vector2.Zero,
            30.0f);

        Assert.True(point.X < 0.0f);
        Assert.True(point.Y < 0.0f);
    }

    [Fact]
    public void ConeUsesCurvedArenaClippedArc()
    {
        var arena = new ReplayArenaInfo(100.0f, 100.0f, 20.0f, ReplayArenaShape.Circle);
        var arc = ReplayGeometry.BuildConeArc(
            100.0f,
            100.0f,
            MathF.PI * 0.5f,
            40.0f,
            90.0f,
            80.0f,
            120.0f,
            80.0f,
            120.0f,
            arena);

        Assert.True(arc.Count > 3);
        Assert.All(arc, point => Assert.InRange(Vector2.Distance(point, new Vector2(100.0f)), 19.99f, 20.01f));
        Assert.True(arc[arc.Count / 2].X > arc[0].X);
        Assert.True(arc[arc.Count / 2].X > arc[^1].X);
    }

    [Fact]
    public void ConeRayIsShortenedAlongItsDirectionAtSquareBoundary()
    {
        var arena = new ReplayArenaInfo(100.0f, 100.0f, 20.0f, ReplayArenaShape.Square);
        var arc = ReplayGeometry.BuildConeArc(
            110.0f,
            100.0f,
            MathF.PI * 0.5f,
            40.0f,
            10.0f,
            80.0f,
            120.0f,
            80.0f,
            120.0f,
            arena);

        Assert.All(arc, point =>
        {
            Assert.InRange(point.X, 80.0f, 120.0f);
            Assert.InRange(point.Y, 80.0f, 120.0f);
        });
        Assert.InRange(arc[arc.Count / 2].X, 119.99f, 120.01f);
    }

    [Fact]
    public void ConeStartingOutsideArenaKeepsItsRequestedReach()
    {
        foreach (var shape in new[] { ReplayArenaShape.Circle, ReplayArenaShape.Square })
        {
            var arena = new ReplayArenaInfo(100.0f, 100.0f, 20.0f, shape);
            var arc = ReplayGeometry.BuildConeArc(
                130.0f,
                100.0f,
                MathF.PI * 0.5f,
                10.0f,
                10.0f,
                80.0f,
                120.0f,
                80.0f,
                120.0f,
                arena);

            Assert.InRange(Vector2.Distance(arc[arc.Count / 2], new Vector2(130.0f, 100.0f)), 9.99f, 10.01f);
        }
    }
}
