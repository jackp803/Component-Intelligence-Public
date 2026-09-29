using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPortEditorTests
{
    private readonly SchematicAuthoringService _service = new();

    [Theory]
    [InlineData("Top", 0, 25)]
    [InlineData("Bottom", 30, 25)]
    [InlineData("Left", 15, 0)]
    [InlineData("Right", 15, 50)]
    public void MovesWholePortToSelectedEdgeWithoutChangingPinOrWireIdentity(string side, double y, double x)
    {
        var project = Project();
        var symbol = project.Schematic!.Symbols.Single();
        var pin = symbol.Anchors[0];
        var start = SchematicAuthoringService.AnchorPoint(symbol, pin.EndpointId);
        project = _service.DrawWire(project, symbol.PageId, SchematicAttachment.Pin(symbol.SymbolId, pin.EndpointId),
            SchematicAttachment.Free(), [start, new(start.X + 20, start.Y)]);
        var before = JsonSerializer.Serialize(project);
        var originalWireId = project.Schematic!.Wires.Single().WireId;

        var moved = _service.MovePortToEdge(project, symbol.SymbolId, "P", side,
            side is "Left" or "Right" ? y : x);
        var movedSymbol = moved.Schematic!.Symbols.Single();
        Assert.Equal(2, movedSymbol.Anchors.Count);
        Assert.All(movedSymbol.Anchors, a =>
        {
            Assert.Equal(side, a.Direction);
            Assert.False(a.Confirmed);
            Assert.Null(a.CadContactId);
            Assert.Equal("src-port", a.SourcePortId);
        });
        var wire = Assert.Single(moved.Schematic.Wires);
        Assert.Equal(originalWireId, wire.WireId);
        Assert.Equal(pin.EndpointId, wire.Start.EndpointId);
        Assert.Equal(SchematicAuthoringService.AnchorPoint(movedSymbol, pin.EndpointId), wire.Points[0]);
        Assert.Empty(moved.Connections);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        SchematicAuthoringService.Validate(moved);
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(moved))!;
        Assert.Equal(movedSymbol.Anchors.Select(a => a.Position), reloaded.Schematic!.Symbols.Single().Anchors.Select(a => a.Position));
    }

    [Fact]
    public void CollapseIsVisualOnlyAndLockedWirePreventsPortMove()
    {
        var project = Project();
        var symbol = project.Schematic!.Symbols.Single();
        var anchor = symbol.Anchors[0];
        var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
        project = _service.DrawWire(project, symbol.PageId, SchematicAttachment.Pin(symbol.SymbolId, anchor.EndpointId),
            SchematicAttachment.Free(), [point, new(point.X + 20, point.Y)]);
        var before = JsonSerializer.Serialize(project);
        var collapsed = _service.TogglePortCollapsed(project, symbol.SymbolId, "P");
        Assert.Contains("P", collapsed.Schematic!.Symbols.Single().CollapsedPortIds);
        Assert.Contains("P", JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(collapsed))!
            .Schematic!.Symbols.Single().CollapsedPortIds);
        var owner = SchematicSymbolOwner.Resolve(collapsed, collapsed.Schematic.Symbols.Single());
        Assert.False(SchematicPortPresentation.IsCollapsedPin(collapsed.Schematic, collapsed.Schematic.Symbols.Single(), owner, anchor));
        Assert.True(SchematicPortPresentation.IsCollapsedPin(collapsed.Schematic, collapsed.Schematic.Symbols.Single(), owner, symbol.Anchors[1]));
        Assert.Equal(JsonSerializer.Serialize(project.Schematic!.Wires.Single()),
            JsonSerializer.Serialize(collapsed.Schematic.Wires.Single()));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var expanded = _service.TogglePortCollapsed(collapsed, symbol.SymbolId, "P");
        Assert.Empty(expanded.Schematic!.Symbols.Single().CollapsedPortIds);
        var locked = _service.SetLocked(project, project.Schematic.Wires.Single().WireId, true);
        Assert.Throws<InvalidOperationException>(() => _service.MovePortToEdge(locked, symbol.SymbolId, "P", "Top", 25));
        Assert.Equal(point, SchematicAuthoringService.AnchorPoint(locked.Schematic!.Symbols.Single(), anchor.EndpointId));
    }

    [Theory]
    [InlineData(0, 125, 80, "Top")]
    [InlineData(0, 150, 95, "Right")]
    [InlineData(90, 115, 80, "Left")]
    [InlineData(90, 100, 105, "Bottom")]
    [InlineData(180, 150, 95, "Left")]
    [InlineData(270, 100, 105, "Top")]
    [InlineData(270, 130, 105, "Bottom")]
    public void DropTargetUsesDisplayedEdgesAfterRotation(int rotation, double x, double y, string expectedLocalSide)
    {
        var symbol = Project().Schematic!.Symbols.Single() with
        { Position = new(100, 80), Rotation = rotation, Width = 50, Height = 30 };
        var target = SchematicPortPresentation.DropTarget(symbol, new(x, y));
        Assert.Equal(expectedLocalSide, target.Side);
        Assert.InRange(target.Coordinate, 0, expectedLocalSide is "Left" or "Right" ? 30 : 50);
    }

    private static ElectricalProject Project()
    {
        var component = new ComponentInstance { ComponentInstanceId = "C", ComponentDefinitionId = "module", TypeKey = "MODULE",
            Ports = [new ComponentPort { PortId = "P", SourcePortId = "src-port", Name = "Power",
                Pins = [new ComponentPin { PinId = "A", SourcePinId = "src-a", PinNumber = "1" },
                    new ComponentPin { PinId = "B", SourcePinId = "src-b", PinNumber = "2" }] }] };
        return new ElectricalProject { ProjectId = "test", Components = [component], Schematic = new SchematicDocument
        {
            Pages = [new SchematicPage { PageId = "page", Title = "First" }],
            Symbols = [SchematicAuthoringService.CreateSymbol(component, "page", new(40, 40))]
        } };
    }
}
