using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EditableWagoGroupTests
{
    private readonly SchematicAuthoringService _service = new();
    private ElectricalProject Fixture() => _service.GroupWagoSymbols(ElectricalProjectMigrator.ForPortFanout(WagoVisualGroupTests.Fixture()), ["w1", "w2", "w3"]);

    [Fact]
    public void ResizeChangesEveryRowButPreservesOwnersAndConnections()
    {
        var p = Fixture(); var before = JsonSerializer.Serialize(p); var bounds = SchematicWagoGroups.Build(p.Schematic!).Single().Bounds;
        var next = _service.ResizeGenericSymbol(p, "w2", 40, 26);
        Assert.All(next.Schematic!.Symbols, s => { Assert.Equal(40, s.Width); Assert.Equal(26, s.Height); });
        var group = SchematicWagoGroups.Build(next.Schematic).Single();
        Assert.Equal(bounds.X, group.Bounds.X); Assert.Equal(bounds.Y, group.Bounds.Y);
        Assert.All(group.Members.Zip(group.Members.Skip(1)), pair => Assert.Equal(26, pair.Second.Position.Y - pair.First.Position.Y));
        Assert.Equal(JsonSerializer.Serialize(p.Components), JsonSerializer.Serialize(next.Components));
        Assert.Equal(JsonSerializer.Serialize(p.Connections), JsonSerializer.Serialize(next.Connections));
        Assert.Equal(JsonSerializer.Serialize(p.BomItems), JsonSerializer.Serialize(next.BomItems));
        Assert.Equal(p.Schematic!.Wires.Select(w => (w.WireId, w.Start, w.End, w.ConnectionId)), next.Schematic.Wires.Select(w => (w.WireId, w.Start, w.End, w.ConnectionId)));
        foreach (var wire in next.Schematic.Wires)
            Assert.Equal(SchematicAuthoringService.AnchorPoint(next.Schematic.Symbols.Single(s => s.SymbolId == wire.Start.SymbolId), wire.Start.EndpointId!), wire.Points[0]);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void EachGroupedRowCanExpandAndRetainItsOwnFanoutAfterResizeAndDissolve()
    {
        var p = Fixture(); var other = JsonSerializer.Serialize(p.Schematic!.Symbols[2]); var wire = JsonSerializer.Serialize(p.Schematic.Wires[2]);
        var expanded = _service.SetPortFanout(p, "w1", "port1", true, 10, 10);
        expanded = _service.SetPortFanout(expanded, "w2", "port2", true, 10, 15);
        Assert.Equal(other, JsonSerializer.Serialize(expanded.Schematic!.Symbols[2]));
        Assert.Equal(wire, JsonSerializer.Serialize(expanded.Schematic.Wires[2]));
        var resized = _service.ResizeGenericSymbol(expanded, "w1", 36, 24);
        foreach (var s in resized.Schematic!.Symbols.Take(2))
        foreach (var placement in s.EndpointDisplayPlacements)
        {
            var a = s.Anchors.Single(a => a.EndpointId == placement.EndpointId);
            Assert.Equal(s.SymbolId == "w1" ? 10 : 15, Math.Abs(placement.Position.X - a.Position.X));
            foreach (var attached in resized.Schematic.Wires.Where(w => w.Start.EndpointId == placement.EndpointId))
                Assert.Equal(SchematicAuthoringService.AnchorPoint(s, placement.EndpointId), attached.Points[0]);
        }
        var dissolved = _service.DissolveWagoGroup(resized, "w2");
        Assert.Equal(JsonSerializer.Serialize(resized.Schematic.Wires), JsonSerializer.Serialize(dissolved.Schematic!.Wires));
        Assert.All(dissolved.Schematic.Symbols, s => Assert.Null(s.WagoVisualGroupId));
        Assert.DoesNotContain("WagoHeaderHeight", JsonSerializer.Serialize(dissolved));
    }

    [Fact]
    public void LegacyGroupKeepsNativePlacementUntilExplicitResize()
    {
        var p = Fixture(); var json = JsonSerializer.SerializeToNode(p)!;
        foreach (var s in json["Schematic"]!["Symbols"]!.AsArray()) s!.AsObject().Remove("WagoHeaderHeight");
        var legacy = json.Deserialize<ElectricalProject>()!; var before = JsonSerializer.Serialize(legacy);
        SchematicAuthoringService.Validate(legacy);
        var oldBounds = SchematicWagoGroups.Build(legacy.Schematic!).Single().Bounds;
        var next = _service.ResizeGenericSymbol(legacy, "w1", 32, 22);
        Assert.Equal(oldBounds.Y, SchematicWagoGroups.Build(next.Schematic!).Single().Bounds.Y);
        Assert.Equal(before, JsonSerializer.Serialize(legacy));
        SchematicAuthoringService.Validate(JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!);
    }

    [Theory]
    [InlineData("wire-locked")] [InlineData("member-locked")] [InlineData("nan")] [InlineData("overflow")]
    public void UnsafeResizeIsAtomic(string kind)
    {
        var p = Fixture();
        if (kind == "wire-locked") p.Schematic!.Wires[0] = p.Schematic.Wires[0] with { Locked = true };
        if (kind == "member-locked") p.Schematic!.Symbols[2] = p.Schematic.Symbols[2] with { Locked = true };
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.ResizeGenericSymbol(p, "w1", kind == "nan" ? double.NaN : 36, kind == "overflow" ? 200 : 26));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void AdjacentRowsCannotExpandIndependentPinsOntoSamePoint()
    {
        var p = ElectricalProjectMigrator.ForPortFanout(WagoVisualGroupTests.Fixture()); p.Schematic!.Wires.Clear();
        for (var i = 0; i < 2; i++) p.Schematic.Symbols[i] = p.Schematic.Symbols[i] with {
            Anchors = p.Schematic.Symbols[i].Anchors.Select((a, n) => a with { Direction = "Right", Position = new(50, 6 + 6 * n) }).ToList() };
        p = _service.GroupWagoSymbols(p, ["w1", "w2"]);
        p = _service.SetPortFanout(p, "w1", "port1", true, 18, 10);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.SetPortFanout(p, "w2", "port2", true, 18, 10));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Theory]
    [InlineData(0, 10)] [InlineData(10, 0)] [InlineData(-1, 10)] [InlineData(double.PositiveInfinity, 10)]
    public void InvalidCatalogImageDimensionsAreRejected(double width, double height)
    {
        var group = SchematicWagoGroups.Build(Fixture().Schematic!).Single();
        Assert.Throws<InvalidOperationException>(() => SchematicWagoGroups.ImageBounds(group, width, height));
    }

    private ElectricalProject SameSideRows()
    {
        var p = ElectricalProjectMigrator.ForPortFanout(WagoVisualGroupTests.Fixture());
        p.Schematic!.Wires.Clear();
        p.Schematic.Symbols.RemoveAt(2);
        for (var i = 0; i < 2; i++) p.Schematic.Symbols[i] = p.Schematic.Symbols[i] with {
            Anchors = p.Schematic.Symbols[i].Anchors.Select((a, n) => a with {
                Direction = "Right", Position = new(50, 6 + 6 * n) }).ToList() };
        return _service.GroupWagoSymbols(p, ["w1", "w2"]);
    }

    [Fact]
    public void GroupResizePreservesConfiguredFanoutPitch()
    {
        var p = _service.SetPortFanout(SameSideRows(), "w1", "port1", true, 10, 10);
        var resized = _service.ResizeGenericSymbol(p, "w1", 36, 42);
        var pins = resized.Schematic!.Symbols.Single(s => s.SymbolId == "w1").EndpointDisplayPlacements;
        Assert.Equal(10, Math.Abs(pins[1].Position.Y - pins[0].Position.Y));
    }

    [Fact]
    public void IndependentRowsCannotOverlapFanoutLeadSegments()
    {
        var p = _service.SetPortFanout(SameSideRows(), "w1", "port1", true, 60, 10);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.SetPortFanout(p, "w2", "port2", true, 60, 10));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void FanoutCannotMergeWithAnotherRowsExistingManualRoute()
    {
        var p = SameSideRows();
        var other = p.Schematic!.Symbols.Single(s => s.SymbolId == "w2");
        var first = p.Schematic.Symbols.Single(s => s.SymbolId == "w1");
        var endpoint = SchematicAuthoringService.AnchorPoint(other, "p21");
        var leadRow = first.Position.Y + first.Anchors.Average(a => a.Position.Y) - 5;
        var right = first.Position.X + first.Width;
        p.Schematic.Wires.Add(new() { WireId = "manual-neighbor", PageId = first.PageId,
            Start = SchematicAttachment.Pin("w2", "p21"), End = SchematicAttachment.Free(), ManualRoute = true,
            Points = [endpoint, new(right + 14, endpoint.Y), new(right + 14, leadRow), new(right + 2, leadRow)] });
        SchematicAuthoringService.Validate(p);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.SetPortFanout(p, "w1", "port1", true, 10, 10));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }
}
