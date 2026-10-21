using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPinOrderTests
{
    [Fact]
    public void VirtualCadPinOrderSwapsPositionsWithoutChangingNumbersFunctionsOrPortBinding()
    {
        var p = Fixture();
        var symbol = p.Schematic!.Symbols[0];
        var port = p.Cables[0].ArchivedCable!.Ports[0];
        var ids = port.Pins.Select(pin => pin.PinId).Reverse().ToArray();
        var before = JsonSerializer.Serialize(p.Cables);
        var bindings = JsonSerializer.Serialize(symbol.CadPortBindings);
        var originalPosition = symbol.Anchors.Single(a => a.EndpointId == ids[0]).Position;
        var updated = Reorder(p, symbol.SymbolId, port.PortId, ids);
        Assert.Equal(before, JsonSerializer.Serialize(updated.Cables));
        Assert.Equal(bindings, JsonSerializer.Serialize(updated.Schematic!.Symbols[0].CadPortBindings));
        Assert.NotEqual(originalPosition, updated.Schematic.Symbols[0].Anchors.Single(a => a.EndpointId == ids[0]).Position);
        var visual = updated.Schematic.Symbols[0].Anchors.Where(a => ids.Contains(a.EndpointId)).OrderBy(a => a.Position.Y);
        Assert.Equal(ids, visual.Select(a => a.EndpointId));
        SchematicAuthoringService.Validate(JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(updated))!);
    }

    [Fact]
    public void ExistingWireFollowsTheSamePinIdentityAfterReorder()
    {
        var p = Fixture();
        var symbol = p.Schematic!.Symbols[0];
        var port = p.Cables[0].ArchivedCable!.Ports[0];
        var pin = port.Pins[0];
        var point = SchematicAuthoringService.AnchorPoint(symbol, pin.PinId);
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin(symbol.SymbolId, pin.PinId),
            SchematicAttachment.Free(), [point, new(10, point.Y)], manualRoute: true);
        var updated = Reorder(p, symbol.SymbolId, port.PortId, port.Pins.Select(pin => pin.PinId).Reverse().ToArray());
        var wire = updated.Schematic!.Wires.Single();
        Assert.Equal(pin.PinId, wire.Start.EndpointId);
        Assert.Equal(SchematicAuthoringService.AnchorPoint(updated.Schematic.Symbols[0], pin.PinId), wire.Points[0]);
        Assert.True(wire.ManualRoute);
    }

    [Fact]
    public void LockedSymbolRejectsReorderAtomically()
    {
        var p = Fixture();
        p.Schematic!.Symbols[0] = p.Schematic.Symbols[0] with { Locked = true };
        var before = JsonSerializer.Serialize(p);
        var port = p.Cables[0].ArchivedCable!.Ports[0];
        Assert.Throws<InvalidOperationException>(() => Reorder(p, p.Schematic.Symbols[0].SymbolId, port.PortId,
            port.Pins.Select(pin => pin.PinId).Reverse().ToArray()));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void InvalidPinPermutationIsNotApplied()
    {
        var p = Fixture();
        var port = p.Cables[0].ArchivedCable!.Ports[0];
        Assert.Throws<InvalidOperationException>(() => Reorder(p, p.Schematic!.Symbols[0].SymbolId, port.PortId,
            [port.Pins[0].PinId, port.Pins[0].PinId]));
    }

    [Fact]
    public void PhysicalCadPinBindingCannotBeReorderedWithoutExplicitRebinding()
    {
        var p = Fixture();
        var symbol = p.Schematic!.Symbols[0]; var port = p.Cables[0].ArchivedCable!.Ports[0];
        var anchors = symbol.Anchors.ToList(); anchors[0] = anchors[0] with { CadContactId = "physical-pin" };
        p.Schematic.Symbols[0] = symbol with { Anchors = anchors };
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => Reorder(p, symbol.SymbolId, port.PortId, port.Pins.Select(pin => pin.PinId).Reverse().ToArray()));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void IncidentLockedWireRejectsPinOrderAtomically()
    {
        var p = Fixture(); var symbol = p.Schematic!.Symbols[0]; var port = p.Cables[0].ArchivedCable!.Ports[0];
        var pin = port.Pins[0]; var point = SchematicAuthoringService.AnchorPoint(symbol, pin.PinId);
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin(symbol.SymbolId, pin.PinId),
            SchematicAttachment.Free(), [point, new(10, point.Y)], manualRoute: true);
        p.Schematic!.Wires[0] = p.Schematic.Wires[0] with { Locked = true };
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => Reorder(p, symbol.SymbolId, port.PortId, port.Pins.Select(pin => pin.PinId).Reverse().ToArray()));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    private static ElectricalProject Reorder(ElectricalProject p, string symbol, string port, string[] ids)
    {
        return new SchematicAuthoringService().ReorderPortPins(p, symbol, port, ids);
    }

    private static ElectricalProject Fixture()
    {
        var template = CableManufacturingEditorTests.Template();
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 80, Height = 40,
            MillimetresPerUnit = 1, Primitives = [new() { Kind = "LINE", Start = new(0, 20), End = new(80, 20) }],
            ConnectionPoints = [new("CAD-P1", "", new(0, 20), Direction: "Left"), new("CAD-P2", "", new(80, 20), Direction: "Right")] };
        return new SchematicAuthoringService().AddArchivedCable(new() { ProjectId = "pin-order",
            Schematic = new() { Pages = [new() { PageId = "page", Title = "Cable" }] } }, template, CableConstructionType.Unknown,
            geometry, new Dictionary<string, string> { ["P1"] = "CAD-P1", ["P2"] = "CAD-P2" }, "page", new(20, 20));
    }
}
