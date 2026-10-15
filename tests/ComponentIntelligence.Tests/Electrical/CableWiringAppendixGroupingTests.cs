using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableWiringAppendixGroupingTests
{
    [Fact]
    public void ConnectorClassificationOmitsOnlyItsAutomaticAppendix()
    {
        var project = Project();
        var node = JsonSerializer.SerializeToNode(project.Cables[0].ArchivedCable!.Template)!;
        node["LibraryItemKind"] = 1;
        project.Cables[0].ArchivedCable = project.Cables[0].ArchivedCable! with { Template = node.Deserialize<ArchivedCableTemplate>()! };
        var before = JsonSerializer.Serialize(project);
        var plan = CableWiringAppendixPlan.Build(project);
        Assert.Single(plan.MainPages); Assert.Equal(2, plan.MainProject.Schematic!.Symbols.Count);
        Assert.Single(plan.Sections); Assert.Equal(project.Cables[1].CableInstanceId, plan.Sections[0].CableInstanceId);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void EqualWiringHasOneDiagramDespiteIndependentMetadataAndRuntimeIds()
    {
        var project = Project(); var before = JsonSerializer.Serialize(project);
        var plan = CableWiringAppendixPlan.Build(project);
        var section = Assert.Single(plan.Sections);
        var ids = JsonSerializer.SerializeToNode(section)!["CableInstanceIds"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(project.Cables.Select(c => c.CableInstanceId), ids);
        Assert.Equal(2, section.SourceLinks.Count);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal(500, plan.MainProject.Cables[0].ProvidedLengthMm); Assert.Equal(1200, plan.MainProject.Cables[1].ProvidedLengthMm);
        Assert.Equal("Upper sensor", plan.MainProject.Cables[0].Notes); Assert.Equal("Lower sensor", plan.MainProject.Cables[1].Notes);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("short")]
    [InlineData("pin-number")]
    [InlineData("coding")]
    [InlineData("function")]
    [InlineData("specification")]
    public void SamePortsDoNotMergeDifferentElectricalOrPrintedSpecifications(string change)
    {
        var project = Project(); var cable = project.Cables[1]; var binding = cable.ArchivedCable!;
        if (change is "mapping" or "short")
            cable.ArchivedCable = binding with { Mapping = change == "mapping" ? [new("a1", "b2"), new("a2", "b1")] : [..binding.Mapping, new("a1", "a2")] };
        if (change == "pin-number") binding.Ports[0].Pins[0].PinNumber = "5";
        if (change == "coding") binding.Ports[0].Connector!.Coding = "B";
        if (change == "function") binding.Ports[0].Pins[0].Function = "PE";
        if (change == "specification") cable.Specification = "AWG18 / shielded";
        Assert.Equal(2, CableWiringAppendixPlan.Build(project).Sections.Count);
    }

    [Fact]
    public void ReorderedAndReversedEdgesDoNotCreateDuplicateDiagrams()
    {
        var project = Project(); var binding = project.Cables[1].ArchivedCable!;
        project.Cables[1].ArchivedCable = binding with { Mapping = binding.Mapping.AsEnumerable().Reverse()
            .Select(m => new CablePinMapping(m.ToSourcePinId, m.FromSourcePinId)).ToList() };
        Assert.Single(CableWiringAppendixPlan.Build(project).Sections);
    }

    [Fact]
    public void UnknownEmptyMappingsAreNotClaimedToBeEquivalent()
    {
        var project = Project();
        foreach (var cable in project.Cables) cable.ArchivedCable = cable.ArchivedCable! with { Mapping = [], MappingConfirmed = false };
        Assert.Equal(2, CableWiringAppendixPlan.Build(project).Sections.Count);
    }

    [Fact]
    public void IdenticalPinnedDraftsShareTheirRecordedDiagramWithoutConfirmingUnknownPins()
    {
        var project = Project(partial: true); var before = JsonSerializer.Serialize(project);
        Assert.Single(CableWiringAppendixPlan.Build(project).Sections);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.All(project.Cables, c => Assert.False(c.ArchivedCable!.MappingConfirmed));
        var binding = project.Cables[1].ArchivedCable!;
        project.Cables[1].ArchivedCable = binding with { Template = binding.Template with { TemplateId = "other-source" } };
        Assert.Equal(2, CableWiringAppendixPlan.Build(project).Sections.Count);
    }

    [Fact]
    public void CompleteWiringWithDifferentSourceIdsCanShareOneDiagram()
    {
        var project = Project();
        var json = JsonSerializer.Serialize(project.Cables[1]);
        foreach (var id in new[] { "a1", "a2", "b1", "b2", "port-a", "port-b" })
            json = json.Replace("\"" + id + "\"", "\"other-" + id + "\"");
        project.Cables[1] = JsonSerializer.Deserialize<CableInstance>(json)!;
        Assert.Single(CableWiringAppendixPlan.Build(project).Sections);
    }

    [Fact]
    public void ExistingPlacedCableCanBeClassifiedWithoutChangingOtherInstancesOrWiring()
    {
        var project = Project(); var before = JsonSerializer.Serialize(project);
        var method = typeof(SchematicAuthoringService).GetMethod("SetArchivedCableItemKind");
        Assert.NotNull(method);
        var next = (ElectricalProject)method.Invoke(new SchematicAuthoringService(),
            [project, project.Cables[0].CableInstanceId, CableLibraryItemKind.Connector])!;
        Assert.Equal(CableLibraryItemKind.Connector, CableWiringEquivalence.Kind(next.Cables[0]));
        Assert.Equal(CableLibraryItemKind.Cable, CableWiringEquivalence.Kind(next.Cables[1]));
        Assert.Equal(JsonSerializer.Serialize(project.Cables[0].ArchivedCable!.Mapping), JsonSerializer.Serialize(next.Cables[0].ArchivedCable!.Mapping));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Single(CableWiringAppendixPlan.Build(next).Sections);
        var reopened = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        Assert.Equal(CableLibraryItemKind.Connector, CableWiringEquivalence.Kind(reopened.Cables[0]));
    }

    internal static ElectricalProject Project(bool partial = false)
    {
        ComponentPort Port(string id, string name) => new() { PortId = "port-" + id, Name = name,
            Connector = new() { ConnectorId = "connector-" + id, Family = "M12", Coding = "A", Gender = ConnectorGender.Male, PinCount = 2 },
            Pins = Enumerable.Range(1, 2).Select(i => new ComponentPin { PinId = id + i, PinNumber = i.ToString(), Function = i == 1 ? "COM" : "24V" }).ToList() };
        var template = new ArchivedCableTemplate { TemplateId = "shared", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = [Port("a", "P1"), Port("b", "P2")], Mapping = [new("a1", "b1"), new("a2", "b2")],
            MappingConfirmed = !partial, MappingRevision = "r1", MappingEvidence = "synthetic test" };
        if (partial) template.Ports[0].Pins.Add(new() { PinId = "a3", PinNumber = "3" });
        var cables = Enumerable.Range(1, 2).Select(i => {
            var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
            cable.ReferenceDesignator = "C" + i; cable.ProvidedLengthMm = i == 1 ? 500 : 1200;
            cable.Notes = i == 1 ? "Upper sensor" : "Lower sensor"; return cable; }).ToList();
        return new() { ProjectId = "grouping", Cables = cables, Schematic = new() {
            Pages = [new() { PageId = "sheet", Title = "Main" }],
            Symbols = cables.Select((c, i) => new SchematicSymbol { SymbolId = "body" + i, CableInstanceId = c.CableInstanceId,
                PageId = "sheet", Position = new(20, 20 + i * 60), Geometry = new() { SourceSha256 = template.AssetSha256, Width = 40, Height = 30 } }).ToList() } };
    }
}
