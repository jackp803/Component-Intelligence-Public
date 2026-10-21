using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableCadPortBindingTests
{
    [Fact]
    public void UnchangedBindingPreservesRevisionAndExpandedPort()
    {
        var (project, contacts) = BoundProject();
        var service = new SchematicAuthoringService();
        var symbol = project.Schematic!.Symbols[0];
        var portId = project.Cables[0].ArchivedCable!.Ports[0].PortId;
        project = service.TogglePortCollapsed(project, symbol.SymbolId, portId);
        project.Schematic!.Symbols[0] = project.Schematic.Symbols[0] with { AssetRevision = "approved-r1" };
        var updated = service.SetCableContactBindings(project, symbol.SymbolId, contacts);
        Assert.Equal("approved-r1", updated.Schematic!.Symbols[0].AssetRevision);
        Assert.DoesNotContain(portId, updated.Schematic.Symbols[0].CollapsedPortIds);
    }

    [Fact]
    public void MovingCadPortReleasesOnlyItsOwnBinding()
    {
        var (project, _) = BoundProject();
        var symbol = project.Schematic!.Symbols[0];
        var portId = project.Cables[0].ArchivedCable!.Ports[0].PortId;
        var updated = new SchematicAuthoringService().MovePortToEdge(project, symbol.SymbolId, portId, "Top", 30);
        Assert.DoesNotContain(updated.Schematic!.Symbols[0].CadPortBindings, b => b.PortId == portId);
        Assert.Single(updated.Schematic.Symbols[0].CadPortBindings);
        Assert.Equal(20, SchematicPortPresentation.ConnectionPoint(updated, updated.Schematic.Symbols[0], portId).Y);
        Assert.Empty(updated.Cables[0].ArchivedCable!.Mapping);
    }

    private static (ElectricalProject, Dictionary<string, string>) BoundProject()
    {
        var template = CableManufacturingEditorTests.Template();
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 80, Height = 40,
            MillimetresPerUnit = 1, Primitives = [new() { Kind = "LINE", Start = new(0, 20), End = new(80, 20) }],
            ConnectionPoints = [new("CAD-P1", "", new(0, 20), Direction: "Left"), new("CAD-P2", "", new(80, 20), Direction: "Right")] };
        var contacts = new Dictionary<string, string> { ["P1"] = "CAD-P1", ["P2"] = "CAD-P2" };
        var project = new ElectricalProject { ProjectId = "port-cad", Schematic = new() { Pages = [new() { PageId = "page", Title = "Cable" }] } };
        return (new SchematicAuthoringService().AddArchivedCable(project, template, CableConstructionType.Unknown,
            geometry, contacts, "page", new(20, 20)), contacts);
    }

    [Fact]
    public void PortCadBindingUsesOriginalContactWithoutInferringPinMapping()
    {
        var template = CableManufacturingEditorTests.Template();
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 80, Height = 40,
            MillimetresPerUnit = 1, Primitives = [new() { Kind = "LINE", Start = new(0, 20), End = new(80, 20) }],
            ConnectionPoints = [new("CAD-P1", "", new(0, 20), Direction: "Left"), new("CAD-P2", "", new(80, 20), Direction: "Right")] };
        var project = new ElectricalProject { ProjectId = "port-cad", Schematic = new() { Pages = [new() { PageId = "page", Title = "Cable" }] } };
        project = new SchematicAuthoringService().AddArchivedCable(project, template, CableConstructionType.Unknown,
            geometry, new Dictionary<string, string> { ["P1"] = "CAD-P1", ["P2"] = "CAD-P2" }, "page", new(20, 20));
        var cable = Assert.Single(project.Cables);
        var symbol = Assert.Single(project.Schematic!.Symbols);
        Assert.Equal(new SchematicPoint(20, 40), SchematicPortPresentation.ConnectionPoint(project, symbol, cable.ArchivedCable!.Ports[0].PortId));
        Assert.Equal(4, symbol.Anchors.Count);
        Assert.Empty(cable.ArchivedCable.Mapping);
        Assert.False(cable.ArchivedCable.MappingConfirmed);
        Assert.All(symbol.Anchors, a => Assert.False(a.Confirmed));
        SchematicAuthoringService.Validate(project);
    }

    [Fact]
    public void ArchiveAcceptsExactPortBindingWithoutRequiringPinCadContacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "port-cad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = new SymbolArchiveRepository(root);
            repository.Save(new() { CableTemplates = [new() { Template = CableManufacturingEditorTests.Template(),
                AssetPath = "wiring.dwg", MillimetresPerUnit = 1,
                ContactBindings = [new() { EngineeringEndpointId = "P1", ConnectionPointId = "CAD-P1" }] }] });
            var loaded = repository.Load();
            Assert.Equal("P1", Assert.Single(Assert.Single(loaded.CableTemplates).ContactBindings).EngineeringEndpointId);
            Assert.Equal("ci-symbol-archive.v6", loaded.SchemaVersion);
        }
        finally { Directory.Delete(root, true); }
    }
}
