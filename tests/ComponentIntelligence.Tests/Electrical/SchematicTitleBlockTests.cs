using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicTitleBlockTests
{
    [Fact]
    public void ImportedTemplateDetectsFillableTitleAndSheetCells()
    {
        var asset = Template();
        var slots = SchematicTitleBlock.DetectSlots(asset);

        var title = Assert.Single(slots, s => s.Field == "SheetContent");
        var sheet = Assert.Single(slots, s => s.Field == "SheetNumber");
        Assert.InRange(title.Bounds.X, 50, 55);
        Assert.InRange(title.Bounds.Y, 43.5, 47);
        Assert.True(title.Bounds.Width > 30);
        Assert.True(sheet.Bounds.Width > 5);
        Assert.Contains(slots, s => s.Field == "CheckedBy");
    }

    [Fact]
    public void MainModulesSuggestContentButManualOverrideAndPageOrderPersist()
    {
        var component = new ComponentInstance { ComponentInstanceId = "runtime-1",
            ComponentDefinitionId = "source-module", TypeKey = "Module", DisplayName = "MOXA EDS-2005-EL" };
        var first = new SchematicPage { PageId = "first", Title = "工程圖 1" };
        var second = new SchematicPage { PageId = "second", Title = "工程圖 2" };
        var project = new ElectricalProject { ProjectId = "project", Name = "Control cabinet", Components = [component],
            Schematic = new SchematicDocument { Pages = [first, second], Symbols =
                [new SchematicSymbol { SymbolId = "symbol", ComponentInstanceId = component.ComponentInstanceId,
                    PageId = first.PageId }] } };
        var service = new SchematicAuthoringService();
        project = service.SetPageTemplate(project, first.PageId, Template(), "isolated-template.dwt",
            100, 60, 5, 8, 5);
        Assert.Contains(project.Schematic!.Pages[0].TitleBlockSlots, s => s.Field == "SheetContent");
        Assert.Contains("MOXA EDS-2005-EL", SchematicTitleBlock.Value(project, project.Schematic.Pages[0], "SheetContent"));
        Assert.Equal("1/2", SchematicTitleBlock.Value(project, project.Schematic.Pages[0], "SheetNumber"));

        project = service.SetTitleBlock(project, first.PageId,
            new SchematicTitleBlockSettings { DrawingNumber = "DWG-24", DrawnBy = "Jack" }, "Network wiring");
        project.Schematic!.Pages.Reverse();
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;

        Assert.Equal("Network wiring", SchematicTitleBlock.Value(reloaded, reloaded.Schematic!.Pages[1], "SheetContent"));
        Assert.Equal("2/2", SchematicTitleBlock.Value(reloaded, reloaded.Schematic.Pages[1], "SheetNumber"));
        Assert.Equal("DWG-24", SchematicTitleBlock.Value(reloaded, reloaded.Schematic.Pages[1], "DrawingNumber"));
        Assert.Equal("Jack", SchematicTitleBlock.Value(reloaded, reloaded.Schematic.Pages[1], "DrawnBy"));
    }

    private static SchematicCadAsset Template()
    {
        static SchematicCadPrimitive Text(string value, double x, double y) =>
            new() { Kind = "TEXT", Text = value, TextHeight = 2, Start = new(x, y) };
        static SchematicCadPrimitive Line(double x1, double y1, double x2, double y2) =>
            new() { Kind = "LINE", Start = new(x1, y1), End = new(x2, y2) };
        return new SchematicCadAsset { SourceSha256 = new string('a', 64), Width = 100, Height = 60,
            MillimetresPerUnit = 1, Primitives =
            [
                Text("TITLE:", 50, 42), Text("SHEET", 80, 51), Text("CHKD:", 50, 52),
                Line(98, 40, 98, 59), Line(50, 50, 98, 50), Line(50, 59, 98, 59),
                Line(78, 50, 78, 59)
            ] };
    }
}
