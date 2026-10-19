using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class WagoVisualGroupRegressionTests
{
    private readonly SchematicAuthoringService _service = new();
    private static ElectricalProject Fixture()
    {
        var p = WagoVisualGroupTests.Fixture(); p.Schematic!.Wires.Clear();
        for (var i = 0; i < p.Schematic.Symbols.Count; i++)
            p.Schematic.Symbols[i] = p.Schematic.Symbols[i] with { Position = i < 2 ? new(100, 60 + i * 50) : new(300, 180),
                Anchors = p.Schematic.Symbols[i].Anchors.Select(a => a with { Position = a.Position with { Y = 9 } }).ToList() };
        return p;
    }
    private ElectricalProject Group(ElectricalProject p) => _service.GroupWagoSymbols(p, ["w1", "w2"]);
    private static SchematicPoint Pin(ElectricalProject p, string id, string pin) => SchematicAuthoringService.AnchorPoint(p.Schematic!.Symbols.Single(s => s.SymbolId == id), pin);
    private static bool Interior(SchematicPoint a, SchematicPoint b, SchematicGridBounds r) =>
        a.Y == b.Y && a.Y > r.Y && a.Y < r.Y + r.Height && Math.Min(a.X, b.X) < r.X + r.Width && Math.Max(a.X, b.X) > r.X ||
        a.X == b.X && a.X > r.X && a.X < r.X + r.Width && Math.Min(a.Y, b.Y) < r.Y + r.Height && Math.Max(a.Y, b.Y) > r.Y;

    [Fact]
    public void VerticalInputPinsCannotCoincideAcrossRows()
    {
        var p = Fixture();
        for (var i = 0; i < 2; i++) p.Schematic!.Symbols[i] = p.Schematic.Symbols[i] with {
            Anchors = p.Schematic.Symbols[i].Anchors.Select((a, n) => a with { Direction = n == 0 ? "Top" : "Bottom", Position = new(25, n * 30) }).ToList() };
        var next = Group(p); var rows = next.Schematic!.Symbols.Take(2).ToArray();
        var pins = rows.SelectMany(s => s.Anchors.Select(a => SchematicAuthoringService.AnchorPoint(s, a.EndpointId))).ToArray();
        Assert.Equal(4, pins.Distinct().Count());
        Assert.All(rows.SelectMany(s => s.Anchors), a => Assert.Contains(a.Direction, new[] { "Left", "Right" }));
    }

    [Fact]
    public void BothEndsOfManualWireTranslateTogetherWithoutCollapsing()
    {
        var p = Group(Fixture()); var a = Pin(p, "w1", "p12"); var b = Pin(p, "w2", "p22");
        p = _service.DrawWire(p, "page", SchematicAttachment.Pin("w1", "p12"), SchematicAttachment.Pin("w2", "p22"), [a, b], true);
        Assert.NotEmpty(p.Connections); var before = JsonSerializer.Serialize(p.Connections);
        var next = _service.TransformSymbol(p, "w1", new(100, 78), 0);
        Assert.Equal(new[] { new SchematicPoint(a.X, a.Y + 18), new(b.X, b.Y + 18) }, next.Schematic!.Wires.Single().Points);
        Assert.Equal(before, JsonSerializer.Serialize(next.Connections));
    }

    [Fact]
    public void BranchedRouteKeepsOutwardExitAndItsJunctionWhenGroupMoves()
    {
        var p = Group(Fixture()); var start = Pin(p, "w1", "p12");
        p = _service.DrawWire(p, "page", SchematicAttachment.Pin("w1", "p12"), SchematicAttachment.Free(),
            [start, new(180, start.Y), new(180, 130), new(300, 130)]);
        var parentId = p.Schematic!.Wires.Single().WireId;
        p = _service.DrawWire(p, "page", SchematicAttachment.Junction(parentId), SchematicAttachment.Free(), [new(260, 130), new(260, 160)]);
        var next = _service.TransformSymbol(p, "w1", new(200, 60), 0);
        var parent = next.Schematic!.Wires.Single(w => w.WireId == parentId); var bounds = SchematicWagoGroups.Build(next.Schematic).Single().Bounds;
        Assert.True(parent.Points[1].X > parent.Points[0].X);
        Assert.DoesNotContain(parent.Points.Zip(parent.Points.Skip(1)), e => Interior(e.First, e.Second, bounds));
        Assert.Equal(new SchematicPoint(260, 130), next.Schematic.Wires.Last().Points[0]);
        Assert.Equal(p.Schematic.Wires.Last().Start, next.Schematic.Wires.Last().Start);
    }

    [Theory]
    [InlineData("symbol")] [InlineData("wire")] [InlineData("fanout")] [InlineData("boundary-wire")]
    public void LaterMutationsCannotCoverTheGroupHeader(string kind)
    {
        var p = Group(Fixture()); var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => kind switch {
            "symbol" => _service.TransformSymbol(p, "w3", new(101, 51), 0),
            "wire" => _service.DrawWire(p, "page", SchematicAttachment.Free(), SchematicAttachment.Free(), [new(70, 55), new(160, 55)]),
            "boundary-wire" => _service.DrawWire(p, "page", SchematicAttachment.Free(), SchematicAttachment.Free(), [new(100, 55), new(100, 90)]),
            _ => _service.SetPortFanout(_service.TransformSymbol(p, "w3", new(200, 46), 0), "w3", "port3", true, 10, 110)
        });
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void CreationChecksUnrelatedFanout()
    {
        var p = Fixture(); p = _service.TransformSymbol(p, "w3", new(200, 46), 0);
        p = _service.SetPortFanout(p, "w3", "port3", true, 10, 110);
        Assert.Throws<InvalidOperationException>(() => Group(p));
    }

    [Fact]
    public void PortRouteAvoidsHeader()
    {
        var p = Fixture(); p = _service.TogglePortCollapsed(p, "w1", "port1");
        var symbol = p.Schematic!.Symbols[0]; var point = SchematicPortPresentation.ConnectionPoint(p, symbol, "port1");
        p = _service.DrawWire(p, "page", SchematicAttachment.Port("w1", "port1"), SchematicAttachment.Free(), [point, new(250, point.Y)]);
        var next = Group(p); var bounds = SchematicWagoGroups.Build(next.Schematic!).Single().Bounds;
        Assert.DoesNotContain(next.Schematic!.Wires.Single().Points.Zip(next.Schematic.Wires.Single().Points.Skip(1)), e => Interior(e.First, e.Second, bounds));
    }
}
