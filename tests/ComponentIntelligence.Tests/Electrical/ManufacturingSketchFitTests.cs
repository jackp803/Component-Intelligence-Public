using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ManufacturingSketchFitTests
{
    [Fact]
    public void UnusedMaximumHeightDoesNotPreventEnlargingContentThatFitsThePage()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "LINE", Start = new(0, 0), End = new(100, 10) });
        var service = new SchematicCableDetailService();
        var placed = service.AddArchivedDetail(project, project.Cables[0].CableInstanceId, "sheet", Layout(project));
        var page = placed.Schematic!.Pages.Last(); var original = page.CableDetail!;
        var enlarged = service.SetLayout(placed, page.PageId, original.DetailId, original with { SketchWidth = 240, SketchHeight = 300 });
        enlarged = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(enlarged))!;
        var view = service.Build(enlarged, enlarged.Schematic!.Pages.Last());
        var box = view.PartBounds[SchematicCableDetailPart.Sketch];
        Assert.InRange(box.Height, 24, 25);
        Assert.Equal(240, box.Width, 9);
        Assert.Equal(300, enlarged.Schematic.Pages.Last().CableDetail!.SketchHeight);
        Assert.Equal(JsonSerializer.Serialize(project.Cables), JsonSerializer.Serialize(enlarged.Cables));
    }

    [Fact]
    public void EmptyLabelOriginsDoNotAddInvisibleSpaceToTheFit()
    {
        var project = WithSketch(new() { Kind = "LINE", Start = new(0, 0), End = new(100, 0) },
            new() { Kind = "TEXT", Start = new(10000, -10000), Text = "  ", TextHeight = 3 });
        var view = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        Assert.Equal(179, view.Primitives[0].End!.X - view.Primitives[0].Start.X, 9);
        Assert.InRange(view.PartBounds[SchematicCableDetailPart.Sketch].Height, 1, 2);
    }

    [Fact]
    public void PartialArcFitsItsRenderedSweepInsteadOfItsEntireCircleOrCenter()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "ARC", Start = new(0, 0), Radius = 1000, StartAngle = 0, EndAngle = 5 });
        var view = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        var arc = Assert.Single(view.Primitives);
        Assert.Equal(69 / Math.Sin(5 * Math.PI / 180), arc.Radius, 8);
        Assert.InRange(view.PartBounds[SchematicCableDetailPart.Sketch].Width, 3, 5);
    }

    [Fact]
    public void InvisibleTextWithExtremeFiniteOriginIsNotSentToTheRenderer()
    {
        var project = WithSketch(new() { Kind = "LINE", Start = new(-1e308, 0), End = new(-1e308, 10) },
            new() { Kind = "TEXT", Start = new(1e308, 0), Text = "", TextHeight = 3 });
        var view = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        Assert.Single(view.Primitives);
        Assert.All(view.Primitives, p => Assert.True(double.IsFinite(p.Start.X) && double.IsFinite(p.Start.Y)));
    }

    [Fact]
    public void ActualSketchOverflowStillRejectsWithoutChangingTheProject()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "LINE", Start = new(0, 0), End = new(100, 10) });
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => new SchematicCableDetailService().AddArchivedDetail(project,
            project.Cables[0].CableInstanceId, "sheet", Layout(project) with { SketchPosition = new(300, 110) }));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void TableCanUseSpaceOutsideTheEffectiveSketchBounds()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "LINE", Start = new(0, 0), End = new(100, 10) });
        var view = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0],
            Layout(project) with { SketchHeight = 200, TableVisible = true, TablePosition = new(20, 160) });
        Assert.True(view.PartBounds[SchematicCableDetailPart.Sketch].Y + view.PartBounds[SchematicCableDetailPart.Sketch].Height <
            view.PartBounds[SchematicCableDetailPart.Table].Y);
    }

    [Theory]
    [InlineData(95, 0, 0, "BaselineLeft", "TEXT", 0)]
    [InlineData(-5, 5, -90, "BaselineCenter", "TEXT", 0)]
    [InlineData(50, 20, 90, "BottomRight", "TEXT", 0)]
    [InlineData(0, 0, 45, "TopLeft", "MTEXT", 5)]
    [InlineData(100, 20, -45, "BottomRight", "MTEXT", 5)]
    [InlineData(50, 0, 90, "MiddleCenter", "MTEXT", 10)]
    public void TextOutsideLegacyDimensionsFitsRequestedRectangle(double x, double y, double angle,
        string attachment, string kind, double width)
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = kind, Start = new(x, y), Text = "COMPLETE LABEL\nSECOND LINE",
            TextHeight = 3, Rotation = angle, TextAttachment = attachment, TextWidth = width });
        var before = JsonSerializer.Serialize(project);
        var service = new SchematicCableDetailService();
        var placed = service.AddArchivedDetail(project, project.Cables[0].CableInstanceId, "sheet", Layout(project));
        var page = placed.Schematic!.Pages.Last();
        var result = service.Build(placed, page);
        Assert.Equal("COMPLETE LABEL\nSECOND LINE", Assert.Single(result.Primitives).Text);
        AssertTextContained(Assert.Single(result.Primitives), result.PartBounds[SchematicCableDetailPart.Sketch]);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal(JsonSerializer.Serialize(project.Cables), JsonSerializer.Serialize(placed.Cables));
        Assert.Empty(placed.Connections);
    }

    [Fact]
    public void MultilineBaselineUsesTheLastLineWithoutClippingItsFirstLine()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "TEXT", Start = new(95, 0), Text = "A\nB", TextHeight = 3 });
        var result = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        var text = Assert.Single(result.Primitives);
        // 9 mm text envelope scaled into 69 mm height, with a 0.5 mm stroke margin.
        Assert.Equal(23, text.TextHeight, 9);
        Assert.Equal(168, text.Start.Y, 9);
    }

    [Fact]
    public void MtextWordWrappingBudgetsWholeWordsInsteadOfOnlyCharacterCount()
    {
        var text = string.Join(" ", Enumerable.Repeat("WWWWWWWWWW", 10));
        var project = WithSketch(new SchematicCadPrimitive { Kind = "MTEXT", Start = new(100, 0), Text = text,
            TextHeight = 3, TextWidth = 54, TextAttachment = "TopLeft" });
        var result = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        Assert.Equal(text, result.Primitives[0].Text);
        // Each ten-character word requires its own line at 18-character conservative capacity.
        Assert.Equal(4.6, result.Primitives[0].TextHeight, 9);
    }

    [Fact]
    public void NarrowMtextGlyphOverflowFitsWithoutChangingItsWrapWidth()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "MTEXT", Start = new(100, 0), Text = "W",
            TextHeight = 3, TextWidth = 1, TextAttachment = "TopCenter", Rotation = 90 });
        var result = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        var glyph = result.Primitives[0];
        // A 3 mm glyph in a 1 mm centered wrap box spans local X=[-0.5,2.5].
        Assert.InRange(glyph.Start.Y - glyph.TextWidth / 2, 110, 180);
        Assert.InRange(glyph.Start.Y - glyph.TextWidth / 2 + glyph.TextHeight, 110, 180);
        Assert.Equal(3, glyph.TextHeight / glyph.TextWidth, 9);
    }

    [Fact]
    public void LargeFiniteOriginDoesNotEraseCurvedExtents()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "CIRCLE", Start = new(1e18, 1e18), Radius = 3 });
        var before = JsonSerializer.Serialize(project.Cables);
        var result = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        Assert.Equal(34.5, result.Primitives[0].Radius, 9);
        Assert.Equal(new SchematicPoint(55, 145), result.Primitives[0].Start);
        Assert.Equal(before, JsonSerializer.Serialize(project.Cables));
    }

    [Fact]
    public void EmptyTextCannotBypassFiniteDimensionValidation()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "TEXT", Start = new(0, 0), Text = "",
            TextHeight = double.NaN, Rotation = double.NaN });
        Assert.Throws<InvalidOperationException>(() => new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project)));
    }

    [Fact]
    public void AllVisibleGeometryFitsTogetherWithoutLegacyWhitespace()
    {
        var project = WithSketch(
            new() { Kind = "LINE", Start = new(0, 0), End = new(100, 0) },
            new() { Kind = "CIRCLE", Start = new(0, 10), Radius = 10 },
            new() { Kind = "ARC", Start = new(100, 10), Radius = 10, StartAngle = 0, EndAngle = 180 },
            new() { Kind = "SOLID_HATCH", Start = new(10, 30), Contours = [new SchematicPoint[] { new(10, 30), new(20, 40), new(30, 30) }] },
            new() { Kind = "TEXT", Start = new(95, 0), Text = "EDGE LABEL", TextHeight = 3 });
        var archive = project.Cables[0].ArchivedCable!;
        project.Cables[0].ArchivedCable = archive with { ManufacturingGeometry = archive.ManufacturingGeometry! with { Width = 1000, Height = 1000 } };
        var before = JsonSerializer.Serialize(project.Cables);
        var service = new SchematicCableDetailService();
        var large = service.BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        var small = service.BuildArchived(project, project.Schematic.Pages[0], Layout(project) with { SketchWidth = 90, SketchHeight = 35 });
        var line = large.Primitives[0]; var smallLine = small.Primitives[0];
        var scale = (line.End!.X - line.Start.X) / 100;
        Assert.True(line.End.X - line.Start.X > 100, "Legacy whitespace must not determine the fit scale.");
        Assert.Equal(10 * scale, large.Primitives[1].Radius, 9);
        Assert.Equal(10 * scale, large.Primitives[2].Radius, 9);
        Assert.Equal(3 * scale, large.Primitives[4].TextHeight, 9);
        Assert.Equal(10 * scale, large.Primitives[3].Contours[0][1].X - large.Primitives[3].Contours[0][0].X, 9);
        Assert.True(smallLine.End!.X - smallLine.Start.X < line.End.X - line.Start.X);
        foreach (var result in new[] { large, small })
        {
            var box = result.PartBounds[SchematicCableDetailPart.Sketch];
            AssertTextContained(result.Primitives[4], box);
            foreach (var primitive in result.Primitives.Take(4))
            {
                AssertPoint(primitive.Start, box);
                if (primitive.End is not null) AssertPoint(primitive.End, box);
                foreach (var point in primitive.Contours.SelectMany(c => c)) AssertPoint(point, box);
                if (primitive.Radius > 0)
                {
                    AssertPoint(new(primitive.Start.X - primitive.Radius, primitive.Start.Y - primitive.Radius), box);
                    AssertPoint(new(primitive.Start.X + primitive.Radius, primitive.Start.Y + primitive.Radius), box);
                }
            }
        }
        Assert.Equal(before, JsonSerializer.Serialize(project.Cables));
    }

    [Fact]
    public void UpdatedFieldIsMeasuredBeforeFittingAndSurvivesReopen()
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "TEXT", Start = new(95, 0), Text = "REF", AttributeTag = "REF", TextHeight = 3 });
        var service = new SchematicCableDetailService();
        var placed = service.AddArchivedDetail(project, project.Cables[0].CableInstanceId, "sheet", Layout(project));
        var cable = placed.Cables[0]; var source = JsonSerializer.Serialize(cable.ArchivedCable!.ManufacturingGeometry);
        var next = new SchematicAuthoringService().SetArchivedCableDetails(placed, cable.CableInstanceId,
            "CB-COMPLETE-LONG-REFERENCE", 500, "TEST", CableConstructionType.Custom, cable.ArchivedCable.Mapping);
        next = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        var result = service.Build(next, next.Schematic!.Pages.Last());
        var text = Assert.Single(result.Primitives);
        Assert.Equal("CB-COMPLETE-LONG-REFERENCE", text.Text);
        Assert.Null(text.AttributeTag);
        AssertTextContained(text, result.PartBounds[SchematicCableDetailPart.Sketch]);
        Assert.Equal(source, JsonSerializer.Serialize(next.Cables[0].ArchivedCable!.ManufacturingGeometry));
        Assert.Equal(500, next.Cables[0].ProvidedLengthMm);
    }

    [Fact]
    public void ImportedInsertionPointBoundsDoNotBlockCompleteText()
    {
        var path = Path.Combine(Path.GetTempPath(), "ci-sketch-fit-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var document = new DxfDocument();
            document.Entities.Add(new Line(new Vector2(0, 0), new Vector2(100, 20)));
            document.Entities.Add(new Text("COMPLETE OUTER LABEL", new Vector2(100, 20), 3));
            Assert.True(document.Save(path));
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var project = WithSketch(); var archive = project.Cables[0].ArchivedCable!;
            project.Cables[0].ArchivedCable = archive with { ManufacturingGeometry = asset };
            var source = JsonSerializer.Serialize(asset);
            var result = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
            AssertTextContained(result.Primitives.Single(p => p.Kind == "TEXT"), result.PartBounds[SchematicCableDetailPart.Sketch]);
            Assert.Equal(source, JsonSerializer.Serialize(project.Cables[0].ArchivedCable!.ManufacturingGeometry));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, 0, 100, 0)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 0, 0)]
    public void DegenerateGeometryProducesFiniteContainedOutput(double x, double y, double ex, double ey)
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "LINE", Start = new(x, y), End = new(ex, ey) });
        var result = new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project));
        AssertPoint(result.Primitives[0].Start, result.PartBounds[SchematicCableDetailPart.Sketch]);
        AssertPoint(result.Primitives[0].End!, result.PartBounds[SchematicCableDetailPart.Sketch]);
    }

    [Theory]
    [InlineData(double.NaN, 3, 1)]
    [InlineData(0, double.PositiveInfinity, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 3, -1)]
    public void MalformedContentRejectsPlacementWithoutMutation(double x, double height, double widthFactor)
    {
        var project = WithSketch(new SchematicCadPrimitive { Kind = "TEXT", Start = new(x, 0), Text = "BAD", TextHeight = height, TextWidthFactor = widthFactor });
        var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var before = JsonSerializer.Serialize(project, options);
        Assert.Throws<InvalidOperationException>(() => new SchematicCableDetailService().BuildArchived(project, project.Schematic!.Pages[0], Layout(project)));
        Assert.Equal(before, JsonSerializer.Serialize(project, options));
    }

    private static ElectricalProject WithSketch(params SchematicCadPrimitive[] primitives)
    {
        var project = ArchivedCableManufacturingDetailTests.Project();
        var archive = project.Cables[0].ArchivedCable!;
        project.Cables[0].ArchivedCable = archive with { ManufacturingGeometry = archive.ManufacturingGeometry! with { Primitives = primitives } };
        return project;
    }

    private static SchematicCableDetailBinding Layout(ElectricalProject project) => new(project.Cables[0].CableInstanceId, null)
    { TableVisible = false, SketchVisible = true, SketchPosition = new(20, 110), SketchWidth = 180, SketchHeight = 70 };

    private static void AssertTextContained(SchematicCadPrimitive p, SchematicGridBounds box)
    {
        // Independent conservative envelope of the unabridged displayed text, including wrapping.
        var lines = p.DisplayText!.Replace("\r", "").Split('\n');
        var glyph = p.TextHeight * (p.Kind == "TEXT" ? p.TextWidthFactor : 1);
        var width = lines.Max(line => line.Length) * glyph;
        var count = lines.Length;
        if (p.Kind == "MTEXT" && p.TextWidth > 0)
        {
            width = p.TextWidth;
            // Spaces between words do not require a separate visible glyph line in these fixtures.
            count = lines.Sum(line => Math.Max(1, (int)Math.Ceiling(line.Count(c => !char.IsWhiteSpace(c)) /
                Math.Max(1, Math.Floor(width / glyph)))));
        }
        var height = p.TextHeight * 1.5 * count;
        var attachment = p.TextAttachment ?? (p.Kind == "MTEXT" ? "TopLeft" : "BaselineLeft");
        var x = attachment.EndsWith("Right") ? -width : attachment.EndsWith("Center") || attachment == "Middle" ? -width / 2 : 0;
        var y = attachment.StartsWith("Top") ? 0 : attachment.StartsWith("Middle") ? -height / 2 :
            attachment.StartsWith("Bottom") ? -height : p.Kind == "TEXT" ? -(height - .5 * p.TextHeight) : 0;
        var angle = p.Rotation * Math.PI / 180;
        foreach (var corner in new[] { new SchematicPoint(x, y), new(x + width, y), new(x, y + height), new(x + width, y + height) })
            AssertPoint(new(p.Start.X + corner.X * Math.Cos(angle) - corner.Y * Math.Sin(angle),
                p.Start.Y + corner.X * Math.Sin(angle) + corner.Y * Math.Cos(angle)), box);
    }

    private static void AssertPoint(SchematicPoint point, SchematicGridBounds box)
    {
        Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y));
        Assert.InRange(point.X, box.X - .001, box.X + box.Width + .001);
        Assert.InRange(point.Y, box.Y - .001, box.Y + box.Height + .001);
    }
}
