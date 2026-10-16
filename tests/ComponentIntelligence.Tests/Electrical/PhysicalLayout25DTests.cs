using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class PhysicalLayout25DTests
{
    [Theory]
    [InlineData(ComponentMountOrientation.Front, 0, 102, 82, 259)]
    [InlineData(ComponentMountOrientation.Front, 90, 82, 102, 259)]
    [InlineData(ComponentMountOrientation.Side, 0, 259, 82, 102)]
    [InlineData(ComponentMountOrientation.Side, 90, 82, 259, 102)]
    [InlineData(ComponentMountOrientation.Top, 0, 102, 259, 82)]
    public void FootprintProjection_UsesMountedFaceAndPlanarRotation(
        ComponentMountOrientation orientation,
        int rotation,
        double expectedWidth,
        double expectedHeight,
        double expectedProtrusion)
    {
        var footprint = new PhysicalFootprint { WidthMm = 102, HeightMm = 82, DepthMm = 259 };
        var placement = new PhysicalPlacement
        {
            ParentContainerId = "cab",
            MountOrientation = orientation,
            RotationDegrees = rotation
        };

        var projection = PhysicalFootprintProjection.Project(footprint, placement);

        Assert.Equal(expectedWidth, projection.WidthMm);
        Assert.Equal(expectedHeight, projection.HeightMm);
        Assert.Equal(expectedProtrusion, projection.ProtrusionMm);
    }

    [Fact]
    public void SideMountedComponent_UsesWidthAsCabinetProtrusion()
    {
        var project = CabinetProject(depthMm: 120);
        var component = Component("psu", "PSU01", MountingSurface.Backplate, 10, 10, 102, 82, 259);
        component.Placement!.MountOrientation = ComponentMountOrientation.Side;
        project.Components.Add(component);

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.DoesNotContain(issues, issue => issue.RuleId == "RULE-LAYOUT-006" && issue.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public void SameSurfaceBodyOverlap_WithOverlappingDepth_Blocks()
    {
        var project = CabinetProject(depthMm: 250);
        project.Components.Add(Component("a", "A", MountingSurface.Backplate, 20, 20, 100, 100, 60));
        project.Components.Add(Component("b", "B", MountingSurface.Backplate, 80, 60, 100, 100, 50));

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.Contains(issues, issue => issue.RuleId == "RULE-LAYOUT-002" && issue.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public void DoorAndBackplate_XyOverlapWithoutDepthOverlap_IsAllowed()
    {
        var project = CabinetProject(depthMm: 250);
        project.Components.Add(Component("plc", "PLC01", MountingSurface.Backplate, 100, 100, 120, 120, 80));
        project.Components.Add(Component("hmi", "HMI01", MountingSurface.Door, 100, 100, 120, 120, 40));

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.DoesNotContain(issues, issue => issue.RuleId == "RULE-LAYOUT-002" && issue.Severity == ValidationSeverity.Block);
        Assert.DoesNotContain(issues, issue => issue.RuleId == "RULE-LAYOUT-009" && issue.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public void DoorAndBackplate_DepthVolumesOverlap_BlocksDoorClosure()
    {
        var project = CabinetProject(depthMm: 250);
        project.Components.Add(Component("deep-plc", "PLC01", MountingSurface.Backplate, 100, 100, 120, 120, 180));
        project.Components.Add(Component("deep-hmi", "HMI01", MountingSurface.Door, 100, 100, 120, 120, 100));

        var issues = new PhysicalLayoutValidator().Validate(project);

        var collision = Assert.Single(issues, issue => issue.RuleId == "RULE-LAYOUT-009" && issue.Severity == ValidationSeverity.Block);
        Assert.Contains("Door Closure Collision", collision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownMountingSurface_DoesNotTurn2dOverlapIntoFalseCollision()
    {
        var project = CabinetProject(depthMm: 250);
        project.Components.Add(Component("a", "A", MountingSurface.Unknown, 20, 20, 100, 100, 60));
        project.Components.Add(Component("b", "B", MountingSurface.Unknown, 40, 40, 100, 100, 60));

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.DoesNotContain(issues, issue => issue.RuleId == "RULE-LAYOUT-002" && issue.Severity == ValidationSeverity.Block);
        Assert.Contains(issues, issue => issue.RuleId == "RULE-LAYOUT-008" && issue.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void ComponentDepthExceedingCabinet_BlocksFit()
    {
        var project = CabinetProject(depthMm: 250);
        project.Components.Add(Component("deep", "DEEP01", MountingSurface.Backplate, 50, 50, 100, 100, 300));

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.Contains(issues, issue => issue.RuleId == "RULE-LAYOUT-006" && issue.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public void TerminalBlocks_CountTowardDinRailCapacity()
    {
        var project = CabinetProject(depthMm: 250);
        project.DinRails.Add(new DinRail
        {
            DinRailId = "rail-1",
            ParentContainerId = "cab",
            XMm = 0,
            YMm = 0,
            LengthMm = 100,
            Surface = MountingSurface.Backplate
        });
        project.TerminalBlocks.Add(Terminal("tb1", "TB1", 60));
        project.TerminalBlocks.Add(Terminal("tb2", "TB2", 60, x: 60));

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.Contains(issues, issue => issue.RuleId == "RULE-LAYOUT-007" && issue.Severity == ValidationSeverity.Block);
    }

    private static ElectricalProject CabinetProject(double depthMm)
    {
        var project = new ElectricalProject { ProjectId = Guid.NewGuid().ToString("N") };
        project.LayoutContainers.Add(new LayoutContainer
        {
            ContainerId = "cab",
            Name = "CAB-01",
            WidthMm = 600,
            HeightMm = 800,
            DepthMm = depthMm
        });
        return project;
    }

    private static ComponentInstance Component(
        string id,
        string reference,
        MountingSurface surface,
        double x,
        double y,
        double width,
        double height,
        double depth,
        double depthOffset = 0) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"def-{id}",
        TypeKey = "DEVICE",
        ReferenceDesignator = reference,
        Footprint = new PhysicalFootprint
        {
            WidthMm = width,
            HeightMm = height,
            DepthMm = depth,
            MountingType = surface == MountingSurface.Door ? MountingType.Door : MountingType.Backplate
        },
        Placement = new PhysicalPlacement
        {
            ParentContainerId = "cab",
            XMm = x,
            YMm = y,
            Surface = surface,
            DepthOffsetMm = depthOffset
        }
    };

    private static TerminalBlock Terminal(string id, string reference, double width, double x = 0) => new()
    {
        TerminalBlockId = id,
        ReferenceDesignator = reference,
        Footprint = new PhysicalFootprint
        {
            WidthMm = width,
            HeightMm = 50,
            DepthMm = 60,
            MountingType = MountingType.DinRail
        },
        Placement = new PhysicalPlacement
        {
            ParentContainerId = "cab",
            XMm = x,
            YMm = 100,
            MountTargetId = "rail-1",
            Surface = MountingSurface.Backplate
        }
    };
}
