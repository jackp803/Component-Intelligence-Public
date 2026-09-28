using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicWireEvidenceTests
{
    [Fact]
    public void UsesOnlyExactTypedEndpointAndPreservesZeroVoltReturn()
    {
        var (p, w) = Fixture(Polarity.Positive, 24);
        var evidence = SchematicWireEvidence.Resolve(p, w);
        Assert.Equal("DC_POSITIVE", evidence.Category);
        Assert.Equal("#B42318", evidence.ColorHex);
        Assert.Contains("24 V DC", evidence.Description);
        p.Components[0].Ports[0].Pins[0].Power = new() { Polarity = Polarity.Return,
            Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 0 } };
        evidence = SchematicWireEvidence.Resolve(p, w);
        Assert.Equal("UNCLASSIFIED", evidence.Category);
        Assert.Contains("0 V DC", evidence.Description);
        Assert.DoesNotContain("Neutral", evidence.Description);
        Assert.Empty(p.Nets);
    }

    [Fact]
    public void DoesNotGuessFromNameOrWholePortOrMergeConflictingEndpoints()
    {
        var (p, w) = Fixture(Polarity.Positive, 24);
        p.Components[0].Ports[0].Pins[0].Power = null;
        p.Components[0].Ports[0].Pins[0].PinName = "24V positive";
        Assert.Equal("UNCLASSIFIED", SchematicWireEvidence.Resolve(p, w).Category);
        p.Components[0].Ports[0].Pins[0].Power = new() { Polarity = Polarity.Positive, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 24 } };
        Assert.Equal("UNCLASSIFIED", SchematicWireEvidence.Resolve(p, w with { Start = SchematicAttachment.Pin("S", "PORT") }).Category);
        p.Components[0].Ports[0].Pins.Add(new() { PinId = "RETURN", PinNumber = "2", Power = new() { Polarity = Polarity.Negative, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = 0 } } });
        Assert.Equal("CONFLICT", SchematicWireEvidence.Resolve(p, w with { End = SchematicAttachment.Pin("S", "RETURN") }).Category);
    }

    [Fact]
    public void ExplicitPairedDraftCarriesEndpointDisplayEvidenceWithoutInventingNet()
    {
        var (p, first) = Fixture(Polarity.Positive, 24);
        first = first with { End = SchematicAttachment.Marker("M1") };
        var second = new SchematicWire { WireId = "W2", PageId = "P2", Start = SchematicAttachment.Marker("M2") };
        p.Schematic = new() { Wires = [first, second], Continuations = [new() { ContinuationId = "PAIR", Signal = "S",
            Source = new() { MarkerId = "M1", PageId = "P", Position = new(30, 40) },
            Destination = new() { MarkerId = "M2", PageId = "P2", Position = new(40, 50) } }] };
        Assert.Equal("DC_POSITIVE", SchematicWireEvidence.Resolve(p, second).Category);
        Assert.Empty(p.Connections); Assert.Empty(p.Nets);
        p.Schematic.Continuations.Clear();
        Assert.Equal("UNCLASSIFIED", SchematicWireEvidence.Resolve(p, second).Category);
    }

    private static (ElectricalProject, SchematicWire) Fixture(Polarity polarity, double voltage)
    {
        var p = new ElectricalProject { ProjectId = "TEST" };
        p.Components.Add(new() { ComponentInstanceId = "C", ComponentDefinitionId = "SOURCE", TypeKey = "TEST",
            Ports = [new() { PortId = "PORT", Name = "Output", Pins = [new() { PinId = "PIN", PinNumber = "1", Power = new()
            { Polarity = polarity, Voltage = new() { Type = VoltageType.Dc, NominalVoltage = voltage } } }] }] });
        return (p, new() { WireId = "W", PageId = "P", Start = SchematicAttachment.Pin("S", "PIN") });
    }
}
