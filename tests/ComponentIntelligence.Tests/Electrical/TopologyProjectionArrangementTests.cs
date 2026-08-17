using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyProjectionArrangementTests
{
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
