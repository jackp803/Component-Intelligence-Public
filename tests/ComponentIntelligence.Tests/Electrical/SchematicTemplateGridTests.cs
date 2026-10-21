using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicTemplateGridTests
{
    [Fact]
    public void UsesPairedCompanyFrameLabelsInsteadOfFallbackMarginGrid()
    {
        var geometry = Frame();
        var page = new SchematicPage { PageId = "P", Title = "Company", Width = 420, Height = 297,
            TemplateGeometry = geometry, GridColumns = 8, GridRows = 5,
            CoordinateGrid = new(10, 10, 400, 277) };
        var detected = SchematicTemplateGrid.TryDetect(geometry, page.Width, page.Height, 8, 5);
        Assert.NotNull(detected);
        Assert.InRange(detected.X, 5, 5.3);
        Assert.InRange(detected.Y, 5, 5.3);
        Assert.Equal("B5", page.GridCell(new(260.5, 62)));
        Assert.Equal("A6", (page with { TemplateGeometry = null }).GridCell(new(260.5, 62)));
    }

    [Fact]
    public void IncompleteOrConflictingFrameFailsClosedAndExplicitCustomGridWins()
    {
        var complete = Frame();
        var incomplete = complete with { Primitives = complete.Primitives.Where(p => p.Text != "8").ToArray() };
        Assert.Null(SchematicTemplateGrid.TryDetect(incomplete, 420, 297, 8, 5));
        Assert.Equal("格位待校準", new SchematicPage { PageId = "P", Title = "P", TemplateGeometry = incomplete,
            CoordinateGrid = new(10, 10, 400, 277) }.GridCell(new(260, 62)));
        var page = new SchematicPage { PageId = "P", Title = "P", TemplateGeometry = complete,
            CoordinateGrid = new(20, 30, 320, 200) };
        Assert.Equal(page.CoordinateGrid, page.EffectiveGrid());
    }

    private static SchematicCadAsset Frame()
    {
        var labels = new List<SchematicCadPrimitive>();
        for (var index = 0; index < 8; index++)
        {
            var x = 30.83 + index * 51.38;
            labels.Add(Text((index + 1).ToString(), x, 1.54));
            labels.Add(Text((index + 1).ToString(), x, 283.13));
        }
        for (var index = 0; index < 5; index++)
        {
            var y = 30.83 + index * 51.38;
            labels.Add(Text(((char)('A' + index)).ToString(), 1.54, y));
            labels.Add(Text(((char)('A' + index)).ToString(), 405.42, y));
        }
        labels.Add(Text("A", 335.55, 13.87));
        return new() { SourceSha256 = "frame", Width = 407, Height = 285, Primitives = labels };
    }

    private static SchematicCadPrimitive Text(string label, double x, double y) =>
        new() { Kind = "TEXT", Text = label, Start = new(x, y), TextHeight = 3 };
}
