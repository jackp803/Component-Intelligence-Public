using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class LegacyCompositeCableIsolationTests
{
    [Fact]
    public void LegacyDirectAssemblyCreation_IsFailClosedWithoutMutatingProject()
    {
        var project = new ElectricalProject { ProjectId = "project-1" };
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "connection-1",
            FromEndpointId = "component-a:port-1",
            ToEndpointId = "component-b:port-1"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TopologyConnectionEditor().CreateCustomCableAssembly(
                project,
                ["connection-1"],
                isYHarness: false,
                new CustomCableAssemblyOptions("CBL-01", "Legacy cable", 1000)));

        Assert.Contains("LEGACY_CP2E2_REPLACEMENT_PENDING", exception.Message, StringComparison.Ordinal);
        Assert.Empty(project.Cables);
        Assert.Empty(project.CableAssemblies);
        Assert.Null(project.Connections[0].CableInstanceId);
        Assert.Equal(ConnectionKind.Wire, project.Connections[0].Kind);
    }
}
