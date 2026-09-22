using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPreviewPageTests
{
    [Fact]
    public void PreviewLabelsUseNamesWithoutTurningIdentityIntoEngineeringEvidence()
    {
        var project = new ElectricalProject
        {
            ProjectId = "preview-only",
            Components = [new() { ComponentInstanceId = "internal-component", ComponentDefinitionId = "unresolved", TypeKey = "Unknown", ReferenceDesignator = "IO1", DisplayName = "IFM AL1342" },
                          new() { ComponentInstanceId = "unknown-component", ComponentDefinitionId = "unresolved", TypeKey = "Unknown" }],
            Cables = [new() { CableInstanceId = "internal-cable", CableDefinitionId = "unresolved", ReferenceDesignator = "CBL-01", DisplayName = "Sensor supply" }]
        };
        var labels = DrawingPreviewLabels.Build(project);
        Assert.Equal("IO1\nIFM AL1342", labels["REP:internal-component:Schematic"]);
        Assert.DoesNotContain("unknown-component", labels["REP:unknown-component:Schematic"]);
        Assert.StartsWith("CBL-01\nSensor supply", labels["REP:internal-cable:CableDetail"]);
        Assert.Equal(CableConstructionType.Unknown, project.Cables[0].CableConstructionType);
        Assert.Equal("unresolved", project.Components[0].ComponentDefinitionId);
    }

    [Fact]
    public void PageSelectionShowsOnlyItsRoutesIncludingContinuationLegs()
    {
        var controller = new DrawingPlanningWorkspaceController(new DrawingPlanEditService());
        Assert.Empty(controller.VisibleRoutes);
        controller.Load(new DrawingPlanDocument
        {
            ProjectId = "P", SourcePlanningInputHash = new string('1', 64), SourcePagePlanHash = new string('2', 64),
            Pages = [Page("A", 0), Page("B", 1)],
            Routes = [Route("local", "A", "C1"), Route("source", "A", "C2"), Route("destination", "B", "C2")]
        });
        Assert.Equal(new[] { "local", "source" }, controller.VisibleRoutes.Select(x => x.RouteId));
        controller.SelectPage("B");
        Assert.Equal("destination", Assert.Single(controller.VisibleRoutes).RouteId);
        Assert.Equal(3, controller.CurrentPlan!.Routes.Count);
        controller.Load(null);
        Assert.Empty(controller.VisibleRoutes);
    }

    private static DrawingPlanPage Page(string id, int order) => new()
    {
        PageId = id, Order = order, Archetype = "FieldDevices", Bounds = new(0, 0, 1000, 700)
    };

    private static DrawingRoute Route(string id, string page, string connection) => new()
    {
        RouteId = id, PageId = page, ConnectionId = connection, EndpointAId = "pin-a", EndpointBId = "pin-b",
        Points = [new(10, 10), new(40, 10)]
    };
}
