using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadImportTests
{
    [Theory]
    [InlineData(TextAlignment.MiddleCenter)]
    [InlineData(TextAlignment.TopRight)]
    [InlineData(TextAlignment.BaselineRight)]
    public void SingleLineTextRetainsAlignmentAndWidthFactor(TextAlignment alignment)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new DxfDocument();
            doc.Entities.Add(new Line(Vector2.Zero, new Vector2(100, 100)));
            doc.Entities.Add(new Text("Grid", new Vector2(50, 50), 3)
                { Alignment = alignment, WidthFactor = 0.8, Rotation = 90 });
            doc.Save(path);
            var text = Assert.Single(new SchematicCadImporter().ReadDxf(path, 1).Primitives, p => p.Kind == "TEXT");
            Assert.Equal(alignment.ToString(), text.TextAttachment);
            Assert.Equal(0.8, text.TextWidthFactor, 8);
            Assert.Equal(-90, text.Rotation, 8);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("TopRight", -20, 0)]
    [InlineData("MiddleCenter", -10, -5)]
    [InlineData("BottomLeft", 0, -10)]
    [InlineData("BaselineRight", -20, -8)]
    [InlineData(null, 0, -8)]
    public void TextAnchorOffsetUsesMeasuredGeometry(string? alignment, double x, double y)
    {
        var text = new SchematicCadPrimitive { Kind = "TEXT", Start = new(0, 0), TextAttachment = alignment };
        Assert.Equal(new SchematicPoint(x, y), text.TextAnchorOffset(20, 10, 8));
    }

    [Fact]
    public void InsertHiddenAttributesRemainInventoryWithoutVisibleTextOrMetadataBounds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var block = new netDxf.Blocks.Block("HiddenAttributes");
            block.Entities.Add(new Line(Vector2.Zero, new Vector2(30, 20)));
            block.AttributeDefinitions.Add(new AttributeDefinition("CONFIG")
                { Position = new Vector3(-1000, -1000, 0), Flags = AttributeFlags.Hidden, Value = "PRIVATE_CONFIG" });
            block.AttributeDefinitions.Add(new AttributeDefinition("X1TERM01")
                { Position = new Vector3(5, 10, 0), Flags = AttributeFlags.Hidden, Value = "1" });
            block.AttributeDefinitions.Add(new AttributeDefinition("TAG1")
                { Position = new Vector3(10, 10, 0), Value = "DEVICE" });
            var doc = new DxfDocument();
            doc.Entities.Add(new Insert(block, Vector2.Zero));
            doc.Save(path);
            var before = File.ReadAllBytes(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            Assert.Equal(30, asset.Width);
            Assert.Equal(20, asset.Height);
            Assert.DoesNotContain(asset.Primitives, p => p.Text == "PRIVATE_CONFIG" || p.Text == "1");
            Assert.Contains(asset.Primitives, p => p.Text == "DEVICE");
            Assert.Equal("TAG1", Assert.Single(asset.Primitives, p => p.Text == "DEVICE").AttributeTag);
            Assert.Equal("X1TERM01", Assert.Single(asset.ConnectionPoints).Tag);
            Assert.Null(asset.ConnectionPoints[0].SourcePinId);
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StandaloneSymbolReadsHiddenModelSpaceContactDefinitionsWithoutGuessingPinIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new DxfDocument();
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(30, 20)));
            doc.Blocks[netDxf.Blocks.Block.DefaultModelSpaceName].AttributeDefinitions.Add(
                new AttributeDefinition("X4TERM05") { Position = new Vector3(0, 10, 0), Flags = AttributeFlags.Hidden });
            doc.Blocks[netDxf.Blocks.Block.DefaultModelSpaceName].AttributeDefinitions.Add(
                new AttributeDefinition("TERM05") { Position = new Vector3(5, 10, 0), Value = "OUT-", Height = 2 });
            doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var point = Assert.Single(asset.ConnectionPoints);
            Assert.Equal("X4TERM05", point.Tag); Assert.Null(point.SourcePinId);
            Assert.Equal("Left", point.Direction);
            Assert.Equal(new SchematicPoint(0, 10), point.Position);
            Assert.Contains(asset.Primitives, p => p.Kind == "TEXT" && p.Text == "OUT-");
            Assert.DoesNotContain(asset.Primitives, p => p.Text == "X4TERM05");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MultilineTextRemainsVisibleWithExplicitFormattingLimitation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new DxfDocument();
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(100, 100)));
            doc.Entities.Add(new MText(@"Company\PProject", new Vector2(20, 80), 3, 40)
                { AttachmentPoint = MTextAttachmentPoint.TopLeft });
            doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var text = Assert.Single(asset.Primitives, p => p.Kind == "MTEXT");
            Assert.Contains("Company", text.Text); Assert.Contains("Project", text.Text);
            Assert.DoesNotContain(@"\P", text.Text);
            Assert.Equal(new SchematicPoint(20, 20), text.Start);
            Assert.Contains(asset.Diagnostics, d => d.Contains("MText formatting"));
            Assert.False(asset.Complete);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ImportsFixedGeometryWithExplicitScaleAndPreservesSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new DxfDocument();
            doc.Entities.Add(new Line(new Vector2(10, 20), new Vector2(30, 20)));
            doc.Entities.Add(new Line(new Vector2(10, 20), new Vector2(10, 40)));
            doc.Entities.Add(new Circle(new Vector2(20, 30), 2));
            doc.Entities.Add(new Arc(new Vector2(20, 30), 3, 0, 180));
            doc.Save(path);
            var bytes = File.ReadAllBytes(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 2);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), asset.SourceSha256);
            Assert.Equal(40, asset.Width);
            Assert.Equal(40, asset.Height);
            Assert.Contains(asset.Primitives, p => p.Kind == "LINE" && p.Start == new SchematicPoint(0, 40) && p.End == new SchematicPoint(40, 40));
            Assert.Contains(asset.Primitives, p => p.Kind == "CIRCLE" && p.Radius == 4);
            Assert.Empty(asset.Diagnostics);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnsupportedGeometryIsReportedAndCannotPretendComplete()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new DxfDocument();
            doc.Entities.Add(new Line(new Vector2(0, 0), new Vector2(20, 20)));
            doc.Entities.Add(new Ray(new Vector2(0, 0), new Vector2(1, 0)));
            doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            Assert.Contains(asset.Diagnostics, d => d.Contains("Ray"));
            Assert.False(asset.Complete);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SchematicCadImporter().ReadDxf(path, double.NaN));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ConnectionAttributesAreInventoryNotAutomaticPinBindings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cad-{Guid.NewGuid():N}.dxf");
        try
        {
            var block = new netDxf.Blocks.Block("TEST");
            block.Entities.Add(new Line(new Vector2(0, 0), new Vector2(20, 20)));
            block.AttributeDefinitions.Add(new AttributeDefinition("X1TERM01") { Position = new Vector3(0, 5, 0), Value = "1" });
            var doc = new DxfDocument(); doc.Entities.Add(new Insert(block, new Vector2(10, 10))); doc.Save(path);
            var asset = new SchematicCadImporter().ReadDxf(path, 1);
            var contact = Assert.Single(asset.ConnectionPoints);
            Assert.Equal("X1TERM01", contact.Tag);
            Assert.Equal(new SchematicPoint(0, 15), contact.Position);
            Assert.Null(contact.SourcePinId);
        }
        finally { File.Delete(path); }
    }
}
