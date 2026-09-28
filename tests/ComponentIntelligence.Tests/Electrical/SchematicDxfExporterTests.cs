using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicDxfExporterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompanyTemplateOwnsFrameLabelsWithoutGeneratedOverlay(bool imported)
    {
        var project = new SchematicAuthoringService().AddPage(new ElectricalProject { ProjectId = "TEST" }, "Sheet");
        var page = project.Schematic!.Pages[0];
        if (imported)
            project.Schematic.Pages[0] = page with { TemplateGeometry = new SchematicCadAsset
            { SourceSha256 = new string('A', 64), Width = 420, Height = 297,
                Primitives = [new() { Kind = "TEXT", Text = "COMPANY", Start = new(20, 280), TextHeight = 3 }] } };
        var before = JsonSerializer.Serialize(project);
        using var stream = new MemoryStream(Assert.Single(new SchematicDxfExporter().Create(project)).Dxf);
        var labels = DxfDocument.Load(stream).Entities.Texts.Where(t => t.Layer.Name == "SCHEMATIC_FRAME").ToArray();
        if (imported) Assert.Empty(labels);
        else Assert.Equal(page.GridColumns + page.GridRows + 1, labels.Length);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData("TopLeft", 0)]
    [InlineData("MiddleCenter", 90)]
    [InlineData("BottomRight", 270)]
    public void MTextExportRetainsWidthAttachmentAndLiteralMultilineContent(string attachment, double rotation)
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "Multiline");
        var page = project.Schematic!.Pages[0];
        var primitive = new SchematicCadPrimitive { Kind = "MTEXT", Start = new(41.125, 62.875),
            Text = "First {literal} \\P\nSecond", TextHeight = 3.5, TextWidth = 22.75,
            TextAttachment = attachment, Rotation = rotation };
        project.Schematic.Pages[0] = page with { TemplateGeometry = new SchematicCadAsset
            { SourceSha256 = new string('A', 64), Width = page.Width, Height = page.Height, Primitives = [primitive] } };
        using var stream = new MemoryStream(Assert.Single(new SchematicDxfExporter().Create(project)).Dxf);
        var exported = Assert.Single(DxfDocument.Load(stream).Entities.MTexts);
        Assert.Equal(primitive.Text, exported.PlainText().Replace("\r\n", "\n"));
        Assert.Equal(attachment, exported.AttachmentPoint.ToString());
        Assert.Equal(primitive.TextWidth, exported.RectangleWidth, 8);
        Assert.Equal(primitive.Start.X, exported.Position.X, 8);
        Assert.Equal(page.Height - primitive.Start.Y, exported.Position.Y, 8);
        Assert.Equal((360 - rotation) % 360, exported.Rotation, 8);
    }

    [Theory]
    [InlineData(0, 40, 57)]
    [InlineData(90, 43, 60)]
    [InlineData(180, 40, 63)]
    [InlineData(270, 37, 60)]
    public void PreviewTextRotationKeepsTheBaselineFixed(double rotation, double x, double y)
    {
        var primitive = new SchematicCadPrimitive { Kind = "TEXT", Start = new(40, 60), TextHeight = 3, Rotation = rotation };
        var origin = primitive.PreviewTextOrigin();
        Assert.Equal(x, origin.X, 8);
        Assert.Equal(y, origin.Y, 8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void CadTextRetainsItsBaselineAndRotationInsteadOfBecomingTopAligned(double rotation)
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "Text");
        var page = project.Schematic!.Pages[0];
        var text = new SchematicCadPrimitive { Kind = "TEXT", Start = new(41.125, 62.875),
            Text = "CAD baseline", TextHeight = 3.5, Rotation = rotation };
        project.Schematic.Pages[0] = page with { TemplateGeometry = new SchematicCadAsset
            { SourceSha256 = new string('A', 64), Width = page.Width, Height = page.Height, Primitives = [text] } };
        using var stream = new MemoryStream(Assert.Single(new SchematicDxfExporter().Create(project)).Dxf);
        var exported = Assert.Single(DxfDocument.Load(stream).Entities.Texts, t => t.Value == text.Text);
        Assert.Equal(netDxf.Entities.TextAlignment.BaselineLeft, exported.Alignment);
        Assert.Equal(text.Start.X, exported.Position.X, 8);
        Assert.Equal(page.Height - text.Start.Y, exported.Position.Y, 8);
        Assert.Equal((360 - rotation) % 360, exported.Rotation, 8);
    }

    [Fact]
    public void DraftExportKeepsPageOrderExactGeometryAndProjectUnchanged()
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "TEST", Name = "Synthetic" }, "First");
        var page = project.Schematic!.Pages[0];
        project = service.DrawWire(project, page.PageId, SchematicAttachment.Free(), SchematicAttachment.Free(),
            [new(20, 30), new(50, 30), new(50, 60)]);
        project = service.SetWireAwg(project, project.Schematic!.Wires[0].WireId, 18);
        project = service.AddPage(project, "Second");
        var before = JsonSerializer.Serialize(project);
        var sheets = new SchematicDxfExporter().Create(project);
        Assert.Equal(project.Schematic!.Pages.Select(p => p.PageId), sheets.Select(p => p.PageId));
        Assert.Contains(sheets[0].Diagnostics, d => d.Contains("INCOMPLETE_WIRE"));
        using var stream = new MemoryStream(sheets[0].Dxf);
        var dxf = DxfDocument.Load(stream);
        var lines = dxf.Entities.Lines.Where(l => l.Layer.Name == "SCHEMATIC_DRAFT_WIRE").ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, l => l.StartPoint.X == 20 && l.StartPoint.Y == page.Height - 30 && l.EndPoint.X == 50);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ExportUsesTheSameCrossingPrimitivesAndNoElectricalJunctionForUnrelatedWires()
    {
        var service = new SchematicAuthoringService();
        var p = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "Crossing");
        var page = p.Schematic!.Pages[0].PageId;
        p = service.DrawWire(p, page, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(20, 40), new(80, 40)]);
        p = service.DrawWire(p, page, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(50, 20), new(50, 70)]);
        var sheet = Assert.Single(new SchematicDxfExporter().Create(p));
        using var stream = new MemoryStream(sheet.Dxf);
        var dxf = DxfDocument.Load(stream);
        Assert.Single(dxf.Entities.Arcs);
        Assert.DoesNotContain(dxf.Entities.All, e => e.Layer.Name == "SCHEMATIC_JUNCTION");
    }

    [Fact]
    public void CrossPageLabelsUseCurrentPeerPageAndGridWithoutCreatingConnections()
    {
        var service = new SchematicAuthoringService();
        var p = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "First");
        p = service.AddPage(p, "Second");
        foreach (var page in p.Schematic!.Pages.ToArray())
            p = service.DrawWire(p, page.PageId, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(20, 40), new(80, 40)]);
        p = service.ConnectAcrossPages(p, p.Schematic!.Wires[0].WireId, false, p.Schematic.Wires[1].WireId, true, "Signal");
        var marker = p.Schematic!.Continuations[0].Source;
        var expected = service.ReferenceFor(p, marker.MarkerId);
        var sheets = new SchematicDxfExporter().Create(p);
        using var stream = new MemoryStream(sheets[0].Dxf);
        var dxf = DxfDocument.Load(stream);
        Assert.Contains(dxf.Entities.Texts, t => t.Value == expected);
        Assert.Empty(p.Connections);
    }
}
