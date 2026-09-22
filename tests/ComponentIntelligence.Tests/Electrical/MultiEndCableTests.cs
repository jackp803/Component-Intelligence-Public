using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class MultiEndCableTests
{
    [Fact]
    public void PhysicalTopologySurvivesProjectSerializationWithoutElectricalSplit()
    {
        const string json = """
            {"ProjectId":"p","CableAssemblies":[{"CableAssemblyId":"a",
            "PhysicalTopology":{"CableInstanceId":"c","CommonPortId":"common",
            "ConnectionIds":["wire-1","wire-2"],"Branches":[
            {"PortId":"branch-a","Index":1},{"PortId":"branch-b","Index":3}]}}]}
            """;
        var project = JsonSerializer.Deserialize<ElectricalProject>(json)!;
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(project));
        Assert.True(saved.RootElement.GetProperty("CableAssemblies")[0]
            .TryGetProperty("PhysicalTopology", out var topology));
        Assert.Equal(3, topology.GetProperty("Branches")[1].GetProperty("Index").GetInt32());
        Assert.Empty(project.Components);
        Assert.Empty(project.Connections);
    }
}
