using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicHatchTests
{
    [Fact]
    public void SolidLinearHatchRetainsOuterAndHoleLoops()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try
        {
            HatchBoundaryPath Rectangle(double low, double high) => new(new EntityObject[] {
                new Line(new Vector2(low, low), new Vector2(high, low)),
                new Line(new Vector2(high, low), new Vector2(high, high)),
                new Line(new Vector2(high, high), new Vector2(low, high)),
                new Line(new Vector2(low, high), new Vector2(low, low)) });
            var doc = new DxfDocument();
            doc.Entities.Add(new Hatch(HatchPattern.Solid, new[] { Rectangle(0, 20), Rectangle(5, 15) }, false));
            doc.Save(file);
            var asset = new SchematicCadImporter().ReadDxf(file, 2);
            var hatch = Assert.Single(asset.Primitives);
            Assert.Equal("SOLID_HATCH", hatch.Kind);
            Assert.Equal(2, hatch.Contours.Count);
            Assert.All(hatch.Contours, loop => Assert.Equal(4, loop.Count));
            Assert.Equal(40, asset.Width);
            Assert.Equal(new SchematicPoint(0, 40), hatch.Contours[0][0]);
            Assert.Empty(asset.Diagnostics);
        }
        finally { File.Delete(file); }
    }
}
