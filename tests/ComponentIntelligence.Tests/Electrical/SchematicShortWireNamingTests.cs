using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicShortWireNamingTests
{
    [Fact]
    public void AutomaticNameUsesShortComponentReferenceAndNoPortOrPinName()
    {
        var p = Project();
        p = SchematicWireIdentificationTests.Draw(p);
        Assert.Equal("Valve1_1", p.Schematic!.Wires[0].Designation);
        Assert.Equal("EnergyValveControlValve1", p.Components[0].ReferenceDesignator);
        Assert.Equal("CONTROL_LEADS", p.Components[0].Ports[0].Name);
    }

    [Fact]
    public void SeparatePortsCannotProduceDuplicateShortNumbers()
    {
        var p = SchematicWireIdentificationTests.Draw(Project());
        p.Components[0].Ports.Add(new() { PortId = "OTHER", Name = "AUX_SWITCH_LEADS", Pins = [new() { PinId = "OTHER.1", PinNumber = "1" }] });
        p.Schematic!.Symbols[0] = p.Schematic.Symbols[0] with { Anchors = [.. p.Schematic.Symbols[0].Anchors,
            new() { EndpointId = "OTHER.1", Position = new(40, 25), Direction = "Right" }] };
        p = new SchematicAuthoringService().DrawWire(p, "page", SchematicAttachment.Pin("S1", "OTHER.1"), SchematicAttachment.Free(), [new(60, 45), new(120, 45)]);
        Assert.Equal(new[] { "Valve1_1", "Valve1_2" }, p.Schematic!.Wires.Select(w => w.Designation));
    }

    [Fact]
    public void DeletionAndReloadDoNotRecycleExistingOrRetiredWireNumbers()
    {
        var service = new SchematicAuthoringService();
        var p = SchematicWireIdentificationTests.Draw(Project());
        p = service.DeleteWire(p, p.Schematic!.Wires[0].WireId);
        p = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(p))!;
        p = SchematicWireIdentificationTests.Draw(p);
        Assert.Equal("Valve1_2", p.Schematic!.Wires[0].Designation);
    }

    [Fact]
    public void RecognizedLegacyAutomaticNameIsShortenedButManualAndLockedNamesArePreserved()
    {
        foreach (var state in new[] { "auto", "manual", "locked" })
        {
            var p = SchematicWireIdentificationTests.Draw(Project());
            const string old = "EnergyValveControlValve1_CONTROL_LEADS_1";
            p.Schematic!.WireNamingReferences["io"] = "EnergyValveControlValve1";
            p.Schematic.Wires[0] = p.Schematic.Wires[0] with { Designation = state == "manual" ? "1P24" : old,
                ManualDesignation = state == "manual", Locked = state == "locked" };
            p.Schematic.UsedWireDesignations.Clear(); p.Schematic.UsedWireDesignations.Add(p.Schematic.Wires[0].Designation!);
            var electrical = JsonSerializer.Serialize(p.Connections);
            SchematicWireIdentification.Reconcile(p);
            Assert.Equal(state == "auto" ? "Valve1_1" : state == "manual" ? "1P24" : old, p.Schematic.Wires[0].Designation);
            Assert.Equal(electrical, JsonSerializer.Serialize(p.Connections));
        }
    }

    [Fact]
    public void NumericPortNameCannotMakeANewShortWireNameLookLikeALegacyName()
    {
        var p = Project();
        p.Components[0].Ports[0].Name = "1";
        var service = new SchematicAuthoringService();
        p.Schematic!.Symbols[0] = p.Schematic.Symbols[0] with { CollapsedPortIds = ["X1"] };
        var start = SchematicPortPresentation.ConnectionPoint(p, p.Schematic!.Symbols[0], "X1");
        p = service.DrawWire(p, "page", SchematicAttachment.Port("S1", "X1"), SchematicAttachment.Free(), [start, new(120, start.Y)]);
        var name = p.Schematic!.Wires[0].Designation;
        p = service.RefreshAutomaticWireNames(p);
        Assert.Equal("Valve1_1", name);
        Assert.Equal(name, p.Schematic!.Wires[0].Designation);
    }

    private static ElectricalProject Project()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].TypeKey = "Energy Valve / Control Valve";
        p.Components[0].ReferenceDesignator = "EnergyValveControlValve1";
        p.Components[0].Ports[0].Name = "CONTROL_LEADS";
        return p;
    }
}
