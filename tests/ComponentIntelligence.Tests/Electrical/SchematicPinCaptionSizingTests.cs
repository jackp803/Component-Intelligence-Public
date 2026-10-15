using System.Reflection;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPinCaptionSizingTests
{
    [Fact]
    public void SamePageCaptionUsesBothPinsWithoutOwnerPortNamesOrLongDesignation()
    {
        var p = Fixture(); var wire = p.Schematic!.Wires[0]; var before = JsonSerializer.Serialize(p);
        var entry = SchematicWireLegendPresentation.Entry(p, wire);
        Assert.Equal("1_COM → 1_COM", entry.Caption);
        Assert.Contains("MyValve", entry.FullText);
        Assert.Contains(wire.Designation!, entry.FullText);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Theory]
    [InlineData("Supply/control conductor", "1 / black", "供電/控制")]
    [InlineData("Shared auxiliary-switch contact; continuity is checked between S1-S2 or S1-S3 depending switch state.", "S1 / violet", "共點")]
    [InlineData("Auxiliary-switch contact paired with S1 in the alternate switch state.", "S3 / white", "接點")]
    [InlineData("Unknown application-specific function with a very long description without a verified shorthand.", "", "待辨識")]
    public void LongDescriptionsUseConservativeShortMeaningWithoutInventingPolarity(string function, string pinName, string expected)
    {
        var p = Fixture(); var pin = p.Components[0].Ports[0].Pins[0]; pin.Function = function; pin.PinName = pinName;
        p = new SchematicAuthoringService().RefreshAutomaticWireNames(p);
        var entry = SchematicWireLegendPresentation.Entries(p).Single();
        Assert.Contains("1_" + expected, entry.Caption);
        Assert.DoesNotContain("NO", entry.Caption); Assert.DoesNotContain("NC", entry.Caption);
        Assert.DoesNotContain("Shared auxiliary", entry.Caption);
        Assert.Contains(function, entry.FullText);
    }

    [Fact]
    public void CrossPageCaptionsCarryCustomReferencesOnBothSegments()
    {
        var p = Fixture(false); var author = new SchematicAuthoringService();
        p.Schematic!.Pages.Add(new() { PageId = "second", Title = "Second" });
        p.Schematic.Symbols[1] = p.Schematic.Symbols[1] with { PageId = "second" };
        p = author.DrawWire(p, "page", SchematicAttachment.Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 25), new(100, 25)]);
        p = author.DrawWire(p, "second", SchematicAttachment.Free(), SchematicAttachment.Pin("S2", "B"), [new(100, 25), new(160, 25)]);
        p = author.ConnectAcrossPages(p, p.Schematic!.Wires[0].WireId, false, p.Schematic.Wires[1].WireId, true, "COM");
        foreach (var wire in p.Schematic!.Wires)
        {
            var caption = SchematicWireLegendPresentation.Entry(p, wire).Caption;
            Assert.Contains("MyValve:1_COM", caption); Assert.Contains("CabinetIO:1_COM", caption);
        }
        p = Accept(p, p.Schematic.Wires[1].WireId);
        Assert.All(p.Schematic!.Wires, wire => { Assert.Equal(.5, wire.AreaMm2); Assert.Null(wire.SizingProposal); Assert.NotNull(wire.AdoptedSizingProposal); });
    }

    [Fact]
    public void AcceptSuggestionRetainsExactMetricSizeAndEvidenceThroughUndoAndReload()
    {
        var p = Fixture(); var wire = p.Schematic!.Wires[0]; var before = JsonSerializer.Serialize(p);
        var proposal = Assert.IsType<SchematicWireSizingProposal>(wire.SizingProposal);
        var next = Accept(p, wire.WireId); var selected = next.Schematic!.Wires[0];
        Assert.Equal(.5, selected.AreaMm2); Assert.Null(selected.Awg); Assert.Null(selected.SizingProposal);
        Assert.True(selected.ManualSpecification);
        var evidence = typeof(SchematicWire).GetProperty("AdoptedSizingProposal")!;
        Assert.Equal(proposal, evidence.GetValue(selected));
        var restored = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        Assert.Equal(proposal, evidence.GetValue(restored.Schematic!.Wires[0]));
        Assert.True(SchematicWirePresentation.Resolve(restored, restored.Schematic.Wires[0]).AwgIsApproximate);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        var history = new ProjectMutationHistory(); history.RecordBeforeMutation(p, "Adopt");
        Assert.True(history.TryUndo(next, out var undone, out _)); Assert.Equal(before, JsonSerializer.Serialize(undone));
    }

    [Fact]
    public void ManualOverrideAndAutomaticResetClearOldAdoptionEvidence()
    {
        var p = Fixture(); var id = p.Schematic!.Wires[0].WireId; var author = new SchematicAuthoringService();
        p = Accept(p, id);
        p = author.SetWireAwg(p, id, 18);
        Assert.Null(typeof(SchematicWire).GetProperty("AdoptedSizingProposal")!.GetValue(p.Schematic!.Wires[0]));
        Assert.Equal(18, p.Schematic.Wires[0].Awg);
        p = author.UseAutomaticWireSize(p, id); p = Accept(p, id);
        p = author.UseAutomaticWireSize(p, id);
        Assert.Null(typeof(SchematicWire).GetProperty("AdoptedSizingProposal")!.GetValue(p.Schematic!.Wires[0]));
        Assert.NotNull(p.Schematic.Wires[0].SizingProposal);
    }

    [Fact]
    public void AdoptionDoesNotSuppressReviewOrBecomeAnAlreadyReviewedBatchSource()
    {
        var p = Fixture(); var id = p.Schematic!.Wires[0].WireId;
        p = Accept(p, id);
        Assert.Contains(new ComponentIntelligence.Electrical.Validation.ElectricalProjectValidator().Validate(p).Results,
            issue => issue.RuleId == "RULE-WIRE-SIZE-PROPOSAL" && issue.RequiresPreExportReview);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => new SchematicAuthoringService().SetWireSpecifications(p, [id], referenceWireId: id));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void AnalogueAlternativeWithoutMpBusDoesNotInventBusSupport()
    {
        var p = Fixture(); p.Components[0].Ports[0].Pins[0].Function = "Selectable analogue output 0...10 V or 4...20 mA";
        p.Components[0].Ports[0].Pins[0].PinName = "AO / orange";
        var caption = SchematicWireLegendPresentation.Entry(p, p.Schematic!.Wires[0]).Caption;
        Assert.DoesNotContain("MP-Bus", caption); Assert.Contains("AO", caption);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("locked")]
    public void InvalidSuggestionCannotBeAdoptedOrMutateOriginal(string reason)
    {
        var p = Fixture(); var wire = p.Schematic!.Wires[0];
        if (reason == "missing") p = new SchematicAuthoringService().SetWireAwg(p, wire.WireId, 18);
        if (reason == "stale") p.Components[1].Ports[0].Pins[0].Power = p.Components[1].Ports[0].Pins[0].Power! with { RequiredCurrentAmp = 8 };
        if (reason == "locked") p.Schematic!.Wires[0] = wire with { Locked = true };
        var before = JsonSerializer.Serialize(p);
        var error = Assert.Throws<TargetInvocationException>(() => Accept(p, wire.WireId));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    private static ElectricalProject Accept(ElectricalProject p, string id)
    {
        var method = typeof(SchematicAuthoringService).GetMethod("AcceptWireSizeSuggestion");
        Assert.NotNull(method);
        return (ElectricalProject)method.Invoke(new SchematicAuthoringService(), [p, id])!;
    }

    private static ElectricalProject Fixture(bool draw = true)
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].ReferenceDesignator = "MyValve"; p.Components[1].ReferenceDesignator = "CabinetIO";
        foreach (var pin in p.Components.SelectMany(c => c.Ports).SelectMany(port => port.Pins))
        { pin.Function = "Common for 24 V supply and non-isolated communication reference"; pin.PinName = "COM / black"; }
        return draw ? SchematicWireIdentificationTests.Draw(p) : p;
    }
}
