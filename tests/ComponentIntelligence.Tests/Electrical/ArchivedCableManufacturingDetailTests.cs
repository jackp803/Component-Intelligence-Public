using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ArchivedCableManufacturingDetailTests
{
    [Theory]
    [InlineData("TableVisible", "P1 FUNCTION")]
    [InlineData("SketchVisible", null)]
    public void PartsCanBeRemovedIndependentlyAndStayRemovedAfterReopen(string property, string? header)
    {
        var p = Project(); var service = new SchematicCableDetailService();
        p = service.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        var page = p.Schematic!.Pages.Last(); var binding = page.CableDetail!;
        var layout = property == "TableVisible" ? binding with { TableVisible = false } : binding with { SketchVisible = false };
        var next = service.SetLayout(p, page.PageId, binding.DetailId, layout);
        next = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        var geometry = service.Build(next, next.Schematic!.Pages.Last());
        if (header is not null)
        {
            Assert.DoesNotContain(geometry.Primitives, x => x.Text == header);
            Assert.Contains(geometry.Primitives, x => x.Kind == "CIRCLE");
        }
        else
        {
            Assert.DoesNotContain(geometry.Primitives, x => x.Kind == "CIRCLE");
            Assert.Contains(geometry.Primitives, x => x.Text == "P1 FUNCTION");
        }
        Assert.Equal(JsonSerializer.Serialize(p.Cables), JsonSerializer.Serialize(next.Cables));
        Assert.Equal(JsonSerializer.Serialize(p.Connections), JsonSerializer.Serialize(next.Connections));
    }

    [Fact]
    public void RemovingBothPartsRemovesOnlyTheViewAndUndoRestoresLayout()
    {
        var service = new SchematicCableDetailService(); var p = Project();
        p = service.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        var page = p.Schematic!.Pages.Last(); var binding = page.CableDetail!;
        var history = new ComponentIntelligence.Electrical.Editing.ProjectMutationHistory();
        history.RecordBeforeMutation(p, "remove table");
        var hidden = service.RemovePart(p, page.PageId, binding.DetailId, SchematicCableDetailPart.Table);
        Assert.False(hidden.Schematic!.Pages.Last().CableDetail!.TableVisible);
        Assert.True(history.TryUndo(hidden, out var undo, out _));
        Assert.Equal(JsonSerializer.Serialize(p), JsonSerializer.Serialize(undo));
        Assert.True(history.TryRedo(undo, out var redo, out _));
        Assert.False(redo.Schematic!.Pages.Last().CableDetail!.TableVisible);
        var empty = service.RemovePart(redo, page.PageId, binding.DetailId, SchematicCableDetailPart.Sketch);
        Assert.Null(empty.Schematic!.Pages.Last().CableDetail);
        Assert.Equal(JsonSerializer.Serialize(p.Cables), JsonSerializer.Serialize(empty.Cables));
    }

    [Fact]
    public void TableScaleChangesItsBoundsAndTextWithoutChangingTheSketchOrCable()
    {
        var service = new SchematicCableDetailService(); var p = Project();
        p = service.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        var page = p.Schematic!.Pages.Last(); var binding = page.CableDetail!;
        var before = service.Build(p, page);
        var next = service.SetLayout(p, page.PageId, binding.DetailId, binding with { TableWidth = 90, TableScale = .5 });
        var after = service.Build(next, next.Schematic!.Pages.Last());
        Assert.Equal(before.PartBounds[SchematicCableDetailPart.Table].Height / 2, after.PartBounds[SchematicCableDetailPart.Table].Height);
        Assert.Equal(1.3, after.Primitives.Single(x => x.Text == "P1 FUNCTION").TextHeight);
        Assert.Equal(before.PartBounds[SchematicCableDetailPart.Sketch], after.PartBounds[SchematicCableDetailPart.Sketch]);
        Assert.Equal(JsonSerializer.Serialize(p.Cables), JsonSerializer.Serialize(next.Cables));
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingDetailsRejectsNewOverflowAtomically(bool manufacturing)
    {
        var p = Project(); var details = new SchematicCableDetailService();
        p = details.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        var before = JsonSerializer.Serialize(p); var cable = p.Cables[0];
        var author = new SchematicAuthoringService();
        if (manufacturing)
        {
            var draft = new CableManufacturingEditorService().Prepare(cable);
            draft.Rows[0].FromFunction = new string('X', 4000);
            Assert.Throws<InvalidOperationException>(() => author.SetArchivedCableManufacturing(p, cable.CableInstanceId, draft));
        }
        else
            Assert.Throws<InvalidOperationException>(() => author.SetArchivedCableDetails(p, cable.CableInstanceId,
                "CB-01", 1000, new string('X', 4000), CableConstructionType.Custom, cable.ArchivedCable!.Mapping));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Theory]
    [InlineData(95, 5, 0, "BaselineLeft", "TEXT", "REF", 0, true)]
    [InlineData(50, 10, 0, "BaselineCenter", "TEXT", "REF", 0, false)]
    [InlineData(50, 5, -90, "BaselineLeft", "TEXT", "REF", 0, true)]
    [InlineData(50, 10, 90, "BaselineCenter", "TEXT", "REF", 0, false)]
    [InlineData(10, 2, 0, "TopLeft", "MTEXT", "ABCDEFGHIJKLMN", 5, true)]
    [InlineData(10, 2, 0, "TopLeft", "MTEXT", "ABCD", 30, false)]
    public void SketchTextBoundsIncludePositionRotationAndWrapping(double x, double y, double angle,
        string attachment, string kind, string text, double width, bool overflow)
    {
        var p = Project(); p.Cables[0].ReferenceDesignator = "CB-01";
        var binding = p.Cables[0].ArchivedCable!;
        p.Cables[0].ArchivedCable = binding with { ManufacturingGeometry = binding.ManufacturingGeometry! with {
            Primitives = [new() { Kind = kind, Start = new(x, y), Text = text, AttributeTag = kind == "TEXT" ? "REF" : null,
                TextHeight = 3, Rotation = angle, TextAttachment = attachment, TextWidth = width }] } };
        var service = new SchematicCableDetailService();
        if (overflow) Assert.Throws<InvalidOperationException>(() => service.AddArchivedDetail(p, p.Cables[0].CableInstanceId));
        else Assert.NotNull(service.AddArchivedDetail(p, p.Cables[0].CableInstanceId));
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
