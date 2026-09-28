using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicGridBoundsTests
{
    [Fact]
    public void PageSettingsDoNotRequireReimportOrModifyTemplateOrOriginal()
    {
        var service = new SchematicAuthoringService();
        var p = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "Sheet");
        var page = p.Schematic!.Pages[0];
        p.Schematic.Pages[0] = page with { TemplatePath = "unavailable-source.dwt", TemplateSha256 = new string('A', 64),
            TemplateGeometry = new() { SourceSha256 = new string('A', 64), Width = 420, Height = 297 } };
        var before = JsonSerializer.Serialize(p);
        var changed = service.SetPageSettings(p, page.PageId, 420, 297, 10, 8, 5, new(20, 30, 320, 200));
        Assert.Equal(before, JsonSerializer.Serialize(p));
        Assert.Equal("unavailable-source.dwt", changed.Schematic!.Pages[0].TemplatePath);
        Assert.Equal(JsonSerializer.Serialize(p.Schematic.Pages[0].TemplateGeometry), JsonSerializer.Serialize(changed.Schematic.Pages[0].TemplateGeometry));
        Assert.Equal("B2", changed.Schematic.Pages[0].GridCell(new(61, 71)));
    }

    [Fact]
    public void ExplicitGridUsesItsOwnOriginAndExtentAndSurvivesReload()
    {
        var page = new SchematicPage { PageId = "P", Title = "P", GridColumns = 8, GridRows = 5,
            CoordinateGrid = new(20, 30, 320, 200) };
        Assert.Equal("B2", page.GridCell(new(61, 71)));
        var loaded = JsonSerializer.Deserialize<SchematicPage>(JsonSerializer.Serialize(page))!;
        Assert.Equal(page.CoordinateGrid, loaded.CoordinateGrid);
        Assert.Equal("B2", loaded.GridCell(new(61, 71)));
        Assert.Equal("E8", loaded.GridCell(new(340, 230)));
    }

    [Fact]
    public void LegacyPageKeepsItsMarginGrid()
    {
        var page = JsonSerializer.Deserialize<SchematicPage>("{\"PageId\":\"P\",\"Title\":\"P\"}")!;
        Assert.Null(page.CoordinateGrid);
        Assert.Equal("A1", page.GridCell(new(10, 10)));
        Assert.Equal("E8", page.GridCell(new(410, 287)));
    }

    [Theory]
    [InlineData(-1, 0, 100, 100)]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 421, 100)]
    [InlineData(0, 0, 100, 298)]
    [InlineData(double.NaN, 0, 100, 100)]
    public void InvalidExplicitGridFailsClosed(double x, double y, double width, double height)
    {
        var p = new ElectricalProject { ProjectId = "TEST", Schematic = new() };
        p.Schematic.Pages.Add(new() { PageId = "P", Title = "P", CoordinateGrid = new(x, y, width, height) });
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(p));
    }
}
