using ComponentIntelligence.Electrical.Schematic;
using System.Text.Json;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCompactWireLabelTests
{
    [Fact]
    public void LongDescriptionUsesExplicitPinNameAndKeepsGaugeOffTheWire()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        foreach (var pin in p.Components.SelectMany(c => c.Ports).SelectMany(port => port.Pins))
        {
            pin.Function = "Common for 24 V supply and non-isolated communication reference";
            pin.PinName = "Com / black";
        }
        p = SchematicWireIdentificationTests.Draw(p);
        var label = SchematicCableLabelPresentation.Resolve(p, p.Schematic!.Wires.Single())!;
        Assert.Equal(p.Schematic.Wires.Single().Designation + " · Com", label.Text);
        Assert.DoesNotContain("AWG", label.Text);
        Assert.Contains("Common for 24 V", label.FullText);
        Assert.Contains("IOLink1_X1", label.FullText);
    }

    [Fact]
    public void GroupProjectionIsExactAndDoesNotMergeElectricalIdentities()
    {
        var p = SchematicWireIdentificationTests.Draw(SchematicWireIdentificationTests.Fixture(.2));
        var wire = p.Schematic!.Wires.Single();
        p.Schematic.Wires.AddRange([
            wire with { WireId = "same", ConnectionId = null },
            wire with { WireId = "near", ConnectionId = null, AreaMm2 = .50001 },
            wire with { WireId = "manual", ConnectionId = null, SizingProposal = null, ManualSpecification = true },
            wire with { WireId = "unknown", ConnectionId = null, SizingProposal = null, Awg = null, AreaMm2 = null }]);
        var before = JsonSerializer.Serialize(p);
        var entries = SchematicWireLegendPresentation.Entries(p);
        var groups = SchematicWireLegendPresentation.Groups(entries);
        Assert.Equal(4, groups.Count);
        Assert.Equal(2, groups[0].Entries.Count);
        Assert.Equal("W01", entries[0].Index);
        Assert.Equal("W05", entries[4].Index);
        Assert.Equal(.50001, groups[1].Specification.AreaMm2);
        Assert.Contains("0.5 mm²", groups[1].Specification.Text);
        Assert.NotEqual(groups[0].Specification.Status, groups[2].Specification.Status);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        var reopened = JsonSerializer.Deserialize<ComponentIntelligence.Electrical.Domain.ElectricalProject>(before)!;
        Assert.Equal(entries.Select(e => e.Index), SchematicWireLegendPresentation.Entries(reopened).Select(e => e.Index));
    }

    [Fact]
    public void LongFunctionWithoutShortNameIsNotInventedOrLost()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].Ports[0].Pins[0].Function = new string('x', 160);
        p = SchematicWireIdentificationTests.Draw(p);
        var entry = SchematicWireLegendPresentation.Entries(p).Single();
        Assert.Equal("功能見明細", entry.ShortFunction);
        Assert.Contains(new string('x', 160), entry.Functions);
        Assert.Contains(entry.Designation, entry.FullText);
    }

    [Fact]
    public void PageProjectionDoesNotResolveHiddenPageSymbolMetadata()
    {
        var p = SchematicWireIdentificationTests.Draw(SchematicWireIdentificationTests.Fixture(.2));
        var wire = p.Schematic!.Wires.Single();
        p.Schematic.Symbols.Add(new() { SymbolId = "unresolved", ComponentInstanceId = "missing", PageId = "hidden" });
        p.Schematic.Wires.Add(wire with { WireId = "hidden-wire", PageId = "hidden", ConnectionId = null,
            Start = SchematicAttachment.Pin("unresolved", "missing-pin") });
        var entry = Assert.Single(SchematicWireLegendPresentation.Entries(p, "page"));
        Assert.Equal(1, entry.GetType().GetProperty("PageNumber")?.GetValue(entry));
    }

    [Fact]
    public void ConflictingRawFunctionColorsRemainAvailableAtEachEndpoint()
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].Ports[0].Pins[0].PinName = "Supply";
        p.Components[0].Ports[0].Pins[0].Function = "Supply / red";
        p.Components[1].Ports[0].Pins[0].PinName = "Return";
        p.Components[1].Ports[0].Pins[0].Function = "Return / black";
        p = SchematicWireIdentificationTests.Draw(p);
        var entry = SchematicWireLegendPresentation.Entries(p).Single();
        Assert.Equal("線色待確認", entry.Color);
        Assert.Contains("Supply / red", entry.Functions);
        Assert.Contains("Return / black", entry.Functions);
    }
}
