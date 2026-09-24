using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadImportTests
{
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
