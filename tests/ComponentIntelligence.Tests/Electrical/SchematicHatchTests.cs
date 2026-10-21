using ComponentIntelligence.Electrical.Schematic;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicHatchTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void PolylineClosureComesFromSourceFlag(bool closed, bool inBlock)
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        try
        {
            var edge = new HatchBoundaryPath.Polyline
            {
                IsClosed = closed,
                Vertexes = [new(0, 0, 0), new(20, 0, 0), new(20, 20, 0), new(0, 20, 0)]
            };
            var hatch = new Hatch(HatchPattern.Solid,
                [new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { edge })], false);
            var doc = new DxfDocument();
            if (inBlock)
            {
                var block = new netDxf.Blocks.Block("ClosureFixture");
                block.Entities.Add(hatch);
                doc.Entities.Add(new Insert(block, new Vector3(30, 40, 0)));
            }
            else doc.Entities.Add(hatch);
            doc.Entities.Add(new Line(Vector2.Zero, new Vector2(100, 0)));
            doc.Save(file);
            var asset = new SchematicCadImporter().ReadDxf(file, 1);
            if (closed)
            {
                Assert.Equal(4, Assert.Single(Assert.Single(asset.Primitives, p => p.Kind == "SOLID_HATCH").Contours).Count);
                Assert.Empty(asset.Diagnostics);
            }
            else
            {
                Assert.DoesNotContain(asset.Primitives, p => p.Kind == "SOLID_HATCH");
                Assert.Contains(asset.Diagnostics, d => d.Contains("Unsupported Hatch boundary"));
            }
        }
        finally { File.Delete(file); }
    }

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
