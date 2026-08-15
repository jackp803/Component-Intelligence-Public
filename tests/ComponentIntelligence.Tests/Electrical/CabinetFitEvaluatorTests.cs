using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CabinetFitEvaluatorTests
{
    [Fact]
    public void UnclassifiedInScopeComponent_KeepsCabinetAtReview()
    {
        var project = ProjectWithCabinet();
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "sensor",
            ComponentDefinitionId = "sensor-def",
            TypeKey = "SENSOR",
            ReferenceDesignator = "S01",
            ResponsibilityScope = ResponsibilityScope.InScope
        });

        var report = new CabinetFitEvaluator().Evaluate(project);

        Assert.Equal(CabinetFitStatus.Review, report.Status);
        Assert.Equal(1, report.UnclassifiedObjectCount);
        Assert.Contains(report.Issues, issue => issue.RuleId == "RULE-LAYOUT-011");
    }

    [Fact]
    public void ExplicitExternalComponent_DoesNotConsumeCabinetSpace()
    {
        var project = ProjectWithCabinet();
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "sensor",
            ComponentDefinitionId = "sensor-def",
            TypeKey = "SENSOR",
            ReferenceDesignator = "S01",
            ResponsibilityScope = ResponsibilityScope.InScope,
            Placement = new PhysicalPlacement
            {
                ParentContainerId = "cab",
                Surface = MountingSurface.External,
                XMm = 0,
                YMm = 0
            }
        });

        var report = new CabinetFitEvaluator().Evaluate(project);

        Assert.Equal(CabinetFitStatus.Fit, report.Status);
        Assert.Equal(0, report.UnclassifiedObjectCount);
    }

    [Fact]
    public void FullyClassifiedNonCollidingCabinet_IsFit()
    {
        var project = ProjectWithCabinet();
        project.Components.Add(Placed("plc", "PLC01", 20, 20, 120, 100, 80));
        project.Components.Add(Placed("psu", "PS01", 200, 20, 100, 120, 90));

        var report = new CabinetFitEvaluator().Evaluate(project);

        Assert.Equal(CabinetFitStatus.Fit, report.Status);
        Assert.Empty(report.Issues);
    }

    private static ElectricalProject ProjectWithCabinet()
    {
        var project = new ElectricalProject { ProjectId = Guid.NewGuid().ToString("N") };
        project.LayoutContainers.Add(new LayoutContainer
        {
            ContainerId = "cab",
            Name = "CAB-01",
            WidthMm = 600,
            HeightMm = 800,
            DepthMm = 250
        });
        return project;
    }

    private static ComponentInstance Placed(
        string id,
        string reference,
        double x,
        double y,
        double width,
        double height,
        double depth) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"def-{id}",
        TypeKey = "DEVICE",
        ReferenceDesignator = reference,
        ResponsibilityScope = ResponsibilityScope.InScope,
        Footprint = new PhysicalFootprint
        {
            WidthMm = width,
            HeightMm = height,
            DepthMm = depth,
            MountingType = MountingType.Backplate
        },
        Placement = new PhysicalPlacement
        {
            ParentContainerId = "cab",
            Surface = MountingSurface.Backplate,
            XMm = x,
            YMm = y
        }
    };
}
