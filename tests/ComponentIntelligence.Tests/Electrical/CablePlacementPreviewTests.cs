using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CablePlacementPreviewTests
{
    [Fact]
    public void CopyPreviewIsIsolatedAndDoesNotRelaxDropValidation()
    {
        var project = CableInstanceDuplicationTests.Project();
        var author = new SchematicAuthoringService(); var source = project.Schematic!.Symbols[0];
        var before = JsonSerializer.Serialize(project);
        var preview = author.PreviewCableDuplicate(project, source.SymbolId, "sheet", new(-100, -100));
        var copy = preview.Cables.Last();
        Assert.Equal(2, preview.Cables.Count);
        Assert.NotEqual(project.Cables[0].CableInstanceId, copy.CableInstanceId);
        Assert.Equal(JsonSerializer.Serialize(project.Schematic.Wires), JsonSerializer.Serialize(preview.Schematic!.Wires));
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(preview.Connections));
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(preview));
        Assert.Throws<InvalidOperationException>(() => author.DuplicateArchivedCable(project, source.SymbolId, "sheet", new(-100, -100)));
        Assert.Null(copy.ReferenceDesignator); Assert.Null(copy.ProvidedLengthMm); Assert.Null(copy.Notes);
        copy.ArchivedCable!.Ports[0].Pins[0].Function = "preview only";
        preview.Schematic.Symbols.Clear();
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var next = author.DuplicateArchivedCable(project, source.SymbolId, "sheet", new(180, 20));
        Assert.Equal(2, next.Cables.Count);
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void LibraryPreviewKeepsBindingsAndClonesManufacturingGeometryWithoutCommitting()
    {
        var project = CableInstanceDuplicationTests.Project(); var before = JsonSerializer.Serialize(project);
        var symbol = project.Schematic!.Symbols[0]; var template = project.Cables[0].ArchivedCable!.Template;
        var manufacturing = symbol.Geometry! with { Primitives = symbol.Geometry!.Primitives.ToList() };
        var contacts = new Dictionary<string, string> { ["P1"] = "A", ["P2"] = "B" };
        var author = new SchematicAuthoringService();
        var preview = author.PreviewArchivedCable(project, template, CableConstructionType.Custom, symbol.Geometry!,
            contacts, "sheet", new(10, 20), "synthetic.dxf", manufacturingGeometry: manufacturing);
        var ghost = preview.Schematic!.Symbols.Last(); var copy = preview.Cables.Last();
        Assert.Equal(new[] { "P1", "P2" }, ghost.CadPortBindings.Select(b => b.SourcePortId));
        Assert.Equal(new[] { "A", "B" }, ghost.CadPortBindings.Select(b => b.CadContactId));
        Assert.Equal("synthetic.dxf", ghost.AssetPath);
        Assert.Null(ghost.AssetRevision);
        Assert.Throws<InvalidOperationException>(() => author.AddArchivedCable(project, template, CableConstructionType.Custom,
            symbol.Geometry!, contacts, "sheet", new(10, 20)));
        Assert.NotSame(manufacturing, copy.ArchivedCable!.ManufacturingGeometry);
        Assert.NotSame(manufacturing.Primitives, copy.ArchivedCable.ManufacturingGeometry!.Primitives);
        Assert.NotSame(manufacturing.Primitives[0], copy.ArchivedCable.ManufacturingGeometry.Primitives[0]);
        Assert.Equal(JsonSerializer.Serialize(manufacturing), JsonSerializer.Serialize(copy.ArchivedCable.ManufacturingGeometry));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var next = author.AddArchivedCable(project, template, CableConstructionType.Custom, symbol.Geometry!, contacts, "sheet", new(180, 20));
        Assert.Equal(2, next.Cables.Count);
        SchematicAuthoringService.Validate(next);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("binding")]
    [InlineData("page")]
    public void PreviewStillRejectsInvalidAssetBindingsAndMissingPages(string invalid)
    {
        var project = CableInstanceDuplicationTests.Project(); var before = JsonSerializer.Serialize(project);
        var symbol = project.Schematic!.Symbols[0]; var geometry = symbol.Geometry!;
        var contacts = new Dictionary<string, string> { [invalid == "binding" ? "unknown" : "P1"] = "A" };
        if (invalid == "hash") geometry = geometry with { SourceSha256 = new('0', 64) };
        var author = new SchematicAuthoringService();
        Assert.Throws<InvalidOperationException>(() => author.PreviewArchivedCable(project, project.Cables[0].ArchivedCable!.Template,
            CableConstructionType.Custom, geometry, contacts, invalid == "page" ? "missing" : "sheet", new(20, 20)));
        Assert.Throws<InvalidOperationException>(() => author.PreviewCableDuplicate(project, symbol.SymbolId, "missing", new(20, 20)));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }
}
