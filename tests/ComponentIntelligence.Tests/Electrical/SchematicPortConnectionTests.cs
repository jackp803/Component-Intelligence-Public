using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPortConnectionTests
{
    private readonly SchematicAuthoringService _service = new();

    [Fact]
    public void CollapsedPortsConnectByPortIdAndExpansionPreservesElectricalIdentity()
    {
        var project = Project();
        var first = project.Schematic!.Symbols[0];
        var second = project.Schematic.Symbols[1];
        var start = SchematicPortPresentation.ConnectionPoint(project, first, "A");
        var end = SchematicPortPresentation.ConnectionPoint(project, second, "B");
        var connected = _service.DrawWire(project, "page", SchematicAttachment.Port(first.SymbolId, "A"),
            SchematicAttachment.Port(second.SymbolId, "B"), Route(start, end));

        var connection = Assert.Single(connected.Connections);
        Assert.Equal("A", connection.FromEndpointId);
        Assert.Equal("B", connection.ToEndpointId);
        Assert.Null(connection.CableCoreId);
        var wire = Assert.Single(connected.Schematic!.Wires);
        Assert.Equal(connection.ConnectionId, wire.ConnectionId);
        Assert.Equal(SchematicAttachmentKind.Port, wire.Start.Kind);
        Assert.Equal(SchematicAttachmentKind.Port, wire.End.Kind);

        var expanded = _service.TogglePortCollapsed(connected, first.SymbolId, "A");
        Assert.DoesNotContain("A", expanded.Schematic!.Symbols[0].CollapsedPortIds);
        Assert.Equal(connection.ConnectionId, Assert.Single(expanded.Connections).ConnectionId);
        Assert.Equal("A", Assert.Single(expanded.Connections).FromEndpointId);
        Assert.Equal(SchematicPortPresentation.ConnectionPoint(expanded, expanded.Schematic.Symbols[0], "A"),
            expanded.Schematic.Wires[0].Points[0]);
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(expanded))!;
        SchematicAuthoringService.Validate(reloaded);
        Assert.Equal(connection.ConnectionId, Assert.Single(reloaded.Connections).ConnectionId);
        Assert.Equal("A", Assert.Single(reloaded.Connections).FromEndpointId);
    }

    [Fact]
    public void ExpandedPinCanConnectWithoutChangingExistingPortConnection()
    {
        var project = Project();
        var first = project.Schematic!.Symbols[0];
        var second = project.Schematic.Symbols[1];
        var start = SchematicPortPresentation.ConnectionPoint(project, first, "A");
        var end = SchematicPortPresentation.ConnectionPoint(project, second, "B");
        project = _service.DrawWire(project, "page", SchematicAttachment.Port(first.SymbolId, "A"),
            SchematicAttachment.Port(second.SymbolId, "B"), Route(start, end));
        var original = Assert.Single(project.Connections);
        project = _service.TogglePortCollapsed(project, first.SymbolId, "A");
        var pin = SchematicAuthoringService.AnchorPoint(project.Schematic!.Symbols[0], "A1");
        var free = new SchematicPoint(pin.X - 25, pin.Y);
        project = _service.DrawWire(project, "page", SchematicAttachment.Pin(first.SymbolId, "A1"),
            SchematicAttachment.Free(), [pin, free]);
        Assert.Equal(2, project.Schematic!.Wires.Count);
        Assert.Single(project.Connections);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(project.Connections[0]));
    }

    [Fact]
    public void PortRouteFollowsMoveRotateAndEdgeChangeButLockedRouteRejectsChangedAnchor()
    {
        var project = Project();
        var first = project.Schematic!.Symbols[0];
        var second = project.Schematic.Symbols[1];
        project = _service.DrawWire(project, "page", SchematicAttachment.Port(first.SymbolId, "A"),
            SchematicAttachment.Port(second.SymbolId, "B"), Route(
                SchematicPortPresentation.ConnectionPoint(project, first, "A"),
                SchematicPortPresentation.ConnectionPoint(project, second, "B")));
        var connectionId = Assert.Single(project.Connections).ConnectionId;
        project = _service.MovePortToEdge(project, first.SymbolId, "A", "Top", 25);
        project = _service.TransformSymbol(project, second.SymbolId, new(150, 45), 90);
        var wire = Assert.Single(project.Schematic!.Wires);
        Assert.Equal(SchematicPortPresentation.ConnectionPoint(project, project.Schematic.Symbols[0], "A"), wire.Points[0]);
        Assert.Equal(SchematicPortPresentation.ConnectionPoint(project, project.Schematic.Symbols[1], "B"), wire.Points[^1]);
        Assert.Equal(connectionId, Assert.Single(project.Connections).ConnectionId);
        SchematicAuthoringService.Validate(project);

        project = _service.SetLocked(project, wire.WireId, true);
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => _service.TogglePortCollapsed(project, first.SymbolId, "A"));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void HiddenPinsCannotBeSnappedUntilTheirPortIsExpanded()
    {
        var project = Project();
        var symbol = project.Schematic!.Symbols[0];
        var pinPoint = SchematicAuthoringService.AnchorPoint(symbol, "A1");
        Assert.Null(SchematicAnchorSnap.FindVisible(project, "page", pinPoint, .1));
        project = _service.TogglePortCollapsed(project, symbol.SymbolId, "A");
        Assert.Equal("A1", SchematicAnchorSnap.FindVisible(project, "page", pinPoint, .1)?.EndpointId);
    }

    [Fact]
    public void MovingPortConnectedModuleToAnotherPagePreservesConnectionAndIdentifiesUnspecifiedPin()
    {
        var project = Project();
        project.Schematic!.Pages.Add(new() { PageId = "other", Title = "Other" });
        var first = project.Schematic.Symbols[0];
        var second = project.Schematic.Symbols[1];
        project = _service.DrawWire(project, "page", SchematicAttachment.Port(first.SymbolId, "A"),
            SchematicAttachment.Port(second.SymbolId, "B"), Route(
                SchematicPortPresentation.ConnectionPoint(project, first, "A"),
                SchematicPortPresentation.ConnectionPoint(project, second, "B")));
        var original = JsonSerializer.Serialize(Assert.Single(project.Connections));
        project = _service.MoveSymbolsToPage(project, [second.SymbolId], "other");
        Assert.Equal(original, JsonSerializer.Serialize(Assert.Single(project.Connections)));
        Assert.Equal(2, project.Schematic!.Wires.Count);
        var continuation = Assert.Single(project.Schematic.Continuations);
        Assert.Contains("Pin 待指定", _service.ReferenceFor(project, continuation.Source.MarkerId));
        Assert.Contains("Pin 待指定", _service.ReferenceFor(project, continuation.Destination.MarkerId));
        SchematicAuthoringService.Validate(project);
    }

    [Fact]
    public void PortWithoutPinDefinitionRemainsConnectableWithoutInventingPins()
    {
        var project = Project();
        foreach (var port in project.Components.SelectMany(component => component.Ports)) port.Pins.Clear();
        project.Schematic!.Symbols.Clear();
        project.Schematic.Symbols.AddRange(project.Components.Select((component, index) =>
            SchematicAuthoringService.CreateSymbol(component, "page", new(30 + index * 100, 30))));
        var first = project.Schematic.Symbols[0];
        var second = project.Schematic.Symbols[1];
        Assert.Empty(first.Anchors);
        Assert.Contains("A", first.CollapsedPortIds);
        first.CollapsedPortIds.Clear();
        second.CollapsedPortIds.Clear();
        Assert.True(SchematicPortPresentation.IsPortCollapsed(first, project.Components[0].Ports[0]));
        project = _service.DrawWire(project, "page", SchematicAttachment.Port(first.SymbolId, "A"),
            SchematicAttachment.Port(second.SymbolId, "B"), Route(
                SchematicPortPresentation.ConnectionPoint(project, first, "A"),
                SchematicPortPresentation.ConnectionPoint(project, second, "B")));
        Assert.Single(project.Connections);
        Assert.All(project.Components.SelectMany(component => component.Ports), port => Assert.Empty(port.Pins));
        SchematicAuthoringService.Validate(project);
    }

    [Fact]
    public void CollapsedPortCanConnectToAnExpandedExactPin()
    {
        var project = Project();
        var first = project.Schematic!.Symbols[0];
        var second = project.Schematic.Symbols[1];
        project = _service.TogglePortCollapsed(project, second.SymbolId, "B");
        var start = SchematicPortPresentation.ConnectionPoint(project, first, "A");
        var end = SchematicAuthoringService.AnchorPoint(project.Schematic!.Symbols[1], "B1");
        project = _service.DrawWire(project, "page", SchematicAttachment.Port(first.SymbolId, "A"),
            SchematicAttachment.Pin(second.SymbolId, "B1"), Route(start, end));
        var connection = Assert.Single(project.Connections);
        Assert.Equal("A", connection.FromEndpointId);
        Assert.Equal("B1", connection.ToEndpointId);
        Assert.DoesNotContain(project.Connections, c => c.ToEndpointId == "B2" || c.FromEndpointId == "B2");
        SchematicAuthoringService.Validate(project);
    }

    private static IReadOnlyList<SchematicPoint> Route(SchematicPoint start, SchematicPoint end) =>
        start.Y == end.Y ? [start, end] : [start, new(end.X, start.Y), end];

    private static ElectricalProject Project()
    {
        var first = new ComponentInstance { ComponentInstanceId = "C1", ComponentDefinitionId = "DEV-1", TypeKey = "MODULE", Ports =
        [
            new ComponentPort { PortId = "A", Name = "ETH", Pins =
            [
                new ComponentPin { PinId = "A1", PinNumber = "1" },
                new ComponentPin { PinId = "A2", PinNumber = "2" }
            ] }
        ] };
        var second = new ComponentInstance { ComponentInstanceId = "C2", ComponentDefinitionId = "DEV-2", TypeKey = "MODULE", Ports =
        [
            new ComponentPort { PortId = "B", Name = "ETH", Pins =
            [
                new ComponentPin { PinId = "B1", PinNumber = "1" },
                new ComponentPin { PinId = "B2", PinNumber = "2" }
            ] }
        ] };
        return new ElectricalProject { ProjectId = "port-wire", Components = [first, second], Schematic = new()
        {
            Pages = [new() { PageId = "page", Title = "Sheet" }],
            Symbols = [SchematicAuthoringService.CreateSymbol(first, "page", new(30, 30)),
                SchematicAuthoringService.CreateSymbol(second, "page", new(130, 30))]
        } };
    }
}
