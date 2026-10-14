using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicEditingConsistencyTests
{
    [Fact]
    public void MovingSixPinBundleKeepsIndependentChannelsAndExactEndpoints()
    {
        var p = Drawing(); var service = new SchematicAuthoringService();
        var before = p.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId)).ToArray();
        var next = service.TransformSymbol(p, "left", new(25, 85), 0);
        var conflicts = SchematicCrossingService.Analyze(next.Schematic!.Wires).Conflicts;
        Assert.DoesNotContain(conflicts, c => c.Code == "COLLINEAR_OVERLAP");
        Assert.DoesNotContain(conflicts, c => c.Code == "UNCONNECTED_CONTACT");
        Assert.Equal(before, next.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId)));
        Assert.Equal(new SchematicPoint(25, 40), p.Schematic!.Symbols[0].Position);
        Assert.All(next.Schematic.Wires, w => Assert.Equal(SchematicAuthoringService.AnchorPoint(next.Schematic.Symbols[0], w.Start.EndpointId!), w.Points[0]));
    }

    [Fact]
    public void WireCaptionUsesActualDesignationAndRoundedArea()
    {
        var p = Drawing(); var service = new SchematicAuthoringService(); var id = p.Schematic!.Wires[0].WireId;
        p = service.SetWireDesignation(p, id, "1P24"); p = service.SetWireAwg(p, id, 18);
        var entry = SchematicWireLegendPresentation.Entry(p, p.Schematic!.Wires[0]);
        Assert.StartsWith("1P24", entry.Caption);
        Assert.DoesNotContain("0.823046", entry.Specification.Text);
        Assert.Contains("0.823", entry.Specification.Text);
        Assert.StartsWith("1P24", SchematicWireLegendPresentation.Groups([entry])[0].Text);
    }

    [Fact]
    public void UnknownSpecificationIsOneReviewStateInsteadOfTwoMissingSettings()
    {
        var p = Drawing(); var entry = SchematicWireLegendPresentation.Entry(p, p.Schematic!.Wires[0]);
        Assert.Equal("線徑待核對", entry.Specification.Text);
        Assert.Null(p.Schematic.Wires[0].AreaMm2);
    }

    [Fact]
    public void BatchSpecificationIsAtomicAndCanReuseAnExplicitKnownSize()
    {
        var p = Drawing(); var service = new SchematicAuthoringService();
        var ids = p.Schematic!.Wires.Take(2).Select(w => w.WireId).ToArray();
        var next = service.SetWireSpecifications(p, ids, awg: 18);
        Assert.All(next.Schematic!.Wires.Take(2), w => Assert.Equal(18, w.Awg));
        Assert.All(p.Schematic.Wires, w => Assert.Null(w.Awg));
        next = service.SetWireSpecifications(next, [next.Schematic.Wires[2].WireId], referenceWireId: ids[0]);
        Assert.Equal(18, next.Schematic!.Wires[2].Awg);
        var locked = service.SetLocked(p, ids[1], true);
        var before = JsonSerializer.Serialize(locked);
        Assert.Throws<InvalidOperationException>(() => service.SetWireSpecifications(locked, ids, awg: 20));
        Assert.Equal(before, JsonSerializer.Serialize(locked));
    }

    [Fact]
    public void ReorderingConnectedPinsKeepsRoutesIndependent()
    {
        var p = Drawing(); var service = new SchematicAuthoringService();
        var ids = p.Components[0].Ports[0].Pins.Select(pin => pin.PinId).Reverse().ToArray();
        var next = service.ReorderPortPins(p, "left", "left.port", ids);
        Assert.Equal(ids, SchematicAuthoringService.PortPinsInVisualOrder(next.Schematic!.Symbols[0], next.Components[0].Ports[0]).Select(a => a.EndpointId));
        Assert.Empty(SchematicCrossingService.Analyze(next.Schematic.Wires).Conflicts);
        Assert.Equal(p.Connections.Select(c => (c.FromEndpointId, c.ToEndpointId)), next.Connections.Select(c => (c.FromEndpointId, c.ToEndpointId)));
    }

    [Fact]
    public void MovingDraftWireBundleAlsoKeepsSeparateChannels()
    {
        var p = Drawing(); var service = new SchematicAuthoringService();
        p.Schematic!.Wires.Clear(); p.Connections.Clear();
        for (var i = 1; i <= 6; i++) p = service.DrawWire(p, "main", SchematicAttachment.Pin("left", "left." + i),
            SchematicAttachment.Free(), [new(65, 52 + (i - 1) * 10), new(210, 52 + (i - 1) * 10)]);
        var next = service.TransformSymbol(p, "left", new(25, 85), 0);
        Assert.Empty(next.Connections);
        Assert.Empty(SchematicCrossingService.Analyze(next.Schematic!.Wires).Conflicts);
    }

    [Fact]
    public void ReusedMetricSpecificationKeepsItsExactArea()
    {
        var service = new SchematicAuthoringService(); var p = Drawing(); var ids = p.Schematic!.Wires.Select(w => w.WireId).ToArray();
        p = service.SetWireArea(p, ids[0], .5);
        var next = service.SetWireSpecifications(p, [ids[1]], referenceWireId: ids[0]);
        Assert.Equal(.5, next.Schematic!.Wires[1].AreaMm2);
        Assert.Null(next.Schematic.Wires[1].Awg);
    }

    [Fact]
    public void MovingOtherModuleReroutesObstructedAutomaticWires()
    {
        var service = new SchematicAuthoringService(); var p = Drawing(2);
        p.Components.Add(new() { ComponentInstanceId = "obstacle", ComponentDefinitionId = "obstacle", TypeKey = "Module" });
        p.Schematic!.Symbols.Add(new() { SymbolId = "obstacle", ComponentInstanceId = "obstacle", PageId = "main", Position = new(120, 160), Width = 40, Height = 80 });
        var next = service.TransformSymbol(p, "obstacle", new(120, 40), 0);
        foreach (var wire in next.Schematic!.Wires)
        foreach (var (a, b) in wire.Points.Zip(wire.Points.Skip(1)))
            Assert.False(a.Y == b.Y && a.Y > 40 && a.Y < 120 && Math.Min(a.X, b.X) < 160 && Math.Max(a.X, b.X) > 120);
        Assert.Empty(SchematicCrossingService.Analyze(next.Schematic.Wires).Conflicts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MovingOtherModuleCannotHideAnAuthoritativeRoute(bool locked)
    {
        var service = new SchematicAuthoringService(); var p = Drawing(1); var wire = p.Schematic!.Wires[0];
        p = locked ? service.SetLocked(p, wire.WireId, true) : service.ReplaceRoute(p, wire.WireId, wire.Points);
        p.Components.Add(new() { ComponentInstanceId = "obstacle", ComponentDefinitionId = "obstacle", TypeKey = "Module" });
        p.Schematic!.Symbols.Add(new() { SymbolId = "obstacle", ComponentInstanceId = "obstacle", PageId = "main", Position = new(120, 160), Width = 40, Height = 80 });
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => service.TransformSymbol(p, "obstacle", new(120, 40), 0));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void MovingModuleKeepsEveryManualInteriorBend()
    {
        var service = new SchematicAuthoringService(); var p = Drawing(1); var wire = p.Schematic!.Wires[0];
        p = service.ReplaceRoute(p, wire.WireId, [new(65, 52), new(100, 52), new(100, 120), new(200, 120), new(200, 52), new(240, 52)]);
        var original = p.Schematic!.Wires[0].Points.Skip(1).SkipLast(1).ToArray();
        var next = service.TransformSymbol(p, "left", new(25, 70), 0);
        Assert.All(original, point => Assert.Contains(point, next.Schematic!.Wires[0].Points));
        Assert.True(next.Schematic!.Wires[0].ManualRoute);
    }

    [Fact]
    public void ReorderingFanoutChangesVisibleSlotsNotNativeCadAnchors()
    {
        var service = new SchematicAuthoringService(); var p = service.SetPortFanout(Drawing(2), "left", "left.port", true);
        var anchors = JsonSerializer.Serialize(p.Schematic!.Symbols[0].Anchors);
        var ids = new[] { "left.2", "left.1" };
        var next = service.ReorderPortPins(p, "left", "left.port", ids);
        Assert.Equal(ids, SchematicAuthoringService.PortPinsInVisualOrder(next.Schematic!.Symbols[0], next.Components[0].Ports[0]).Select(a => a.EndpointId));
        Assert.Equal(anchors, JsonSerializer.Serialize(next.Schematic.Symbols[0].Anchors));
        var locked = service.SetLocked(p, p.Schematic.Wires[0].WireId, true);
        Assert.Throws<InvalidOperationException>(() => service.ReorderPortPins(locked, "left", "left.port", ids));
    }

    [Fact]
    public void FreeTailCannotSilentlyTouchAnUnrelatedWire()
    {
        var service = new SchematicAuthoringService(); var p = Drawing(1); p.Schematic!.Wires.Clear(); p.Connections.Clear();
        p.Schematic.Wires.Add(new() { WireId = "barrier", PageId = "main", Points = [new(120, 40), new(120, 100)], ManualRoute = true });
        Assert.Throws<InvalidOperationException>(() => service.DrawWire(p, "main", SchematicAttachment.Pin("left", "left.1"),
            SchematicAttachment.Free(), [new(65, 52), new(120, 52)]));
    }

    internal static ElectricalProject Drawing(int count = 6)
    {
        var p = new ElectricalProject { ProjectId = "synthetic-editing", Schematic = new() { Pages = [new() { PageId = "main", Title = "Main" }] } };
        foreach (var id in new[] { "left", "right" })
        {
            var port = new ComponentPort { PortId = id + ".port", Name = "P1", Pins = Enumerable.Range(1, count)
                .Select(i => new ComponentPin { PinId = id + "." + i, PinNumber = i.ToString(), PinName = i == 1 ? "COM / black" : i == 2 ? "24V / red" : "Signal" }).ToList() };
            p.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = "Module", ReferenceDesignator = id == "left" ? "Valve1" : "IO1", Ports = [port] });
            p.Schematic.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "main", Width = 40, Height = 80,
                Position = new(id == "left" ? 25 : 240, 40), Anchors = port.Pins.Select((pin, i) => new SchematicAnchor { EndpointId = pin.PinId,
                    Position = new(id == "left" ? 40 : 0, 12 + i * 10), Direction = id == "left" ? "Right" : "Left", Label = "P1 / " + pin.PinNumber + " " + pin.PinName }).ToList() });
        }
        var service = new SchematicAuthoringService();
        for (var i = 1; i <= count; i++) p = service.DrawWire(p, "main", SchematicAttachment.Pin("left", "left." + i),
            SchematicAttachment.Pin("right", "right." + i), [new(65, 42 + i * 10), new(240, 42 + i * 10)]);
        return p;
    }
}
