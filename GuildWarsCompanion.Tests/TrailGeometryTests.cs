using GuildWarsCompanion.Services;

using Xunit;
using Xunit.Abstractions;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Whether the drawn trail joins up points that are actually next to each other.
///
/// The symptom to catch is a tail that renders as a fan of long spokes rather than a line: if two
/// consecutive points are a stride apart in the world, the segment between them on the dial has
/// to be short too. A long one means either the points are coming out in the wrong order or the
/// radial scale is stretching a stride into half the dial.
/// </summary>
public class TrailGeometryTests(ITestOutputHelper output)
{
    /// <summary>The same wandering path the simulator walks, so the numbers mean something.</summary>
    private static (double X, double Y, double Facing) Walk(double seconds)
    {
        const double Ratio = 1.6180339887;
        const double Radius = 500;
        const double Lap = 120;

        var t = seconds / Lap * Math.Tau;

        var x = 44416 + Radius * Math.Cos(t);
        var y = 29248 + Radius * Math.Sin(t * Ratio);

        var dx = -Radius * Math.Sin(t);
        var dy = Radius * Ratio * Math.Cos(t * Ratio);

        return (x, y, Math.Atan2(dx, -dy));
    }

    [Fact]
    public void Consecutive_points_stay_close_on_the_dial()
    {
        var path = new WalkedPath();

        // Ten minutes at the bridge's ten frames a second, so the buffer is well past full.
        for (var i = 0; i < 6000; i++)
        {
            var (x, y, _) = Walk(i / 10.0);

            path.Add(15, x, y);
        }

        var (nowX, nowY, facing) = Walk(600);

        var points = path.Points();

        Assert.Equal(WalkedPath.Capacity, points.Count);

        // Project exactly as CompassViewModel does, then measure each drawn segment as a
        // fraction of the dial's radius.
        var placed = points
            .Select(p => (
                Angle: Bearing.Relative(nowX, nowY, facing, p.X, p.Y),
                Radius: RadarScale.Fraction(Bearing.Distance(nowX, nowY, p.X, p.Y))))
            .Select(p => (X: p.Radius * Math.Sin(p.Angle), Y: -p.Radius * Math.Cos(p.Angle)))
            .ToList();

        var longest = 0.0;
        var worst = -1;

        for (var i = 1; i < placed.Count; i++)
        {
            var step = Math.Sqrt(
                Math.Pow(placed[i].X - placed[i - 1].X, 2) +
                Math.Pow(placed[i].Y - placed[i - 1].Y, 2));

            if (step <= longest) continue;

            longest = step;
            worst = i;
        }

        var world = Bearing.Distance(
            points[worst - 1].X, points[worst - 1].Y, points[worst].X, points[worst].Y);

        output.WriteLine($"longest segment: {longest:0.000} of the radius, at index {worst}");
        output.WriteLine($"  those two points are {world:0.0} units apart in the world");
        output.WriteLine($"  distances from the character: " +
                         $"{Bearing.Distance(nowX, nowY, points[worst - 1].X, points[worst - 1].Y):0} " +
                         $"and {Bearing.Distance(nowX, nowY, points[worst].X, points[worst].Y):0}");

        // A stride is about 12 units. Nothing that short should cross a quarter of the dial.
        Assert.True(longest < 0.25, $"a single stride drew {longest:0.00} of the dial's radius");
    }
}
