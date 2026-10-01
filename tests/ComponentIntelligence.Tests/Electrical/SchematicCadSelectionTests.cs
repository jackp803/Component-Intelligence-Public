using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using netDxf;
using netDxf.Entities;
using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadSelectionTests
{
    private static readonly SchematicCadAsset Combined = new()
    {
        SourceSha256 = new('a', 64), MillimetresPerUnit = 1, Width = 100, Height = 50,
        Primitives = [
            new() { Kind = "LINE", Start = new(10, 10), End = new(30, 20) },
            new() { Kind = "LINE", Start = new(60, 30), End = new(80, 40) },
            new() { Kind = "TEXT", Start = new(60, 5), Text = "OLD TABLE", TextHeight = 2 }],
        ConnectionPoints = [new("X1TERM01", "1", new(10, 15), Direction: "Right")]
    };

    [Fact]
    public void CombinedRegionsKeepContactTransforms()
    {
        var selected = SchematicCadSelectionService.Select(Combined, Region(5, 5, 30, 20));
        Assert.Equal(new SchematicPoint(5, 5), selected.Primitives[0].Start);
        Assert.Equal(new SchematicPoint(5, 10), Assert.Single(selected.ConnectionPoints).Position);
        Assert.Equal("Right", selected.ConnectionPoints[0].Direction);
        Assert.Equal(Combined.SourceSha256, selected.SourceSha256);
        Assert.Equal(30, selected.Width);
        Assert.Equal(20, selected.Height);
    }

    [Fact]
    public void DifferentSelectionsHaveDifferentGeometryIdentity()
    {
        var first = SchematicCadSelectionService.Select(Combined, Region(5, 5, 30, 20));
        var second = SchematicCadSelectionService.Select(Combined, Region(55, 25, 30, 20));
        Assert.NotEqual(first.SelectionSha256, second.SelectionSha256);
        Assert.Equal(first.SelectionSha256, SchematicCadSelectionService.Select(Combined, Region(5, 5, 30, 20)).SelectionSha256);
    }

    [Fact]
    public void IdenticalShapesAtDifferentSourceLocationsRemainDifferentSelections()
    {
        var asset = Combined with { ConnectionPoints = [], Primitives = [
            new() { Kind = "LINE", Start = new(10, 10), End = new(20, 20) },
            new() { Kind = "LINE", Start = new(60, 10), End = new(70, 20) }] };
        var first = SchematicCadSelectionService.Select(asset, Region(5, 5, 20, 20));
        var second = SchematicCadSelectionService.Select(asset, Region(55, 5, 20, 20));
        Assert.Equal(first.Primitives[0].Start, second.Primitives[0].Start);
        Assert.NotEqual(first.SelectionSha256, second.SelectionSha256);
    }

    [Fact]
    public void StaticTableExcludedOnlyExplicitly()
    {
        Assert.Contains(Combined.Primitives, p => p.Text == "OLD TABLE");
        var selected = SchematicCadSelectionService.Select(Combined, Region(55, 25, 30, 20));
        Assert.DoesNotContain(selected.Primitives, p => p.Text == "OLD TABLE");
        Assert.Single(selected.Primitives);
        Assert.Empty(selected.ConnectionPoints);
    }

    [Fact]
    public void BoundaryCutOrUnsupportedGeometryIsDiagnosed()
    {
        var asset = Combined with { Diagnostics = ["Unsupported entity: Spline"] };
        var selected = SchematicCadSelectionService.Select(asset, Region(15, 5, 70, 40));
        Assert.Contains("CAD_SELECTION_CUTS_ENTITY", selected.Diagnostics);
        Assert.Contains("Unsupported entity: Spline", selected.Diagnostics);
        Assert.False(selected.Complete);
    }

    [Fact]
    public void NamedBlocksKeepOnlyTheirGeometryAndContacts()
    {
        var path = Path.Combine(Path.GetTempPath(), "block-selection-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var block = new netDxf.Blocks.Block("WIRE");
            block.Entities.Add(new Line(Vector2.Zero, new(10, 10)));
            block.AttributeDefinitions.Add(new AttributeDefinition("X1TERM01") { Position = new(0, 5, 0), Flags = AttributeFlags.Hidden });
            var other = new netDxf.Blocks.Block("SKETCH"); other.Entities.Add(new Line(Vector2.Zero, new(20, 20)));
            var doc = new DxfDocument(); doc.Entities.Add(new Insert(block, new Vector2(10, 10)));
            doc.Entities.Add(new Insert(other, new Vector2(60, 30))); doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var selected = SchematicCadSelectionService.Select(asset, new() { BlockName = "WIRE", GeometrySha256 = new('0', 64) });
            Assert.Single(selected.Primitives);
            Assert.Single(selected.ConnectionPoints);
            Assert.Equal(10, selected.Width);
            Assert.Equal(10, selected.Height);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EmptyAndOutOfBoundsSelectionsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SchematicCadSelectionService.Select(Combined, Region(90, 0, 5, 5)));
        Assert.Throws<InvalidDataException>(() => SchematicCadSelectionService.Select(Combined, Region(-1, 0, 10, 10)));
    }

    [Fact]
    public void CablePlacementRejectsAnotherSelectionFromTheSameCadFile()
    {
        var first = SchematicCadSelectionService.Select(Combined, Region(5, 5, 30, 20));
        var other = SchematicCadSelectionService.Select(Combined, Region(55, 25, 30, 20));
        var template = new ArchivedCableTemplate { TemplateId = "cable", TemplateRevision = "r1", AssetSha256 = Combined.SourceSha256,
            Ports = [new() { PortId = "P1", Name = "P1", Pins = [new() { PinId = "p1.1", PinNumber = "1" }] }] };
        var node = JsonSerializer.SerializeToNode(template)!;
        node["WiringSelectionSha256"] = first.SelectionSha256;
        template = node.Deserialize<ArchivedCableTemplate>()!;
        var project = new ElectricalProject { ProjectId = "test", Schematic = new() { Pages = [new() { PageId = "sheet", Title = "sheet" }] } };
        Assert.Throws<InvalidOperationException>(() => new SchematicAuthoringService().AddArchivedCable(project,
            template, CableConstructionType.Unknown, other, new Dictionary<string, string>(), "sheet", new(20, 20)));
        Assert.Empty(project.Cables);
    }

    private static CableCadSelection Region(double x, double y, double width, double height) =>
        new() { Bounds = new(x, y, width, height), GeometrySha256 = new('0', 64) };
}
