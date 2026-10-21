using ComponentIntelligence.Electrical.Editing;
using System.Text.Json;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class WirePinMappingTests
{
    [Fact]
    public void ExistingPinEndpointsCanBeConfirmedWithoutCreatingCable()
    {
        var project = UnifiedCableSettingsTests.Project();
        var service = new WirePinMappingService();
        var before = JsonSerializer.Serialize(project);
        var pair = service.GetPorts(project, "C1");
        Assert.Equal("A", pair.From.PortId);
        Assert.Equal("B", pair.To.PortId);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        service.Apply(project, "C1", "A1", "B1");
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Empty(project.Cables);
    }

    [Fact]
    public void ExplicitChangePreservesConnectionAndNetIdentity()
    {
        var project = UnifiedCableSettingsTests.Project();
        project.Connections[0].ProvidedLengthMm = 500;
        project.Components[1].Ports[0].Pins.Add(new() { PinId = "B3", PinNumber = "3" });
        var service = new WirePinMappingService();
        service.Apply(project, "C1", "A1", "B3");
        var connection = project.Connections.Single(c => c.ConnectionId == "C1");
        Assert.Equal("A1", connection.FromEndpointId);
        Assert.Equal("B3", connection.ToEndpointId);
        Assert.Equal("N1", connection.NetId);
        Assert.Equal(500, connection.ProvidedLengthMm);
        Assert.Null(connection.CableInstanceId);
        Assert.Empty(project.Cables);
    }

    [Fact]
    public void InvalidSecondEndpointCausesNoPartialMutation()
    {
        var project = UnifiedCableSettingsTests.Project();
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => new WirePinMappingService().Apply(project, "C1", "A2", "missing"));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void OccupiedPinIsRejectedWithoutRewiring()
    {
        var project = UnifiedCableSettingsTests.Project();
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => new WirePinMappingService().Apply(project, "C1", "A1", "B2"));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }
}
