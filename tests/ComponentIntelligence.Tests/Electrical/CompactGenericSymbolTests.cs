using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CompactGenericSymbolTests
{
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void SmallTwoContactFrameKeepsIdsConnectionsAndFollowedWireEnds(int rotation)
    {
        var project = Fixture(rotation); var author = new SchematicAuthoringService();
        var symbol = project.Schematic!.Symbols.Single(); var anchor = symbol.Anchors[0];
        var start = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
        var direction = SchematicPortPresentation.RotatedSide(anchor.Direction, rotation);
        var free = direction switch { "Left" => new SchematicPoint(start.X - 20, start.Y), "Right" => new(start.X + 20, start.Y),
            "Top" => new(start.X, start.Y - 20), _ => new(start.X, start.Y + 20) };
        project = author.DrawWire(project, "page", SchematicAttachment.Pin(symbol.SymbolId, anchor.EndpointId), SchematicAttachment.Free(), [start, free]);
        var before = JsonSerializer.Serialize(project); var connectionIds = project.Connections.Select(c => c.ConnectionId).ToArray();
        var history = new ProjectMutationHistory(); history.RecordBeforeMutation(project, "Resize");
        var resized = author.ResizeGenericSymbol(project, symbol.SymbolId, 20, 18);
        var next = resized.Schematic!.Symbols.Single();
        var body = SchematicPortPresentation.GenericBodyBounds(resized.Schematic, next, SchematicSymbolOwner.Resolve(resized, next));
        Assert.Equal(20, body.Width); Assert.Equal(18, body.Height);
        Assert.Equal(symbol.SymbolId, next.SymbolId); Assert.Equal(symbol.Position, next.Position);
        Assert.Equal(symbol.Anchors.Select(a => a.EndpointId), next.Anchors.Select(a => a.EndpointId));
        Assert.Equal(connectionIds, resized.Connections.Select(c => c.ConnectionId));
        Assert.Equal(project.Schematic.Wires[0].WireId, resized.Schematic.Wires[0].WireId);
        Assert.Equal(SchematicAuthoringService.AnchorPoint(next, anchor.EndpointId), resized.Schematic.Wires[0].Points[0]);
        Assert.Equal(free, resized.Schematic.Wires[0].Points[^1]);
        Assert.True(history.TryUndo(resized, out var undone, out _)); Assert.Equal(before, JsonSerializer.Serialize(undone));
        Assert.True(history.TryRedo(undone, out var redone, out _)); Assert.Equal(JsonSerializer.Serialize(resized), JsonSerializer.Serialize(redone));
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(resized))!;
        SchematicAuthoringService.Validate(reloaded);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void InspectorAcceptsCompactSizeAndMinimumStillProtectsContacts()
    {
        var project = Fixture(); var symbol = project.Schematic!.Symbols.Single();
        var next = SchematicInspectorEdit.ApplyModule(project, symbol.SymbolId, new("X1", symbol.Position.X, symbol.Position.Y, 20, 18, 0, false, null));
        Assert.Equal(20, next.Schematic!.Symbols.Single().Width); Assert.Equal(18, next.Schematic.Symbols.Single().Height);
        var minimum = new SchematicAuthoringService().ResizeGenericSymbol(project, symbol.SymbolId, 1, 1).Schematic!.Symbols.Single();
        Assert.Equal(20, minimum.Width); Assert.Equal(18, minimum.Height);
        var crowded = Fixture(); var component = crowded.Components.Single();
        component.Ports[0].Pins.AddRange(Enumerable.Range(3, 4).Select(n => new ComponentPin { PinId = "pin" + n, PinNumber = n.ToString() }));
        var view = SchematicAuthoringService.CreateSymbol(component, "page", new(60, 60)) with { CollapsedPortIds = [] };
        crowded.Schematic!.Symbols[0] = view;
        var protectedSize = new SchematicAuthoringService().ResizeGenericSymbol(crowded, view.SymbolId, 20, 18).Schematic!.Symbols.Single();
        Assert.True(protectedSize.Height >= 42);
        var rows = protectedSize.Anchors.Where(a => a.Direction == "Right").Select(a => a.Position.Y).Order().ToArray();
        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 6));
    }

    internal static ElectricalProject Fixture(int rotation = 0)
    {
        var component = new ComponentInstance { ComponentInstanceId = "wago", ComponentDefinitionId = "synthetic-wago", TypeKey = "SPLICE", DisplayName = "WAGO 221-2411",
            Ports = [new() { PortId = "splice", Name = "SPLICE", Pins = [new() { PinId = "pin1", PinNumber = "1" }, new() { PinId = "pin2", PinNumber = "2" }] }] };
        var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(60, 60)) with { Rotation = rotation, CollapsedPortIds = [] };
        return new() { ProjectId = "compact", Components = [component], Schematic = new() { Pages = [new() { PageId = "page", Title = "Compact" }], Symbols = [symbol] } };
    }

    [Fact]
    public void MultiplePortControlsKeepTheirExistingMinimumRoom()
    {
        var project = Fixture(); var component = project.Components.Single();
        component.Ports.Add(new() { PortId = "other", Name = "Other", Pins = [new() { PinId = "other1", PinNumber = "1" }] });
        var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(60, 60)); project.Schematic!.Symbols[0] = symbol;
        var resized = new SchematicAuthoringService().ResizeGenericSymbol(project, symbol.SymbolId, 1, 1).Schematic!.Symbols.Single();
        Assert.Equal(30, resized.Width); Assert.Equal(30, resized.Height);
        var layout = SchematicCatalogSymbolLayout.Create(20, 18, 100, 50);
        Assert.True(layout.ImageWidth > 0 && layout.ImageHeight > 0);
    }
}
