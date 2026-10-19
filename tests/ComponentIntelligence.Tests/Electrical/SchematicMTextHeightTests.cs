using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicMTextHeightTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DimensionInlineHeightRemainsReadableWithCachedAndGeneratedPictures(bool cached, bool nested)
    {
        var path = TempPath();
        try
        {
            var style = new DimensionStyle("MARKS") { TextHeight = .18, ArrowSize = .18 };
            var body = new Block("CABLE");
            body.Entities.Add(new Line(Vector2.Zero, new(100, 0)));
            body.Entities.Add(new LinearDimension(new Vector2(10, 10), new(40, 10), 10, 0, style) { UserText = @"{\H42.02x;L1}" });
            body.Entities.Add(new LinearDimension(new Vector2(50, 10), new(90, 10), 10, 0, style) { UserText = @"{\H42.02x;L2}" });
            var doc = new DxfDocument { BuildDimensionBlocks = cached };
            if (nested) doc.Entities.Add(new Insert(body, new Vector2(200, 300)) { Rotation = 90, Scale = new(2, 2, 2) });
            else foreach (var entity in body.Entities) doc.Entities.Add((EntityObject)entity.Clone());
            doc.Save(path);
            var bytes = File.ReadAllBytes(path);
            var asset = new SchematicCadImporter().ReadDxf(path, .5);
            foreach (var name in new[] { "L1", "L2" })
            {
                var label = Assert.Single(asset.Primitives, p => p.Text == name);
                Assert.Equal(nested ? 7.5636 : 3.7818, label.TextHeight, 6);
                Assert.Equal(nested ? -90 : 0, label.Rotation, 6);
                Assert.Null(label.AttributeTag);
            }
            Assert.Empty(asset.ConnectionPoints);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(@"\A1;{\H42.02x;L1}", 7.5636)]
    [InlineData(@"{\H7.5;L1}", 7.5)]
    [InlineData(@"{\H2x;{\H3x;L1}}", 1.08)]
    [InlineData(@"{\H2x;L1}{\H2x;L2}", .36)]
    [InlineData(@"\H2x;L1\PL2", .36)]
    [InlineData(@"{\H2x;L1} L2", .18)]
    [InlineData(@"{\H2x;L1}{\H3x;L2}", .18)]
    [InlineData(@"\\H42.02x;L1", .18)]
    [InlineData(@"{\H0x;L1}", .18)]
    [InlineData(@"{\HNaNx;L1}", .18)]
    [InlineData(@"{\HInfinityx;L1}", .18)]
    [InlineData(@"{\H-2x;L1}", .18)]
    [InlineData(@"{\H2x;L1", .18)]
    [InlineData(@"{\H2x;\FArial|b0;L1}", .36)]
    [InlineData(@"{\H2x;\{L1\}}", .36)]
    [InlineData(@"{\H2x;\U+004C1}", .36)]
    [InlineData(@"{\H2x;\Wbad;L1}", .18)]
    [InlineData(@"{\H2x;\QNaN;L1}", .18)]
    [InlineData(@"{\H2x;\C9999;L1}", .18)]
    [InlineData(@"{\H2x;\W.8;L1}", .36)]
    public void UniformHeightHonorsScopeWithoutScalingMixedOrMalformedText(string value, double expected)
    {
        var path = TempPath();
        try
        {
            var body = new Block("LABEL");
            body.Entities.Add(new Line(Vector2.Zero, new(100, 0)));
            body.Entities.Add(new MText(value, new Vector2(30, 10), .18));
            var doc = new DxfDocument();
            doc.Entities.Add(new Insert(body) { Scale = new(2, 2, 2) }); doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            Assert.Equal(expected * 2, Assert.Single(asset.Primitives, p => p.Kind == "MTEXT").TextHeight, 6);
            Assert.All(asset.Primitives, p => Assert.True(double.IsFinite(p.TextHeight)));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeightNormalizationDoesNotDecodeLiteralBracesBeforeFinalTextImport(bool nested)
    {
        var path = TempPath();
        try
        {
            var body = new Block("ESCAPED_LABEL");
            body.Entities.Add(new Line(Vector2.Zero, new(100, 0)));
            body.Entities.Add(new MText(@"{\H2x;\{L1\}}", new Vector2(30, 10), .18));
            var doc = new DxfDocument();
            if (nested) doc.Entities.Add(new Insert(body) { Scale = new(2, 2, 2) });
            else foreach (var entity in body.Entities) doc.Entities.Add((EntityObject)entity.Clone());
            doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var label = Assert.Single(asset.Primitives, p => p.Kind == "MTEXT");
            Assert.Equal("{L1}", label.Text);
            Assert.Equal(nested ? .72 : .36, label.TextHeight, 6);
        }
        finally { File.Delete(path); }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), "mtext-height-" + Guid.NewGuid().ToString("N") + ".dxf");
}
