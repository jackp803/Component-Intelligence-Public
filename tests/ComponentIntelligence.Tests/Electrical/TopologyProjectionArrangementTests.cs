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

    private static ComponentInstance Component(string id, string typeKey) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = id,
        TypeKey = typeKey,
        DisplayName = id
    };

    private static TopologyPlacement Placement(ElectricalProject project, string id) =>
        project.TopologyPlacements.Single(item => string.Equals(item.ObjectId, id, StringComparison.OrdinalIgnoreCase));
}
