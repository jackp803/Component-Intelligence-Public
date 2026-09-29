using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicContinuationNavigationTests
{
    [Fact]
    public void PdfLinksLeadToPairedMarkerAndFollowPageReorder()
    {
        var service = new SchematicAuthoringService();
        var project = new ElectricalProject { ProjectId = "test", SchemaVersion = "0.7" };
        project = service.AddPage(project, "Source");
        project = service.AddPage(project, "Target");
        var sourcePage = project.Schematic!.Pages[0];
        var targetPage = project.Schematic.Pages[1];
        project = service.AddContinuation(project, "24V", sourcePage.PageId, new(30, 40), targetPage.PageId, new(80, 90));
        var pair = project.Schematic!.Continuations.Single();

        var first = SchematicContinuationNavigation.Links(project.Schematic, sourcePage.PageId).Single();
        Assert.Equal(pair.Source.MarkerId, first.MarkerId);
        Assert.Equal(1, first.DestinationPageIndex);
        Assert.Equal(pair.Destination.Position, first.Destination);
        Assert.True(first.LinkWidth >= 28);
        Assert.Equal("2.2-B", service.ReferenceCodeFor(project.Schematic, pair.Source.MarkerId));
        Assert.Contains("2.2-B", service.ReferenceFor(project.Schematic, pair.Source.MarkerId));

        project = service.ReorderPages(project, [targetPage.PageId, sourcePage.PageId]);
        var reverse = SchematicContinuationNavigation.Links(project.Schematic!, targetPage.PageId).Single();
        Assert.Equal(1, reverse.DestinationPageIndex);
        Assert.Equal(pair.Source.Position, reverse.Destination);
        Assert.Equal("24V", reverse.Signal);
        Assert.Equal("1.2-B", service.ReferenceCodeFor(project.Schematic!, pair.Source.MarkerId));
    }
}
