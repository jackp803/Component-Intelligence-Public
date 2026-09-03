using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPlanningInputBuilderTests
{
    [Fact]
    public void Build_IsPureProjectionAndDoesNotInferPlanningContextFromTypeKeyOrDisplayName()
    {
        var project = new ElectricalProject
        {
            ProjectId = "P1",
            Components =
            [
                new ComponentInstance
                {
                    ComponentInstanceId = "I1", ComponentDefinitionId = "DEF-1", TypeKey = "PLC_SENSOR_MAGIC",
                    DisplayName = "Controller Module Sensor Zone A",
                    Ports = [new ComponentPort { PortId = "P1", Name = "P1" }]
                }
            ]
        };
        var before = System.Text.Json.JsonSerializer.Serialize(project);
        var input = new DrawingPlanningInputBuilder(new RepresentationPolicy(new NoAssets())).Build(project);
        var rep = Assert.Single(input.Representations);
        Assert.Null(rep.ControllerId);
        Assert.Null(rep.PhysicalModuleId);
        Assert.Null(rep.FunctionKind);
        Assert.Null(rep.MachineZoneId);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(project));
    }

    [Fact]
    public void Build_PreservesStableConnectionCableAndNetworkEvidenceWithoutGuessingMapping()
    {
        var project = new ElectricalProject
        {
            ProjectId = "P2",
            Components =
            [
                new ComponentInstance { ComponentInstanceId = "A", ComponentDefinitionId = "DA", TypeKey = "X", Ports = [new ComponentPort { PortId = "A:P", Name = "A" }] },
                new ComponentInstance { ComponentInstanceId = "B", ComponentDefinitionId = "DB", TypeKey = "Y", Ports = [new ComponentPort { PortId = "B:P", Name = "B" }] }
            ],
            Nets = [new NetDefinition { NetId = "N1", Label = "N1", BusId = "BUS-1" }],
            Buses = [new CommunicationBus { BusId = "BUS-1", Protocol = "ExplicitProtocol", NetIds = ["N1"] }],
            Connections = [new ElectricalConnection { ConnectionId = "CONN-1", FromEndpointId = "A:P", ToEndpointId = "B:P", NetId = "N1", CableInstanceId = "CBL-1" }],
            Cables = [new CableInstance { CableInstanceId = "CBL-1", CableDefinitionId = "CD-1", CableConstructionType = CableConstructionType.Custom }]
        };
        var input = new DrawingPlanningInputBuilder(new RepresentationPolicy(new NoAssets())).Build(project);
        Assert.Equal("CONN-1", Assert.Single(input.Connections).ConnectionId);
        Assert.Equal("BUS-1", Assert.Single(input.Networks).NetworkId);
        Assert.Equal("Custom", Assert.Single(input.Cables).ConstructionType);
        Assert.Empty(Assert.Single(input.Cables).PinCoreMappings);
    }

    private sealed class NoAssets : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => null;
    }
}
