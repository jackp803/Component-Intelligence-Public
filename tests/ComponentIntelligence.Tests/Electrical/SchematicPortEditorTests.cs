using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPortEditorTests
{
    private readonly SchematicAuthoringService _service = new();

    [Fact]
    public void NewlyPlacedGenericModuleStartsWithItsPortsCollapsed()
    {
        var project = Project();
        var symbol = SchematicAuthoringService.CreateSymbol(project.Components.Single(), "page", new(40, 40));
        Assert.Equal(["P"], symbol.CollapsedPortIds);
        Assert.Equal(2, symbol.Anchors.Count);
    }

    [Fact]
    public void TopEdgeGrowsAndKeepsCollapsedPortContactsDistinct()
    {
        var project = Project();
        var component = project.Components.Single();
        for (var i = 1; i <= 5; i++)
            component.Ports.Add(new ComponentPort { PortId = $"ETH{i}", Name = $"ETH{i}",
                Pins = [new ComponentPin { PinId = $"ETH{i}-1", PinNumber = "1" }] });
        var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(40, 40));
        project.Schematic!.Symbols[0] = symbol;
        for (var i = 1; i <= 5; i++)
            project = _service.MovePortToEdge(project, symbol.SymbolId, $"ETH{i}", "Top", 10 + i * 5);
        symbol = project.Schematic!.Symbols.Single();
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var bounds = SchematicPortPresentation.GenericBodyBounds(project.Schematic, symbol, owner);
        var contacts = component.Ports.Skip(1)
            .Select(port => SchematicPortPresentation.GroupContact(symbol, owner, port, bounds)).ToArray();
        Assert.True(bounds.Width > 50);
        Assert.True(symbol.Width > 50);
        var topGroups = component.Ports.Skip(1).Select(port => symbol.Anchors.Single(a => a.EndpointId == port.Pins[0].PinId).Position.X).Order().ToArray();
        Assert.All(topGroups.Zip(topGroups.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 12));
        Assert.All(contacts, c => Assert.Equal("Top", c.Side));
        Assert.Equal(contacts.Length, contacts.Select(c => c.Position).Distinct().Count());
        var sortedContacts = contacts.OrderBy(c => c.Position.X).ToArray();
        Assert.All(sortedContacts.Zip(sortedContacts.Skip(1)), pair => Assert.True(pair.Second.Position.X - pair.First.Position.X >= 12));
        Assert.Equal(component.Ports.SelectMany(p => p.Pins).Count(), symbol.Anchors.Count);
        SchematicAuthoringService.Validate(project);
    }

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

    [Theory]
    [InlineData(0, "Right")]
    [InlineData(90, "Bottom")]
    public void CollapsedGenericPortsBecomeDistinctSingleVisualContactsAndCompactBody(int rotation, string side)
    {
        var project = Project();
        var component = project.Components.Single();
        component.Ports.AddRange(Enumerable.Range(1, 5).Select(i => new ComponentPort
        {
            PortId = "ETH" + i, Name = "ETH" + i,
            Pins = [new ComponentPin { PinId = "ETH" + i + "-1", PinNumber = "1" },
                new ComponentPin { PinId = "ETH" + i + "-2", PinNumber = "2" }]
        }));
        var symbol = project.Schematic!.Symbols.Single();
        project.Schematic.Symbols[0] = symbol with { Height = 140, Rotation = rotation,
            Anchors = symbol.Anchors.Concat(component.Ports.Skip(1).SelectMany((port, i) => port.Pins.Select((pin, j) =>
                new SchematicAnchor { EndpointId = pin.PinId, Position = new(50, 20 + i * 25 + j * 5), Direction = "Right" }))).ToList(),
            CollapsedPortIds = component.Ports.Skip(1).Select(port => port.PortId).ToList() };
        symbol = project.Schematic.Symbols.Single();
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var body = SchematicPortPresentation.GenericBodyBounds(project.Schematic, symbol, owner);
        Assert.InRange(rotation == 0 ? body.Height : body.Width, 80, 139);
        Assert.Equal(5, symbol.CollapsedPortIds.Count);
        var contacts = component.Ports.Skip(1).Select(port => SchematicPortPresentation.GroupContact(symbol, owner, port, body)).ToArray();
        Assert.All(contacts, contact => Assert.Equal(side, contact.Side));
        Assert.Equal(5, contacts.Select(contact => contact.Position).Distinct().Count());
        Assert.All(symbol.Anchors.Where(a => a.EndpointId.StartsWith("ETH", StringComparison.Ordinal)),
            a => Assert.True(SchematicPortPresentation.IsCollapsedPin(project.Schematic, symbol, owner, a)));
        Assert.Equal(12, symbol.Anchors.Count);
    }

    [Theory]
    [InlineData(0, "Top")]
    [InlineData(90, "Left")]
    public void CollapsedBodyEdgeDragMapsBackToOriginalLocalEdge(int rotation, string expectedSide)
    {
        var project = Project();
        var symbol = project.Schematic!.Symbols.Single() with { Rotation = rotation, Height = 140,
            CollapsedPortIds = ["P"] };
        project.Schematic.Symbols[0] = symbol;
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var body = SchematicPortPresentation.GenericBodyBounds(project.Schematic, symbol, owner);
        var target = SchematicPortPresentation.DropTarget(symbol,
            new(body.X + body.Width / 2, body.Y), body);
        Assert.Equal(expectedSide, target.Side);
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
            Symbols = [SchematicAuthoringService.CreateSymbol(component, "page", new(40, 40)) with { CollapsedPortIds = [] }]
        } };
    }
}
