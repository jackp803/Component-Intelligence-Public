using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPageFormatTests
{
    [Fact]
    public void NewPageUsesExplicitActiveFormatWithoutDuplicatingPageContent()
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "test" }, "Source");
        var source = project.Schematic!.Pages[0] with
        {
            Width = 500, Height = 300, Margin = 12, GridColumns = 10, GridRows = 6,
            CoordinateGrid = new(20, 15, 460, 260), TemplatePath = "company.dxf",
            TemplateSha256 = new string('A', 64),
            TitleBlockContentOverride = "Previous page only",
            TitleBlockSlots = [new("SheetContent", new(30, 250, 80, 10))],
            TemplateGeometry = new() { SourceSha256 = new string('A', 64), Width = 500, Height = 300,
                Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(500, 0) }] }
        };
        project.Schematic.Pages[0] = source;
        var next = service.AddPage(project, "Next", source.PageId);
        var added = next.Schematic!.Pages[1];
        Assert.NotEqual(source.PageId, added.PageId);
        Assert.Equal("Next", added.Title);
        Assert.Equal(source with { PageId = added.PageId, Title = "Next", TemplateGeometry = added.TemplateGeometry,
            TitleBlockContentOverride = null, TitleBlockSlots = added.TitleBlockSlots }, added);
        Assert.Null(added.TitleBlockContentOverride);
        Assert.Equal(source.TitleBlockSlots, added.TitleBlockSlots);
        Assert.NotSame(source.TitleBlockSlots, added.TitleBlockSlots);
        Assert.NotSame(source.TemplateGeometry, added.TemplateGeometry);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(source.TemplateGeometry),
            System.Text.Json.JsonSerializer.Serialize(added.TemplateGeometry));
        Assert.Single(project.Schematic.Pages);
        Assert.Empty(next.Schematic.Symbols);
        Assert.Empty(next.Schematic.Wires);
        Assert.Empty(next.Schematic.Continuations);
    }

    [Fact]
    public void MissingExplicitSourceFailsWithoutChangingProject()
    {
        var project = new ElectricalProject { ProjectId = "test" };
        Assert.Throws<InvalidOperationException>(() => new SchematicAuthoringService().AddPage(project, "Next", "missing"));
        Assert.Null(project.Schematic);
    }

    [Fact]
    public void FirstPageRetainsDefaultFormat()
    {
        var page = Assert.Single(new SchematicAuthoringService().AddPage(new ElectricalProject { ProjectId = "test" }, "First").Schematic!.Pages);
        Assert.Equal(420, page.Width);
        Assert.Equal(297, page.Height);
        Assert.Null(page.TemplateGeometry);
        Assert.Null(page.CableDetail);
    }
}
