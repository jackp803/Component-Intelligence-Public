using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPreviewPageTests
{
    [Fact]
    public void HeavyWithoutContacts_RemainsVisibleForUnresolvedEvidence()
    {
        var rep = new DrawingRepresentationDecision { RepresentationId = "HD", OwnerId = "physical" };
        var input = new DrawingPlanningInput { ProjectId = "P", Representations = [rep],
            HeavyDutyConnectors = [new() { HeavyDutyConnectorId = "physical", RepresentationIds = ["HD"], RowsPerPage = 19 }] };
        Assert.Equal(rep, Assert.Single(DrawingPreviewCatalog.Build(input)));
    }

    [Fact]
    public void ContactNumberComparer_OrdersDisplayNumbersWithoutChangingTheirText()
    {
        var numbers = new[] { "12", "2", "1", "X10", "X2" };
        Assert.Equal(new[] { "1", "2", "12", "X2", "X10" }, numbers.OrderBy(n => n, DrawingContactDisplayOrder.NumberComparer));
    }

    [Fact]
    public void HeavyContinuationPreservesExactOwnerAndContactCoverageWithoutCopyingApprovedArt()
    {
        var rep = new DrawingRepresentationDecision { RepresentationId = "HD", OwnerId = "physical",
            PortBindings = Enumerable.Range(1, 55).Select(i => new DrawingPortBinding
                { EngineeringEndpointId = $"P{i:000}", ConnectionPointId = $"X{i:000}" }).ToArray() };
        var input = new DrawingPlanningInput { ProjectId = "P", Representations = [rep], HeavyDutyConnectors = [new()
            { HeavyDutyConnectorId = "physical", RepresentationIds = ["HD"], ContactIds = rep.PortBindings.Select(p => p.EngineeringEndpointId).ToArray(), RowsPerPage = 19 }] };
        var preview = DrawingPreviewCatalog.Build(input);
        Assert.Equal(new[] { "HD", "HD:CONTINUATION:002", "HD:CONTINUATION:003" }, preview.Select(r => r.RepresentationId));
        Assert.All(preview, r => Assert.Equal("physical", r.OwnerId));
        Assert.Equal(55, preview.SelectMany(r => r.PortBindings).Select(p => p.EngineeringEndpointId).Distinct().Count());
        Assert.Equal(55, rep.PortBindings.Count);
        Assert.Single(DrawingPreviewCatalog.Build(input with { Representations = [rep with { AssetPath = "approved.dwg" }] }));
    }

    [Fact]
    public void HeavyContactOrder_UsesExplicitDisplayOrderNotOpaqueEndpointIds()
    {
        var rep = new DrawingRepresentationDecision { RepresentationId="HD",OwnerId="physical",PortBindings =
            new[] { "opaque-a", "opaque-b", "opaque-c" }.Select(id=>new DrawingPortBinding { EngineeringEndpointId=id,ConnectionPointId=id }).ToArray() };
        var input = new DrawingPlanningInput { ProjectId="P",Representations=[rep],
            HeavyDutyConnectors=[new() { HeavyDutyConnectorId="physical",RepresentationIds=["HD"],ContactIds=["opaque-a","opaque-b","opaque-c"],RowsPerPage=2 }],
            WiringRules=[new() { WiringRuleId="ORDER",RuleKind="ContactDisplayOrder",Source="ElectricalProject.ComponentPin.PinNumber",
                Value=new[] { new { endpointId="opaque-c",order=0 },new {endpointId="opaque-a",order=1},new {endpointId="opaque-b",order=2} } }] };
        var result = DrawingPreviewCatalog.Build(DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(input)));
        Assert.Equal(new[] {"opaque-c","opaque-a"},result[0].PortBindings.Select(b=>b.EngineeringEndpointId));
        Assert.Equal("opaque-b",Assert.Single(result[1].PortBindings).EngineeringEndpointId);
    }

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
