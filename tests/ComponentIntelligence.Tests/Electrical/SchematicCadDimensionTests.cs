using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadDimensionTests
{
    [Theory]
    [InlineData(true, false, 0, 1)]
    [InlineData(false, false, 0, 1)]
    [InlineData(true, true, 90, 2)]
    [InlineData(false, true, 30, 1.5)]
    public void DimensionsKeepLabelsLinesAndFilledArrowheads(bool cached, bool nested, double rotation, double scale)
    {
        var path = Path.Combine(Path.GetTempPath(), "dimension-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var style = new DimensionStyle("LABELS") { TextHeight = 4, ArrowSize = 2, DimScaleOverall = 1 };
            var block = new Block("CABLE_DETAIL");
            block.Entities.Add(new Line(Vector2.Zero, new(100, 0)));
            block.Entities.Add(new LinearDimension(new Vector2(10, 10), new(80, 10), 12, 0, style) { UserText = "L1" });
            block.Entities.Add(new AlignedDimension(new Vector2(10, 30), new(90, 30), 10, style) { UserText = "L2" });
            var doc = new DxfDocument { BuildDimensionBlocks = cached };
            if (nested) doc.Entities.Add(new Insert(block, new Vector2(200, 300)) { Rotation = rotation, Scale = new(scale, scale, scale) });
            else foreach (var entity in block.Entities) doc.Entities.Add((EntityObject)entity.Clone());
            doc.Save(path);
            var bytes = File.ReadAllBytes(path);
            var asset = new SchematicCadImporter().ReadDxf(path, .5);
            var l1 = Assert.Single(asset.Primitives, p => p.Text == "L1");
            Assert.Single(asset.Primitives, p => p.Text == "L2");
            Assert.InRange(asset.Primitives.Count(p => p.Kind == "LINE"), 7, 30);
            Assert.True(asset.Primitives.Count(p => p.Kind == "SOLID_HATCH") >= 4, "Dimension arrowheads were not retained as filled polygons.");
            Assert.Equal(4 * scale * .5, l1.TextHeight, 6);
            if (nested) Assert.Contains("CABLE_DETAIL", l1.SourceBlocks);
            Assert.DoesNotContain("Unsupported entity: Dimension", asset.Diagnostics);
            Assert.DoesNotContain("Unsupported entity: Solid", asset.Diagnostics);
            Assert.Empty(asset.ConnectionPoints);
            Assert.All(asset.Primitives.Where(p => p.Text is not null), p => Assert.Null(p.AttributeTag));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), asset.SourceSha256);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CachedCustomPictureTakesPrecedenceOverGeneratingNewMeasurements(int nesting)
    {
        var path = Path.Combine(Path.GetTempPath(), "dimension-picture-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var picture = new Block("CUSTOM_DIMENSION");
            picture.Entities.Add(new Line(Vector2.Zero, new(40, 0)));
            picture.Entities.Add(new Text("L1 CUSTOM", new Vector2(12, 3), 3));
            picture.Entities.Add(new Solid(new Vector2(0, 0), new(3, 1), new(3, -1)));
            var arrow = new Block("CUSTOM_ARROW");
            arrow.Entities.Add(new Line(Vector2.Zero, new(2, 0)));
            arrow.AttributeDefinitions.Add(new AttributeDefinition("X1TERM01") { Value = "DISPLAY ONLY", Height = 2 });
            picture.Entities.Add(new Insert(arrow, new Vector2(4, 0)));
            var dimension = new LinearDimension(Vector2.Zero, new(70, 0), 10, 0) { UserText = "DO NOT REPLACE PICTURE", Block = picture };
            var doc = new DxfDocument { BuildDimensionBlocks = false };
            EntityObject entity = dimension;
            for (var i = 0; i < nesting; i++)
            {
                var parent = new Block("PARENT_" + i) { Origin = new(10, 0, 0) };
                parent.Entities.Add(entity);
                entity = new Insert(parent, new Vector2(200, 300)) { Rotation = 90, Scale = new(2, 2, 2) };
            }
            doc.Entities.Add(new Line(Vector2.Zero, new(100, 0))); doc.Entities.Add(entity); doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var label = Assert.Single(asset.Primitives, p => p.Text == "L1 CUSTOM");
            Assert.Equal(3 * Math.Pow(2, nesting), label.TextHeight, 6);
            if (nesting == 1)
            {
                Assert.Equal(new SchematicPoint(194, 56), label.Start);
                Assert.Equal(-90, label.Rotation, 6);
                Assert.Contains(asset.Primitives, p => p.Kind == "LINE" && p.Start == new SchematicPoint(200, 80) && p.End == new SchematicPoint(200, 0));
                var triangle = Assert.Single(asset.Primitives, p => p.Kind == "SOLID_HATCH").Contours.Single();
                Assert.Equal(new SchematicPoint[] { new(200, 80), new(198, 74), new(202, 74) }, triangle);
            }
            Assert.DoesNotContain(asset.Primitives, p => p.Text?.Contains("DO NOT REPLACE") == true);
            Assert.Single(asset.Primitives, p => p.Kind == "SOLID_HATCH");
            Assert.Empty(asset.ConnectionPoints);
            Assert.All(asset.Primitives, p => Assert.Null(p.AttributeTag));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenDimensionsStayHiddenAndBlankOverridesDoNotRevealMeasurements(bool nested)
    {
        var path = Path.Combine(Path.GetTempPath(), "dimension-visibility-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            var doc = new DxfDocument { BuildDimensionBlocks = true };
            var body = new Block("VISIBLE_CABLE");
            body.Entities.Add(new Line(Vector2.Zero, new(100, 0)));
            body.Entities.Add(new LinearDimension(Vector2.Zero, new(70, 0), 10, 0) { UserText = "HIDDEN LENGTH", IsVisible = false });
            body.Entities.Add(new LinearDimension(Vector2.Zero, new(70, 0), 20, 0) { UserText = " " });
            if (nested) doc.Entities.Add(new Insert(body));
            else foreach (var entity in body.Entities) doc.Entities.Add((EntityObject)entity.Clone());
            doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            Assert.DoesNotContain(asset.Primitives, p => !string.IsNullOrWhiteSpace(p.Text));
            Assert.True(asset.Primitives.Count(p => p.Kind == "LINE") > 1);
        }
        finally { File.Delete(path); }
    }
}
