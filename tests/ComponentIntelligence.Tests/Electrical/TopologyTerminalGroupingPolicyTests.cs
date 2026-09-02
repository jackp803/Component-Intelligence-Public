using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyTerminalGroupingPolicyTests
{
    [Fact]
    public void AdjacentTerminalComponents_FormOneVisualGroupWithoutChangingProjectObjects()
    {
        var project = Project();
        AddTerminal(project, "t1", 100, 100);
        AddTerminal(project, "t2", 100, 160);
        AddTerminal(project, "t3", 100, 220);

        var group = Assert.Single(new TopologyTerminalGroupingPolicy().BuildGroups(project));

        Assert.Equal(new[] { "t1", "t2", "t3" }, group.ComponentInstanceIds);
        Assert.Equal(3, project.Components.Count);
        Assert.Equal(3, project.TopologyPlacements.Count);
        Assert.Equal(100, group.Bounds.X);
        Assert.Equal(100, group.Bounds.Y);
        Assert.Equal(110, group.Bounds.Width);
        Assert.Equal(174, group.Bounds.Height);
    }

    [Fact]
    public void DistantOrDifferentRotationTerminals_RemainIndependent()
    {
        var project = Project();
        AddTerminal(project, "near", 100, 100);
        AddTerminal(project, "far", 100, 300);
        AddTerminal(project, "rotated", 100, 160, rotation: 90);

        Assert.Empty(new TopologyTerminalGroupingPolicy().BuildGroups(project));
    }

    [Fact]
    public void AdjacentNormalComponents_AreNotTreatedAsTerminalStrip()
    {
        var project = Project();
        AddComponent(project, "c1", "Power Supply", 100, 100);
        AddComponent(project, "c2", "Controller", 100, 160);

        Assert.Empty(new TopologyTerminalGroupingPolicy().BuildGroups(project));
    }

    private static ElectricalProject Project() => new() { ProjectId = "terminal-group-test" };

    private static void AddTerminal(
        ElectricalProject project,
        string id,
        double x,
        double y,
        int rotation = 0) =>
        AddComponent(project, id, "DIN Rail Terminal Block", x, y, rotation);

    private static void AddComponent(
        ElectricalProject project,
        string id,
        string typeKey,
        double x,
        double y,
        int rotation = 0)
    {
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = id + ":definition",
            TypeKey = typeKey,
            ReferenceDesignator = "PTTBs 2,5 (3209604) #" + (project.Components.Count + 1)
        });
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = id,
            ObjectKind = "COMPONENT",
            X = x,
            Y = y,
            Width = 110,
            Height = 54,
            RotationDegrees = rotation
        });
    }
}
