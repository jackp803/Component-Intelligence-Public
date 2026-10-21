using System.Diagnostics;
using System.Text.Json;
using ComponentIntelligence.Electrical.Schematic;
using Xunit.Abstractions;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicRoutingPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Reserved130SegmentFallbackCompletesWithinOneSecond()
    {
        var page = Page();
        var reserved = FallbackReservations(128);
        SchematicOrthogonalRoute.Between(new(20, 20), new(40, 20), page, [], reserved);
        var timer = Stopwatch.StartNew();
        var route = SchematicOrthogonalRoute.Between(new(30, 30), new(380, 240), page, [], reserved);
        timer.Stop();
        output.WriteLine($"130 reserved segments: {timer.Elapsed.TotalMilliseconds:F3} ms, {route.Count} points");
        AssertSafe(route, new(30, 30), new(380, 240), page, reserved);
        Assert.Equal(560, route.Zip(route.Skip(1)).Sum(e => Math.Abs(e.First.X - e.Second.X) + Math.Abs(e.First.Y - e.Second.Y)), 6);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"130-segment fallback took {timer.Elapsed.TotalMilliseconds:F3} ms; budget is 1000 ms.");
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 1.99, false)]
    [InlineData(false, 2, true)]
    [InlineData(false, 2.01, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, -1.99, false)]
    [InlineData(true, -2, true)]
    [InlineData(true, -2.01, true)]
    public void ParallelReservationClearanceRetainsStrictTwoMillimetreBoundary(bool vertical, double offset, bool straight)
    {
        var start = vertical ? new SchematicPoint(50, 20) : new(20, 50);
        var end = vertical ? new SchematicPoint(50, 180) : new(180, 50);
        var reserved = new[] { vertical ? Wire("reserved", new(50 + offset, 60), new(50 + offset, 140))
            : Wire("reserved", new(60, 50 + offset), new(140, 50 + offset)) };
        var route = SchematicOrthogonalRoute.Between(start, end, Page(), [], reserved);
        Assert.Equal(straight, route.Count == 2);
        AssertSafe(route, start, end, Page(), reserved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProperInteriorCrossingRemainsAvailable(bool vertical)
    {
        var start = vertical ? new SchematicPoint(100, 20) : new(20, 100);
        var end = vertical ? new SchematicPoint(100, 180) : new(180, 100);
        var reserved = new[] { vertical ? Wire("reserved", new(20, 100), new(180, 100))
            : Wire("reserved", new(100, 20), new(100, 180)) };
        var route = SchematicOrthogonalRoute.Between(start, end, Page(), [], reserved);
        Assert.Equal(new[] { start, end }, route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedPerpendicularEndpointTouchIsStillAvoided(bool vertical)
    {
        var start = vertical ? new SchematicPoint(100, 20) : new(20, 100);
        var end = vertical ? new SchematicPoint(100, 180) : new(180, 100);
        var reserved = new[] { vertical ? Wire("reserved", new(100, 100), new(160, 100))
            : Wire("reserved", new(100, 100), new(100, 160)) };
        var route = SchematicOrthogonalRoute.Between(start, end, Page(), [], reserved);
        Assert.True(route.Count > 2);
        AssertSafe(route, start, end, Page(), reserved);
    }

    [Fact]
    public void QuantizedFourMillimetreCandidatesRemainFirstChoice()
    {
        var route = SchematicOrthogonalRoute.Between(new(20, 40), new(190, 140), Page(), []);
        Assert.Equal(new SchematicPoint[] { new(20, 40), new(104, 40), new(104, 140), new(190, 140) }, route);
    }

    [Fact]
    public void FallbackKeepsReversedOverlappingAndFractionalReservationsWithoutMutatingInput()
    {
        var reserved = FallbackReservations(16);
        reserved.Add(Wire("reverse", new(400, 30), new(15, 30)));
        reserved.Add(Wire("vertical", new(110.25, 160), new(110.25, 50)));
        reserved.Add(Wire("near-vertical", new(112.24, 60), new(112.24, 150)));
        var before = JsonSerializer.Serialize(reserved);
        var body = new SchematicGridBounds(170, 70, 30, 80);
        var route = SchematicOrthogonalRoute.Between(new(30, 30), new(380, 240), Page(), [body], reserved);
        AssertSafe(route, new(30, 30), new(380, 240), Page(), reserved);
        foreach (var (a, b) in route.Zip(route.Skip(1)))
        {
            var blocked = a.Y == b.Y
                ? a.Y > 68 && a.Y < 152 && Math.Min(a.X, b.X) < 202 && Math.Max(a.X, b.X) > 168
                : a.X > 168 && a.X < 202 && Math.Min(a.Y, b.Y) < 152 && Math.Max(a.Y, b.Y) > 68;
            Assert.False(blocked, $"Route crosses expanded body: {a} -> {b}");
        }
        Assert.Equal(before, JsonSerializer.Serialize(reserved));
    }

    [Fact]
    public void CompletedSameSidePortRouteCannotRetraceItsOwnedOutwardStubs()
    {
        var start = new SchematicPoint(68, 50);
        var end = new SchematicPoint(168, 50);
        var stubs = new[] { Wire("start-stub", new(60, 50), start), Wire("end-stub", end, new(160, 50)) };
        var bodies = new SchematicGridBounds[] { new(30, 30, 30, 40), new(130, 30, 30, 40) };
        var route = BetweenWithStubs(start, end, bodies, [], stubs);
        AssertSafe(route, start, end, Page(), []);
        AssertAvoidsStubRetracing(route, stubs);
        AssertNoRetracing(new[] { new SchematicPoint(60, 50) }.Concat(route).Append(new(160, 50)).ToArray());
    }

    [Fact]
    public void DraftRouteCannotReverseOverItsOwnedStartStub()
    {
        var start = new SchematicPoint(68, 50);
        var end = new SchematicPoint(20, 100);
        var stubs = new[] { Wire("start-stub", new(60, 50), start) };
        var route = BetweenWithStubs(start, end, [new(30, 30, 30, 40)], [], stubs);
        AssertSafe(route, start, end, Page(), []);
        AssertAvoidsStubRetracing(route, stubs);
        AssertNoRetracing(new[] { new SchematicPoint(60, 50) }.Concat(route).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedStubJoinsAreLegalButUnrelatedReservedEndpointsStillBlock(bool unrelated)
    {
        var start = new SchematicPoint(68, 50);
        var end = new SchematicPoint(68, 150);
        var stubs = new[] { Wire("start-stub", new(60, 50), start), Wire("end-stub", end, new(60, 150)) };
        var reserved = unrelated ? new[] { Wire("unrelated", new(68, 100), new(100, 100)) } : [];
        var before = JsonSerializer.Serialize(new { stubs, reserved });
        var route = BetweenWithStubs(start, end, [], reserved, stubs);
        Assert.Equal(!unrelated, route.Count == 2);
        AssertSafe(route, start, end, Page(), reserved);
        AssertAvoidsStubRetracing(route, stubs);
        Assert.Equal(before, JsonSerializer.Serialize(new { stubs, reserved }));
    }

    [Fact]
    public void PathologicalImportedGridFailsFastWithActionableErrorWithoutMutation()
    {
        var reserved = FallbackReservations(0);
        for (var i = 0; i < 500; i++)
            reserved.Add(Wire("dense-" + i, new(40 + i * .22, 50 + i * .12), new(43.4 + i * .22, 50 + i * .12)));
        var before = JsonSerializer.Serialize(reserved);
        var timer = Stopwatch.StartNew();
        var error = Assert.Throws<InvalidOperationException>(() =>
            SchematicOrthogonalRoute.Between(new(30, 30), new(380, 240), Page(), [], reserved));
        timer.Stop();
        Assert.Contains("grid", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("split", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Extreme-grid rejection took {timer.Elapsed.TotalMilliseconds:F3} ms.");
        Assert.Equal(before, JsonSerializer.Serialize(reserved));
    }

    private static List<SchematicPoint> BetweenWithStubs(SchematicPoint start, SchematicPoint end,
        IReadOnlyList<SchematicGridBounds> bodies, IReadOnlyList<SchematicWire> reserved, IReadOnlyList<SchematicWire> stubs)
        => SchematicOrthogonalRoute.Between(start, end, Page(), bodies, reserved, stubs);

    private static void AssertAvoidsStubRetracing(IReadOnlyList<SchematicPoint> route, IReadOnlyList<SchematicWire> stubs)
    {
        foreach (var (a, b) in route.Zip(route.Skip(1)))
        foreach (var (c, d) in stubs.SelectMany(w => w.Points.Zip(w.Points.Skip(1))))
        {
            if (a.Y == b.Y && c.Y == d.Y)
                Assert.False(Math.Abs(a.Y - c.Y) < 2 && Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) < Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)));
            else if (a.X == b.X && c.X == d.X)
                Assert.False(Math.Abs(a.X - c.X) < 2 && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) < Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
        }
    }

    private static void AssertNoRetracing(IReadOnlyList<SchematicPoint> points)
    {
        for (var i = 1; i < points.Count - 1; i++)
        {
            var a = points[i - 1]; var b = points[i]; var c = points[i + 1];
            Assert.False(a.X == b.X && b.X == c.X && (b.Y - a.Y) * (c.Y - b.Y) < 0 ||
                a.Y == b.Y && b.Y == c.Y && (b.X - a.X) * (c.X - b.X) < 0);
        }
    }

    private static List<SchematicWire> FallbackReservations(int smallSegments)
    {
        var reserved = new List<SchematicWire> { Wire("start-block", new(15, 30), new(400, 30)),
            Wire("end-block", new(15, 240), new(400, 240)) };
        for (var i = 0; i < smallSegments; i++)
        {
            var x = 40 + i * 2.2;
            var y = 50 + i * 1.2;
            reserved.Add(Wire("r-" + i, new(x, y), new(x + 3.4, y)));
        }
        return reserved;
    }

    private static SchematicPage Page() => new() { PageId = "review", Title = "Synthetic", Width = 420, Height = 297, Margin = 10 };
    private static SchematicWire Wire(string id, SchematicPoint start, SchematicPoint end) =>
        new() { WireId = id, PageId = "review", Points = [start, end] };

    private static void AssertSafe(IReadOnlyList<SchematicPoint> route, SchematicPoint start, SchematicPoint end,
        SchematicPage page, IReadOnlyList<SchematicWire> reserved)
    {
        Assert.Equal(start, route[0]);
        Assert.Equal(end, route[^1]);
        Assert.All(route, p => {
            Assert.InRange(p.X, page.Margin, page.Width - page.Margin);
            Assert.InRange(p.Y, page.Margin, page.Height - page.Margin);
        });
        foreach (var (a, b) in route.Zip(route.Skip(1)))
        {
            Assert.NotEqual(a, b);
            Assert.True(a.X == b.X || a.Y == b.Y);
            foreach (var (c, d) in reserved.SelectMany(w => w.Points.Zip(w.Points.Skip(1))))
            {
                var horizontal = a.Y == b.Y;
                var reservedHorizontal = c.Y == d.Y;
                if (horizontal == reservedHorizontal)
                {
                    var distance = horizontal ? Math.Abs(a.Y - c.Y) : Math.Abs(a.X - c.X);
                    var low = horizontal ? Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) : Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                    var high = horizontal ? Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) : Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                    Assert.False(distance < 2 && low < high, $"Parallel reservation violated: {a} -> {b}, {c} -> {d}");
                }
                else
                {
                    var reservedTouch = horizontal
                        ? (a.Y == c.Y || a.Y == d.Y) && c.X >= Math.Min(a.X, b.X) && c.X <= Math.Max(a.X, b.X)
                        : (a.X == c.X || a.X == d.X) && c.Y >= Math.Min(a.Y, b.Y) && c.Y <= Math.Max(a.Y, b.Y);
                    Assert.False(reservedTouch, $"Reserved endpoint touched: {a} -> {b}, {c} -> {d}");
                }
            }
        }
    }
}
