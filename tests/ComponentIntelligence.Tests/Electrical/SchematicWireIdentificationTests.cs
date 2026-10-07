using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicWireIdentificationTests
{
    [Fact]
    public void KnownSensorLoadGetsSmallMetricProposalWithoutUsingSourceCapacity()
    {
        var p = Draw(Fixture(.2));
        var specification = SchematicWirePresentation.Resolve(p, p.Schematic!.Wires[0]);
        Assert.Equal(.5, specification.AreaMm2);
        Assert.Equal(.5, Assert.Single(p.Connections).ConductorAreaMm2);
    }

    [Fact]
    public void LargerKnownLoadSelectsLargerProposal()
    {
        var p = Draw(Fixture(8));
        Assert.Equal(1, SchematicWirePresentation.Resolve(p, p.Schematic!.Wires[0]).AreaMm2);
    }

    [Fact]
    public void UnknownSensorHasDraftDefaultButUnknownMotorDoesNot()
    {
        var sensor = Draw(Fixture(null));
        Assert.Equal(.5, SchematicWirePresentation.Resolve(sensor, sensor.Schematic!.Wires[0]).AreaMm2);
        var motor = Fixture(null); motor.Components[1].TypeKey = "Motor";
        motor = Draw(motor);
        Assert.Null(SchematicWirePresentation.Resolve(motor, motor.Schematic!.Wires[0]).AreaMm2);
    }

    [Fact]
    public void GeneralWireHasWorkerFacingNameAndSpecification()
    {
        var p = Draw(Fixture(.2));
        var caption = SchematicCableLabelPresentation.Resolve(p, p.Schematic!.Wires[0]);
        Assert.NotNull(caption);
        Assert.Contains("IOLink1_X1_1", caption.Text);
        Assert.Contains("mm", caption.Text);
        Assert.Contains("AWG", caption.Text);
    }

    [Fact]
    public void ManualAwgAndNameSurviveGeometryEditsAndReload()
    {
        var author = new SchematicAuthoringService();
        var p = Draw(Fixture(.2)); var id = p.Schematic!.Wires[0].WireId;
        p = author.SetWireAwg(p, id, 18);
        var label = SchematicCableLabelPresentation.Resolve(p, p.Schematic.Wires[0]);
        Assert.NotNull(label);
        p = author.TransformSymbol(p, "S1", new(30, 20), 0);
        var restored = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(p))!;
        Assert.Equal(18, SchematicWirePresentation.Resolve(restored, restored.Schematic!.Wires[0]).Awg);
        Assert.Equal(label.Text, SchematicCableLabelPresentation.Resolve(restored, restored.Schematic.Wires[0])!.Text);
    }

    [Theory]
    [InlineData("DC", .1)]
    [InlineData("AC", null)]
    public void OnlyDcInputPowerCanDeriveMissingConsumption(string voltageType, double? expected)
    {
        var ir = new ComponentIR { Identity = new() { ComponentId = "S", Manufacturer = "TEST", Model = "TEST" },
            Power = new() { OperatingVoltage = new() { Type = voltageType, Min = 24, Max = 24 }, PowerConsumptionWatt = 2.4m },
            Pins = [new() { PinNumber = "1", Function = "L+", SignalType = "Power", Direction = "Input" }] };
        var instance = new ComponentProjectBridge().CreateInstance(ir, "S");
        Assert.Equal(expected, instance.Ports[0].Pins[0].Power!.RequiredCurrentAmp);
    }

    [Fact]
    public void MetricOverrideIsExactAndPairedDraftNamesAreShared()
    {
        var author = new SchematicAuthoringService(); var p = Fixture(.2);
        p.Schematic!.Pages.Add(new() { PageId = "second", Title = "Second" });
        p.Schematic.Symbols[1] = p.Schematic.Symbols[1] with { PageId = "second" };
        p = author.DrawWire(p, "page", SchematicAttachment.Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 25), new(110, 25)]);
        p = author.DrawWire(p, "second", SchematicAttachment.Free(), SchematicAttachment.Pin("S2", "B"), [new(110, 25), new(160, 25)]);
        p = author.ConnectAcrossPages(p, p.Schematic!.Wires[0].WireId, false, p.Schematic.Wires[1].WireId, true, "Supply");
        Assert.Single(p.Schematic!.Wires.Select(w => w.Designation).Distinct());
        p = author.SetWireArea(p, p.Schematic.Wires[0].WireId, .75);
        Assert.All(p.Schematic!.Wires, w => { Assert.Equal(.75, w.AreaMm2); Assert.True(w.ManualSpecification); });
        p = author.SetWireDesignation(p, p.Schematic.Wires[1].WireId, "Supply-A");
        Assert.All(p.Schematic!.Wires, w => Assert.Equal("Supply-A", w.Designation));
        p = author.UseAutomaticWireSize(p, p.Schematic.Wires[0].WireId);
        Assert.All(p.Schematic!.Wires, w => Assert.Equal(.5, w.AreaMm2));
    }

    [Fact]
    public void BranchLoadSubtotalResizesParentWithoutMergingNamesOrGuessingContinuity()
    {
        var p = Draw(Fixture(2)); var parent = p.Schematic!.Wires[0];
        var sensor = new ComponentInstance { ComponentInstanceId = "second", ComponentDefinitionId = "d2", TypeKey = "Sensor",
            Ports = [new() { PortId = "P2", Name = "P2", Pins = [new() { PinId = "C", PinNumber = "1", Layer = ElectricalLayer.Power,
                Power = new() { Role = PowerRole.Input, RequiredCurrentAmp = 2, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } }] }] };
        p.Components.Add(sensor);
        p.Schematic.Symbols.Add(new() { SymbolId = "S3", ComponentInstanceId = "second", PageId = "page", Position = new(100, 60),
            Width = 40, Height = 40, Anchors = [new() { EndpointId = "C", Position = new(0, 5), Direction = "Left" }] });
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Junction(parent.WireId), SchematicAttachment.Pin("S3", "C"),
            [new(100, 25), new(100, 65)]);
        Assert.All(p.Schematic!.Wires, w => Assert.Equal(.75, w.AreaMm2));
        Assert.Equal(2, p.Schematic.Wires.Select(w => w.Designation).Distinct().Count());
        Assert.Single(p.Connections);
    }

    [Fact]
    public void InvalidatingLoadDoesNotRetainStaleAutomaticArea()
    {
        var p = Draw(Fixture(8));
        p.Components[1].TypeKey = "Motor";
        p.Components[1].Ports[0].Pins[0].Power = new() { Role = PowerRole.Input };
        p = new SchematicAuthoringService().TransformSymbol(p, "S1", new(25, 20), 0);
        Assert.Null(p.Schematic!.Wires[0].AreaMm2);
        Assert.NotNull(p.Schematic.Wires[0].SizingProposal);
    }

    [Fact]
    public void SensorDefaultCannotExceedKnownTerminationRange()
    {
        var p = Fixture(null);
        p.Components[1].Ports[0].Connector = new() { ConnectorId = "small", Family = "Custom", MaxTerminationAreaMm2 = .25 };
        p = Draw(p);
        Assert.Null(p.Schematic!.Wires[0].AreaMm2);
        Assert.NotNull(p.Schematic.Wires[0].SizingProposal);
    }

    [Fact]
    public void KnownPhysicalLengthInfluencesProposalButMovingDrawingDoesNot()
    {
        var p = Draw(Fixture(2));
        p.Connections[0].ProvidedLengthMm = 10000; p.Connections[0].LengthSource = CableLengthSource.User;
        p.Connections[0].MaxVoltageDropPercent = 3;
        p = new SchematicAuthoringService().TransformSymbol(p, "S1", new(25, 20), 0);
        Assert.True(p.Schematic!.Wires[0].AreaMm2 > .5);
        var area = p.Schematic.Wires[0].AreaMm2;
        p = new SchematicAuthoringService().TransformSymbol(p, "S1", new(30, 20), 0);
        Assert.Equal(area, p.Schematic!.Wires[0].AreaMm2);
    }

    [Fact]
    public void JoiningDifferentManualMetricDraftsIsRejectedWithoutMutation()
    {
        var p = DraftPair(); var author = new SchematicAuthoringService();
        p = author.SetWireArea(p, p.Schematic!.Wires[0].WireId, .75);
        p = author.SetWireArea(p, p.Schematic!.Wires[1].WireId, 1.5);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => Join(p));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void JoiningMatchingManualMetricDraftsUpdatesPhysicalConnection()
    {
        var p = DraftPair(); var author = new SchematicAuthoringService();
        foreach (var wire in p.Schematic!.Wires.ToArray()) p = author.SetWireArea(p, wire.WireId, .75);
        p = Join(p);
        Assert.Equal(.75, Assert.Single(p.Connections).ConductorAreaMm2);
    }

    [Fact]
    public void IncompletePairedAwgEditClearsBothMetricOverrides()
    {
        var p = DraftPair(); var author = new SchematicAuthoringService();
        p.Schematic!.Wires[1] = p.Schematic.Wires[1] with { End = SchematicAttachment.Free() };
        p = Join(p);
        p = author.SetWireArea(p, p.Schematic!.Wires[0].WireId, .75);
        p = author.SetWireAwg(p, p.Schematic!.Wires[1].WireId, 18);
        Assert.All(p.Schematic!.Wires, w => { Assert.Equal(18, w.Awg); Assert.Null(w.AreaMm2); });
    }

    [Fact]
    public void SplittingDraftContinuationDoesNotDuplicateWireNames()
    {
        var p = DraftPair(); var author = new SchematicAuthoringService();
        p.Schematic!.Wires[1] = p.Schematic.Wires[1] with { End = SchematicAttachment.Free() };
        p = Join(p);
        p = author.DeleteContinuation(p, p.Schematic!.Continuations[0].Source.MarkerId);
        Assert.Equal(2, p.Schematic!.Wires.Select(w => w.Designation).Distinct().Count());
    }

    [Fact]
    public void CatalogBindingClearsOldAutomaticSizeAndUsesCatalogConnectionArea()
    {
        var p = Draw(Fixture(.2));
        p.Connections[0].Kind = ConnectionKind.Cable;
        p.Connections[0].CableInstanceId = "cable"; p.Connections[0].CableCoreId = "core";
        p.Connections[0].ConductorAreaMm2 = .34;
        p = new SchematicAuthoringService().TransformSymbol(p, "S1", new(25, 20), 0);
        Assert.Null(p.Schematic!.Wires[0].AreaMm2);
        Assert.Equal(.34, SchematicWirePresentation.Resolve(p, p.Schematic.Wires[0]).AreaMm2);
    }

    [Fact]
    public void AutomaticComponentNumbersAndSignedPinsSurviveReload()
    {
        var p = Fixture(.2); p.Components[0].ReferenceDesignator = null;
        p.Components[0].Ports[0].Pins[0].PinNumber = "+";
        p = Draw(p); var name = p.Schematic!.Wires[0].Designation;
        Assert.Equal("IOLink1_X1_+", name);
        var clone = new ComponentInstance { ComponentInstanceId = "io-second", ComponentDefinitionId = "io-def", TypeKey = "IO-Link Master",
            Ports = [new() { PortId = "X1-second", Name = "X1", Pins = [new() { PinId = "A-second", PinNumber = "+" }] }] };
        p.Components.Add(clone);
        p.Schematic.Symbols.Add(p.Schematic.Symbols[0] with { SymbolId = "S3", ComponentInstanceId = clone.ComponentInstanceId,
            Position = new(20, 90), Anchors = [new() { EndpointId = "A-second", Position = new(40, 5), Direction = "Right" }] });
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin("S3", "A-second"), SchematicAttachment.Free(), [new(60, 95), new(100, 95)]);
        Assert.Equal("IOLink2_X1_+", p.Schematic!.Wires[1].Designation);
        p = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(p))!;
        p = new SchematicAuthoringService().TransformSymbol(p, "S1", new(25, 20), 0);
        Assert.Equal(name, p.Schematic!.Wires[0].Designation);
    }

    private static ElectricalProject DraftPair()
    {
        var p = Fixture(.2); var author = new SchematicAuthoringService();
        p.Schematic!.Pages.Add(new() { PageId = "second", Title = "Second" });
        p.Schematic.Symbols[1] = p.Schematic.Symbols[1] with { PageId = "second" };
        p = author.DrawWire(p, "page", SchematicAttachment.Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 25), new(110, 25)]);
        return author.DrawWire(p, "second", SchematicAttachment.Free(), SchematicAttachment.Pin("S2", "B"), [new(110, 25), new(160, 25)]);
    }

    private static ElectricalProject Join(ElectricalProject p) => new SchematicAuthoringService().ConnectAcrossPages(p,
        p.Schematic!.Wires[0].WireId, false, p.Schematic.Wires[1].WireId, true, "Supply");

    [Fact]
    public void ClearingAutomaticGaugeCannotPromoteProposalToManualSize()
    {
        var p = Draw(Fixture(.2));
        p = new SchematicAuthoringService().SetWireAwg(p, p.Schematic!.Wires[0].WireId, null);
        Assert.Null(SchematicWirePresentation.Resolve(p, p.Schematic!.Wires[0]).AreaMm2);
    }

    [Fact]
    public void CatalogAreaCannotUseAnUnverifiedExactManualAwg()
    {
        var p = Draw(Fixture(.2));
        p = new SchematicAuthoringService().SetWireAwg(p, p.Schematic!.Wires[0].WireId, 18);
        p.Connections[0].Kind = ConnectionKind.Cable; p.Connections[0].ConductorAreaMm2 = .34;
        var specification = SchematicWirePresentation.Resolve(p, p.Schematic!.Wires[0]);
        Assert.NotEqual(18, specification.Awg);
        Assert.True(specification.AwgIsApproximate);
    }

    [Fact]
    public void IncompleteVoltageDropInputsDoNotHideInvalidEngineeringData()
    {
        var author = new SchematicAuthoringService(); var p = Draw(Fixture(.2));
        p.Connections[0].ConductorMaterial = ConductorMaterial.Aluminum;
        p = author.TransformSymbol(p, "S1", new(25, 20), 0);
        Assert.Null(p.Schematic!.Wires[0].AreaMm2);
        p.Connections[0].ConductorMaterial = ConductorMaterial.Copper;
        p.Connections[0].ProvidedLengthMm = -1;
        p = author.TransformSymbol(p, "S1", new(30, 20), 0);
        Assert.Null(p.Schematic!.Wires[0].AreaMm2);
    }

    [Fact]
    public void CollapsedPortDoesNotHideInvalidCurrent()
    {
        var p = Fixture(-1);
        p.Schematic!.Symbols[0] = p.Schematic.Symbols[0] with { CollapsedPortIds = ["X1"] };
        var start = SchematicPortPresentation.ConnectionPoint(p, p.Schematic.Symbols[0], "X1");
        var end = SchematicAuthoringService.AnchorPoint(p.Schematic.Symbols[1], "B");
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Port("S1", "X1"),
            SchematicAttachment.Pin("S2", "B"), start.Y == end.Y ? [start, end] : [start, new(end.X, start.Y), end]);
        Assert.Null(p.Schematic!.Wires[0].AreaMm2);
    }

    internal static ElectricalProject Fixture(double? current)
    {
        var p = new ElectricalProject { ProjectId = "wire-test", Schematic = new() { Pages = [new() { PageId = "page", Title = "Test" }] } };
        p.Components.Add(new() { ComponentInstanceId = "io", ComponentDefinitionId = "io-def", TypeKey = "IO-Link Master", ReferenceDesignator = "IOLink1",
            Ports = [new() { PortId = "X1", Name = "X1", Pins = [new() { PinId = "A", PinNumber = "1", Layer = ElectricalLayer.Power,
                Power = new() { Role = PowerRole.Source, MaxCurrentAmp = 20, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } }] }] });
        p.Components.Add(new() { ComponentInstanceId = "sensor", ComponentDefinitionId = "sensor-def", TypeKey = "Sensor",
            Ports = [new() { PortId = "P1", Name = "P1", Pins = [new() { PinId = "B", PinNumber = "1", Layer = ElectricalLayer.Power,
                Power = new() { Role = PowerRole.Input, RequiredCurrentAmp = current, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } } }] }] });
        p.Schematic.Symbols.Add(new() { SymbolId = "S1", ComponentInstanceId = "io", PageId = "page", Width = 40, Height = 40, Position = new(20, 20),
            Anchors = [new() { EndpointId = "A", Position = new(40, 5), Direction = "Right" }] });
        p.Schematic.Symbols.Add(new() { SymbolId = "S2", ComponentInstanceId = "sensor", PageId = "page", Width = 40, Height = 40, Position = new(160, 20),
            Anchors = [new() { EndpointId = "B", Position = new(0, 5), Direction = "Left" }] });
        return p;
    }

    internal static ElectricalProject Draw(ElectricalProject p) => new SchematicAuthoringService().DrawWire(p, "page",
        SchematicAttachment.Pin("S1", "A"), SchematicAttachment.Pin("S2", "B"), [new(60, 25), new(160, 25)]);
}
