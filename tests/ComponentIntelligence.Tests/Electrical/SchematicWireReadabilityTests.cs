using ComponentIntelligence.Electrical.Cables;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using System.Text.Json;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicWireReadabilityTests
{
    [Fact]
    public void KnownLowVoltageAcLoadCanReceiveProvisionalSizeWithoutDerivingCurrentFromWatts()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        foreach (var pin in p.Components.SelectMany(c => c.Ports).SelectMany(port => port.Pins))
            pin.Power = new() { Role = pin.Power!.Role, RequiredCurrentAmp = pin.Power.RequiredCurrentAmp,
                Voltage = new() { Type = VoltageType.Ac, NominalVoltage = 24 } };
        p = SchematicWireIdentificationTests.Draw(p);
        Assert.Equal(.5, p.Schematic!.Wires.Single().SizingProposal?.AreaMm2);
    }

    [Fact]
    public void NewWireLeavesBothContactsOutwardBeforeTurning()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Schematic!.Symbols[1] = p.Schematic.Symbols[1] with { Position = new(160, 60) };
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin("S1", "A"),
            SchematicAttachment.Pin("S2", "B"), [new(60, 25), new(160, 25), new(160, 65)]);
        var points = p.Schematic!.Wires.Single().Points;
        Assert.Equal(25, points[1].Y);
        Assert.True(points[1].X > 60);
        Assert.Equal(65, points[^2].Y);
        Assert.True(points[^2].X < 160);
    }

    [Fact]
    public void NewAutomaticWireAvoidsAnInterveningModule()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components.Add(new() { ComponentInstanceId = "obstacle", ComponentDefinitionId = "o", TypeKey = "IO" });
        p.Schematic!.Symbols.Add(new() { SymbolId = "O", ComponentInstanceId = "obstacle", PageId = "page",
            Width = 30, Height = 30, Position = new(95, 15) });
        p = SchematicWireIdentificationTests.Draw(p);
        Assert.DoesNotContain(p.Schematic!.Wires.Single().Points.Zip(p.Schematic.Wires.Single().Points.Skip(1)),
            edge => edge.First.Y == edge.Second.Y && edge.First.Y > 15 && edge.First.Y < 45 &&
                Math.Min(edge.First.X, edge.Second.X) < 125 && Math.Max(edge.First.X, edge.Second.X) > 95);
    }

    [Fact]
    public void ObstacleBlockingContactLeadCannotBeSilentlyIgnored()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components.Add(new() { ComponentInstanceId = "obstacle", ComponentDefinitionId = "o", TypeKey = "IO" });
        p.Schematic!.Symbols.Add(new() { SymbolId = "O", ComponentInstanceId = "obstacle", PageId = "page",
            Width = 30, Height = 30, Position = new(64, 20) });
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => SchematicWireIdentificationTests.Draw(p));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void CloseOpposingContactsDoNotProduceRetracedLeads()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Schematic!.Symbols[1] = p.Schematic.Symbols[1] with { Position = new(67, 20) };
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin("S1", "A"),
            SchematicAttachment.Pin("S2", "B"), [new(60, 25), new(67, 25)]);
        var points = p.Schematic!.Wires.Single().Points;
        Assert.True(points.Zip(points.Skip(1)).All(edge => edge.Second.X >= edge.First.X), "Route retraced its own lead.");
    }

    [Fact]
    public void ReorderingOtherPinsPreservesStationaryPinManualRoute()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        var port = p.Components[0].Ports[0];
        port.Pins.AddRange([new() { PinId = "C", PinNumber = "2" }, new() { PinId = "D", PinNumber = "3" }]);
        var symbol = p.Schematic!.Symbols[0];
        p.Schematic.Symbols[0] = symbol with { Anchors = symbol.Anchors.Concat(new[] {
            new SchematicAnchor { EndpointId = "C", Position = new(40, 10), Direction = "Right" },
            new SchematicAnchor { EndpointId = "D", Position = new(40, 15), Direction = "Right" } }).ToList() };
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 25), new(100, 25), new(100, 100), new(130, 100)], manualRoute: true);
        var route = p.Schematic!.Wires.Single().Points.ToArray();
        var next = new SchematicAuthoringService().ReorderPortPins(p, "S1", port.PortId, ["A", "D", "C"]);
        Assert.Equal(route, next.Schematic!.Wires.Single().Points);
    }

    [Theory]
    [InlineData("Com / black", "#1F1F1F")]
    [InlineData("AC/DC 24 V / red", "#C62828")]
    [InlineData("Sensor (optional) / white", "#FFFFFF")]
    [InlineData("D- / pink", "#C21873")]
    public void ExplicitLeadColorWinsOverPolarityPreview(string name, string expected)
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].Ports[0].Pins[0].PinName = name;
        p = SchematicWireIdentificationTests.Draw(p);
        Assert.Equal(expected, SchematicWireEvidence.Resolve(p, p.Schematic!.Wires.Single()).ColorHex);
    }

    [Fact]
    public void CombinedCaptionIncludesBothDifferentFunctionsAndPhysicalColor()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].Ports[0].Pins[0].Function = "COM";
        p.Components[0].Ports[0].Pins[0].PinName = "Com / black";
        p.Components[1].Ports[0].Pins[0].Function = "GND";
        p = SchematicWireIdentificationTests.Draw(p);
        var label = SchematicCableLabelPresentation.Resolve(p, p.Schematic!.Wires.Single())!;
        Assert.Contains("COM", label.Text);
        Assert.Contains("GND", label.Text);
        Assert.Contains("black", label.Text);
        Assert.Contains("AWG", label.Text);
    }

    [Theory]
    [InlineData(.14)]
    [InlineData(.34)]
    [InlineData(.5)]
    [InlineData(.75)]
    public void DisplayGaugeNeverRoundsToASmallerConductor(double area)
    {
        Assert.True(WireSize.AwgToAreaMm2(SchematicWireIdentification.ApproximateAwg(area)) >= area);
    }

    [Fact]
    public void AreaOutsideSupportedAwgRangeDoesNotDisplayAnUndersizedGauge()
    {
        var p = SchematicWireIdentificationTests.Draw(SchematicWireIdentificationTests.Fixture(.2));
        var wire = p.Schematic!.Wires.Single() with { AreaMm2 = 100, Awg = null };
        var specification = SchematicWirePresentation.Resolve(p, wire);
        Assert.Equal(100, specification.AreaMm2);
        Assert.Null(specification.Awg);
    }

    [Fact]
    public void MovingLabelDoesNotChangeWireGeometryAndSurvivesReopen()
    {
        var p = SchematicWireIdentificationTests.Draw(SchematicWireIdentificationTests.Fixture(.2));
        var wire = p.Schematic!.Wires.Single();
        var next = new SchematicAuthoringService().SetWireLabelPosition(p, wire.WireId, new(100, 80));
        var restored = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        Assert.Equal(new SchematicPoint(100, 80), restored.Schematic!.Wires.Single().LabelPosition);
        Assert.Equal(wire.Points, restored.Schematic.Wires.Single().Points);
        Assert.Null(p.Schematic.Wires.Single().LabelPosition);
    }
}
