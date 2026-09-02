using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class PhysicalTerminalGroupingPolicyTests
{
    [Fact]
    public void AdjacentPhysicalTerminals_FormOneVisualStripWithoutMergingData()
    {
        var project = Project();
        AddComponentTerminal(project, "t1", 100, 100);
        AddComponentTerminal(project, "t2", 110, 100);
        AddComponentTerminal(project, "t3", 120, 100);

        var group = Assert.Single(new PhysicalTerminalGroupingPolicy().BuildGroups(
            project,
            "cab-1",
            MountingSurface.Backplate));

        Assert.Equal(3, group.Members.Count);
        Assert.Equal(3, project.Components.Count);
        Assert.Empty(project.TerminalBlocks);
        Assert.Equal(30, group.Bounds.Width);
        Assert.Equal(60, group.Bounds.Height);
    }

    [Fact]
    public void DifferentSurfaceRotationOrDistance_DoesNotJoinStrip()
    {
        var project = Project();
        AddComponentTerminal(project, "base", 100, 100);
        AddComponentTerminal(project, "far", 140, 100);
        AddComponentTerminal(project, "rotated", 110, 100, rotation: 90);
        AddComponentTerminal(project, "door", 110, 100, surface: MountingSurface.Door);

        Assert.Empty(new PhysicalTerminalGroupingPolicy().BuildGroups(
            project,
            "cab-1",
            MountingSurface.Backplate));
    }

    [Fact]
    public void OrdinaryComponents_AreNeverGroupedAsTerminalStrip()
    {
        var project = Project();
        AddComponent(project, "c1", "Power Supply", 100, 100);
        AddComponent(project, "c2", "Controller", 110, 100);

        Assert.Empty(new PhysicalTerminalGroupingPolicy().BuildGroups(
            project,
            "cab-1",
            MountingSurface.Backplate));
    }

    [Fact]
    public void OverlappingPhysicalTerminals_AreRepairedIntoTouchingNonOverlappingStrip()
    {
        var project = Project();
        AddComponentTerminal(project, "t1", 100, 100);
        AddComponentTerminal(project, "t2", 100, 100);
        AddComponentTerminal(project, "t3", 100, 100);
        var policy = new PhysicalTerminalGroupingPolicy();

        Assert.True(policy.ArrangeContiguously(project, "cab-1", MountingSurface.Backplate));

        var placements = project.Components
            .Select(component => component.Placement!)
            .OrderBy(placement => placement.XMm)
            .ToArray();
        Assert.Equal([100d, 110d, 120d], placements.Select(placement => placement.XMm).ToArray());
        Assert.All(placements, placement => Assert.Equal(100d, placement.YMm));
        Assert.False(policy.ArrangeContiguously(project, "cab-1", MountingSurface.Backplate));
        Assert.Equal(3, Assert.Single(policy.BuildGroups(project, "cab-1", MountingSurface.Backplate)).Members.Count);
    }

    [Fact]
    public void RotatedPhysicalTerminals_UseProjectedWidthWhenRepairingStrip()
    {
        var project = Project();
        AddComponentTerminal(project, "t1", 100, 100, rotation: 90);
        AddComponentTerminal(project, "t2", 100, 100, rotation: 90);
        var policy = new PhysicalTerminalGroupingPolicy();

        Assert.True(policy.ArrangeContiguously(project, "cab-1", MountingSurface.Backplate));

        var placements = project.Components.Select(component => component.Placement!).OrderBy(item => item.YMm).ToArray();
        Assert.All(placements, placement => Assert.Equal(100d, placement.XMm));
        Assert.Equal([100d, 110d], placements.Select(placement => placement.YMm).ToArray());
    }

    [Fact]
    public void RotatedTerminalsPlacedAlongLongEdge_AreRepackedIntoCompactColumn()
    {
        var project = Project();
        AddComponentTerminal(project, "t1", 100, 100, rotation: 90);
        AddComponentTerminal(project, "t2", 160, 100, rotation: 90);
        AddComponentTerminal(project, "t3", 220, 100, rotation: 90);
        var policy = new PhysicalTerminalGroupingPolicy();

        Assert.True(policy.ArrangeContiguously(project, "cab-1", MountingSurface.Backplate));

        var placements = project.Components.Select(component => component.Placement!).OrderBy(item => item.YMm).ToArray();
        Assert.All(placements, placement => Assert.Equal(100d, placement.XMm));
        Assert.Equal([100d, 110d, 120d], placements.Select(placement => placement.YMm).ToArray());
    }

    [Fact]
    public void AdjacentDifferentTerminalModels_RemainSeparateVisualSections()
    {
        var project = Project();
        AddComponent(project, "feed", "DIN Rail Terminal Block", 100, 100);
        AddComponent(project, "fused", "Fused DIN Rail Terminal", 110, 100);

        Assert.Empty(new PhysicalTerminalGroupingPolicy().BuildGroups(
            project,
            "cab-1",
            MountingSurface.Backplate));
    }

    private static ElectricalProject Project() => new() { ProjectId = "physical-terminal-group-test" };

    private static void AddComponentTerminal(
        ElectricalProject project,
        string id,
        double x,
        double y,
        int rotation = 0,
        MountingSurface surface = MountingSurface.Backplate) =>
        AddComponent(project, id, "DIN Rail Terminal Block", x, y, rotation, surface);

    private static void AddComponent(
        ElectricalProject project,
        string id,
        string typeKey,
        double x,
        double y,
        int rotation = 0,
        MountingSurface surface = MountingSurface.Backplate)
    {
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = typeKey + ":definition",
            TypeKey = typeKey,
            ReferenceDesignator = "PTTB #" + (project.Components.Count + 1),
            Footprint = Footprint(),
            Placement = Placement(x, y, rotation, surface)
        });
    }

    private static void AddTerminalBlock(ElectricalProject project, string id, double x, double y)
    {
        project.TerminalBlocks.Add(new TerminalBlock
        {
            TerminalBlockId = id,
            ReferenceDesignator = "XT3",
            Footprint = Footprint(),
            Placement = Placement(x, y, 0, MountingSurface.Backplate)
        });
    }

    private static PhysicalFootprint Footprint() => new() { WidthMm = 10, HeightMm = 60, DepthMm = 45 };

    private static PhysicalPlacement Placement(
        double x,
        double y,
        int rotation,
        MountingSurface surface) => new()
    {
        ParentContainerId = "cab-1",
        XMm = x,
        YMm = y,
        RotationDegrees = rotation,
        Surface = surface
    };
}
