using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyProjectionArrangementTests
{
    [Fact]
    public void EnsurePlacement_AddsOnlyRequestedObject()
    {
        var project = new ElectricalProject { ProjectId = "palette-first" };
        project.Components.Add(Component("C1", "PLC"));
        project.Components.Add(Component("C2", "IO-Link Master"));
        project.Components.Add(Component("C3", "Pressure Sensor"));
        var projection = new TopologyProjection();

        var placement = projection.EnsurePlacement(project, "C2", 520, 310);

        Assert.Single(project.TopologyPlacements);
        Assert.Equal("C2", placement.ObjectId);
        Assert.Equal(520, placement.X, 6);
        Assert.Equal(310, placement.Y, 6);
        Assert.Empty(projection.Build(project).Nodes.Where(node => node.ObjectId != "C2"));
    }

    [Fact]
    public void EnsurePlacement_ReusesExistingSavedPlacementWithoutDuplicate()
    {
        var project = new ElectricalProject { ProjectId = "saved-placement" };
        project.Components.Add(Component("C1", "PLC"));
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = "C1",
            ObjectKind = "COMPONENT",
            X = 123,
            Y = 456
        });
        var projection = new TopologyProjection();

        var placement = projection.EnsurePlacement(project, "C1", 900, 900);

        Assert.Single(project.TopologyPlacements);
        Assert.Equal(123, placement.X, 6);
        Assert.Equal(456, placement.Y, 6);
    }

    [Fact]
    public void EnsurePlacements_PutsFieldSensorsInFarRightLane()
    {
        var project = new ElectricalProject { ProjectId = "sensor-lane" };
        project.Components.Add(Component("PLC", "PLC"));
        project.Components.Add(Component("MASTER", "IO-Link Master"));
        project.Components.Add(Component("S1", "Pressure Sensor"));
        project.Components.Add(Component("S2", "Photoelectric Sensor"));

        new TopologyProjection().EnsurePlacements(project);

        var plc = Placement(project, "PLC");
        var master = Placement(project, "MASTER");
        var sensor1 = Placement(project, "S1");
        var sensor2 = Placement(project, "S2");

        Assert.True(sensor1.X > Math.Max(plc.X, master.X));
        Assert.Equal(sensor1.X, sensor2.X, 6);
        Assert.True(sensor2.Y > sensor1.Y);
    }

    [Fact]
    public void EnsurePlacements_DoesNotTreatSensorAmplifierAsFieldSensor()
    {
        var project = new ElectricalProject { ProjectId = "sensor-amplifier" };
        project.Components.Add(Component("K7L", "Liquid Leakage Sensor Amplifier"));
        project.Components.Add(Component("S1", "Liquid Leakage Sensor"));

        new TopologyProjection().EnsurePlacements(project);

        var amplifier = Placement(project, "K7L");
        var sensor = Placement(project, "S1");

        Assert.True(sensor.X > amplifier.X);
    }

    [Fact]
    public void ArrangeConnectedPlacements_LayersGraphLeftToRightAndPreventsNodeOverlap()
    {
        var project = new ElectricalProject { ProjectId = "connected-layout" };
        project.Components.Add(ConnectedComponent("SOURCE", "source-out"));
        project.Components.Add(ConnectedComponent("BRANCH-A", "branch-a"));
        project.Components.Add(ConnectedComponent("BRANCH-B", "branch-b"));
        project.Components.Add(ConnectedComponent("LOAD", "load-in"));
        project.Connections.Add(new ElectricalConnection { ConnectionId = "1", FromEndpointId = "source-out", ToEndpointId = "branch-a" });
        project.Connections.Add(new ElectricalConnection { ConnectionId = "2", FromEndpointId = "source-out", ToEndpointId = "branch-b" });
        project.Connections.Add(new ElectricalConnection { ConnectionId = "3", FromEndpointId = "branch-a", ToEndpointId = "load-in" });
        project.Connections.Add(new ElectricalConnection { ConnectionId = "4", FromEndpointId = "branch-b", ToEndpointId = "load-in" });
        var projection = new TopologyProjection();
        foreach (var component in project.Components)
            projection.EnsurePlacement(project, component.ComponentInstanceId, 100, 100);

        var result = projection.ArrangeConnectedPlacements(project);

        var source = Placement(project, "SOURCE");
        var branchA = Placement(project, "BRANCH-A");
        var branchB = Placement(project, "BRANCH-B");
        var load = Placement(project, "LOAD");
        Assert.True(source.X < branchA.X);
        Assert.Equal(branchA.X, branchB.X, 6);
        Assert.True(branchA.X < load.X);
        Assert.True(branchA.Y + branchA.Height <= branchB.Y || branchB.Y + branchB.Height <= branchA.Y);
        Assert.Equal(4, result.NodeCount);
        Assert.Equal(3, result.LayerCount);
        Assert.Equal(1, result.GraphGroupCount);
    }

    [Fact]
    public void ArrangeConnectedPlacements_DoesNotMaterializeUnplacedBomComponents()
    {
        var project = new ElectricalProject { ProjectId = "placed-only-layout" };
        project.Components.Add(Component("PLACED", "PLC"));
        project.Components.Add(Component("BOM-ONLY", "Sensor"));
        var projection = new TopologyProjection();
        projection.EnsurePlacement(project, "PLACED", 900, 700);

        var result = projection.ArrangeConnectedPlacements(project);

        Assert.Equal(1, result.NodeCount);
        Assert.Single(project.TopologyPlacements);
        Assert.Equal("PLACED", project.TopologyPlacements[0].ObjectId);
    }

    private static ComponentInstance Component(string id, string typeKey) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = typeKey,
        DisplayName = id
    };

    private static ComponentInstance ConnectedComponent(string id, string endpointId) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = "Device",
        DisplayName = id,
        Ports =
        {
            new ComponentPort { PortId = endpointId, Name = endpointId }
        }
    };

    private static TopologyPlacement Placement(ElectricalProject project, string id) =>
        project.TopologyPlacements.Single(item => string.Equals(item.ObjectId, id, StringComparison.OrdinalIgnoreCase));
}
