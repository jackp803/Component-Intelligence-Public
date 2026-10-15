using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class MatingReferenceCaptionTests
{
    [Fact]
    public void EachArrowShowsTheOppositeReferenceAndUpdatesAfterRename()
    {
        var (project, a, b) = ConnectorMatingTests.Drawing(); var author = new SchematicAuthoringService();
        project.Cables[0].ReferenceDesignator = "ValveCable"; project.Cables[1].ReferenceDesignator = "SensorCable";
        var from = ConnectorMatingTests.Port(project, a); var to = ConnectorMatingTests.Port(project, b);
        project = author.MatePorts(project, SchematicAttachment.Port(a.SymbolId, from.PortId), SchematicAttachment.Port(b.SymbolId, to.PortId),
            ConnectorMatingService.Prepare(project, from.PortId, to.PortId), "verified fixture", "J2 接頭對接");
        var pair = project.Schematic!.Continuations[0];
        Assert.StartsWith("SensorCable 接頭對接 / ", SchematicContinuationNavigation.Caption(project, pair.Source.MarkerId));
        Assert.StartsWith("ValveCable 接頭對接 / ", SchematicContinuationNavigation.Caption(project, pair.Destination.MarkerId));
        Assert.DoesNotContain("J2", SchematicContinuationNavigation.Caption(project, pair.Source.MarkerId));
        var dxf = netDxf.DxfDocument.Load(new MemoryStream(new SchematicDxfExporter().Create(project)[0].Dxf));
        Assert.Contains(dxf.Entities.Texts, t => t.Value.StartsWith("SensorCable 接頭對接 / ", StringComparison.Ordinal));
        project.Cables[1].ReferenceDesignator = "UpdatedCable";
        Assert.StartsWith("UpdatedCable 接頭對接 / ", SchematicContinuationNavigation.Caption(project, pair.Source.MarkerId));
        project.Cables[1].ReferenceDesignator = null;
        Assert.StartsWith("J2 接頭對接 / ", SchematicContinuationNavigation.Caption(project, pair.Source.MarkerId));
    }
}
