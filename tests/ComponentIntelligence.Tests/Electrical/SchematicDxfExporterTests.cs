using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using netDxf;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicDxfExporterTests
{
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
