using ComponentIntelligence.Electrical.Cables;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ConnectionCableRequirementBuilderTests
{
    [Fact]
    public void Build_Rs485ConnectionProducesCommunicationAndTwistedPairRequirements()
    {
        var project = new ElectricalProject { ProjectId = "p" };
        var left = AddRs485(project, "left", ConnectorGender.Female);
        var right = AddRs485(project, "right", ConnectorGender.Male);
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "c1",
            FromEndpointId = left.PortId,
            ToEndpointId = right.PortId,
            Kind = ConnectionKind.Cable
        });

        var requirement = new ConnectionCableRequirementBuilder().Build(project, "c1");

        Assert.Equal(2, requirement.Conductors.Count);
        Assert.Equal(1, requirement.MinTwistedPairCount);
        Assert.Contains("RS485", requirement.CommunicationStandards, StringComparer.OrdinalIgnoreCase);
        Assert.All(requirement.Conductors, conductor => Assert.Equal(ElectricalLayer.Communication, conductor.Layer));
    }

    [Fact]
    public void Build_DoesNotInventConductorCountWhenPinMappingIsUnknown()
    {
        var project = new ElectricalProject { ProjectId = "p" };
        var left = AddUnknownPort(project, "left");
        var right = AddUnknownPort(project, "right");
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "c1",
            FromEndpointId = left.PortId,
            ToEndpointId = right.PortId,
            Kind = ConnectionKind.Cable
        });

        var requirement = new ConnectionCableRequirementBuilder().Build(project, "c1");

        Assert.Empty(requirement.Conductors);
        Assert.Null(requirement.MinTwistedPairCount);
    }

    private static ComponentPort AddRs485(ElectricalProject project, string id, ConnectorGender gender)
    {
        var port = new ComponentPort
        {
            PortId = $"{id}:port",
            Name = "RS485",
            Protocol = "RS485",
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{id}:connector",
                Family = "M12",
                Coding = "A",
                PinCount = 2,
                Gender = gender
            },
            Pins =
            {
                new ComponentPin { PinId = $"{id}:a", PinNumber = "1", Function = "A", Protocol = "RS485", Layer = ElectricalLayer.Communication, DifferentialRole = DifferentialRole.Positive, Status = PinStatus.Normal, IsRequired = true },
                new ComponentPin { PinId = $"{id}:b", PinNumber = "2", Function = "B", Protocol = "RS485", Layer = ElectricalLayer.Communication, DifferentialRole = DifferentialRole.Negative, Status = PinStatus.Normal, IsRequired = true }
            }
        };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = id,
            TypeKey = "DEVICE",
            Ports = { port }
        });
        return port;
    }

    private static ComponentPort AddUnknownPort(ElectricalProject project, string id)
    {
        var port = new ComponentPort
        {
            PortId = $"{id}:port",
            Name = "X1",
            Connector = new ConnectorDefinition { ConnectorId = $"{id}:connector", Family = "M12" }
        };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = id,
            ComponentDefinitionId = id,
            TypeKey = "DEVICE",
            Ports = { port }
        });
        return port;
    }
}
