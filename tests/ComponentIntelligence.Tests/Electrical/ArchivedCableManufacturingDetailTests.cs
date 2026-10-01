using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ArchivedCableManufacturingDetailTests
{
    [Fact]
    public void UnwiredArchivedCableHasDraftDetail()
    {
        var p = Project(); var service = new SchematicCableDetailService();
        var next = service.AddArchivedDetail(p, p.Cables[0].CableInstanceId, "sheet");
        var detail = service.Build(next, next.Schematic!.Pages.Last());
        Assert.Contains("CABLE_MAPPING_UNCONFIRMED", detail.Diagnostics);
        Assert.Contains(detail.Primitives, x => x.Text == "P1 FUNCTION");
        Assert.Contains(detail.Primitives, x => x.Text?.Contains("草稿") == true);
        Assert.Contains(detail.Primitives, x => x.Kind == "CIRCLE");
        Assert.Empty(detail.ConnectionIds);
    }

    [Fact]
    public void TwoViewsCountOneCable()
    {
        var p = Project(); var s = new SchematicCableDetailService();
        var next = s.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        Assert.Single(next.Cables); Assert.Single(next.Schematic!.Symbols);
        Assert.Equal(JsonSerializer.Serialize(p.Cables), JsonSerializer.Serialize(next.Cables));
    }

    [Fact]
    public void ReferenceLengthSpecificationUpdateBothViews()
    {
        var p = Project(); var s = new SchematicCableDetailService(); var a = new SchematicAuthoringService();
        p = s.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        p = a.SetArchivedCableDetails(p, p.Cables[0].CableInstanceId, "CB-02", 1234, "custom specification",
            CableConstructionType.Custom, p.Cables[0].ArchivedCable!.Mapping);
        var detail = s.Build(p, p.Schematic!.Pages.Last());
        foreach (var value in new[] { "CB-02", "1234 mm", "custom specification" })
            Assert.Contains(detail.Primitives, x => x.Text?.Contains(value) == true);
        var symbol = p.Schematic.Symbols[0];
        Assert.Contains(SchematicSymbolPresentation.GeometryForOwner(symbol, SchematicSymbolOwner.Resolve(p, symbol))!.Primitives, x => x.Text == "CB-02");
    }

    [Fact]
    public void DetailGeometryHasNoConnectableAnchors()
    {
        var p = Project(); var s = new SchematicCableDetailService();
        var next = s.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        Assert.Single(next.Schematic!.Symbols);
        Assert.Empty(next.Schematic.Wires);
        Assert.All(s.Build(next, next.Schematic.Pages.Last()).Primitives, x => Assert.Null(x.AttributeTag));
    }

    [Fact]
    public void RemovingDetailDoesNotRemoveCableOrConnections()
    {
        var p = Project(); var s = new SchematicCableDetailService();
        p = s.PlaceArchivedDetail(p, p.Cables[0].CableInstanceId, "sheet", new(20, 30), new(20, 110));
        var binding = Assert.Single(p.Schematic!.Pages[0].InlineCableDetails);
        var next = s.RemoveInlineDetail(p, "sheet", binding.DetailId);
        Assert.Empty(next.Schematic!.Pages[0].InlineCableDetails); Assert.Single(next.Cables); Assert.Single(next.Schematic.Symbols);
    }

    [Fact]
    public void MoveDetailDoesNotChangePhysicalLength()
    {
        var p = Project(); p.Cables[0].ProvidedLengthMm = 500;
        var s = new SchematicCableDetailService();
        p = s.PlaceArchivedDetail(p, p.Cables[0].CableInstanceId, "sheet", new(20, 30), new(20, 110));
        var b = p.Schematic!.Pages[0].InlineCableDetails[0];
        var next = s.SetLayout(p, "sheet", b.DetailId, b with { SketchPosition = new(100, 140) });
        Assert.Equal(500, next.Cables[0].ProvidedLengthMm);
        Assert.Equal(new(100, 140), next.Schematic!.Pages[0].InlineCableDetails[0].SketchPosition);
    }

    [Fact]
    public void OverflowIsNotSilent()
    {
        var p = Project(); var s = new SchematicCableDetailService();
        Assert.Throws<InvalidOperationException>(() => s.PlaceArchivedDetail(p, p.Cables[0].CableInstanceId, "sheet", new(20, 280), new(20, 110)));
        Assert.Empty(p.Schematic!.Pages[0].InlineCableDetails);
    }

    [Fact]
    public void LongTextFitsOrIsDiagnosed()
    {
        var p = Project(); p.Cables[0].Specification = new string('X', 4000);
        Assert.Throws<InvalidOperationException>(() => new SchematicCableDetailService().AddArchivedDetail(p, p.Cables[0].CableInstanceId));
    }

    internal static ElectricalProject Project()
    {
        var template = CableManufacturingEditorTests.Template() with { TextBindings = [new("REF", CableTextField.Reference)] };
        var wiring = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 60, Height = 20, MillimetresPerUnit = 1,
            Primitives = [new() { Kind = "TEXT", Start = new(2, 5), Text = "REF", TextHeight = 3, AttributeTag = "REF" }] };
        var sketch = new SchematicCadAsset { SourceSha256 = new('b', 64), Width = 100, Height = 20, MillimetresPerUnit = 1,
            Primitives = [new() { Kind = "CIRCLE", Start = new(10, 10), Radius = 5 },
                new() { Kind = "LINE", Start = new(15, 10), End = new(90, 10) },
                new() { Kind = "TEXT", Start = new(30, 5), Text = "REF", AttributeTag = "REF", TextHeight = 3 }] };
        return new SchematicAuthoringService().AddArchivedCable(new() { ProjectId = "test",
            Schematic = new() { Pages = [new() { PageId = "sheet", Title = "wiring" }] } },
            template, CableConstructionType.Custom, wiring, new Dictionary<string, string>(), "sheet", new(20, 20),
            manufacturingGeometry: sketch);
    }
}
