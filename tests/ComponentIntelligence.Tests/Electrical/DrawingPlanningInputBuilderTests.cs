using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPlanningInputBuilderTests
{
    [Fact]
    public void Build_CarriesExplicitPortAndPinDisplayNamesWithoutChangingEndpointIdentity()
    {
        var project = new ElectricalProject
        {
            ProjectId = "P-LABEL",
            Components = [new ComponentInstance
            {
                ComponentInstanceId = "IO-1", ComponentDefinitionId = "IFM_AL1342", TypeKey = "IO", DisplayName = "IFM AL1342",
                Ports = [new ComponentPort
                {
                    PortId = "OPAQUE-PORT-ID", Name = "X01",
                    Pins = [new ComponentPin { PinId = "OPAQUE-PIN-ID", PinNumber = "1", PinName = "L+" }]
                }]
            }]
        };

        var input = new DrawingPlanningInputBuilder(new RepresentationPolicy(new NoAssets())).Build(project);
        var representation = Assert.Single(input.Representations);
        Assert.Equal("IFM AL1342", representation.DisplayLabel);
        var bindings = representation.PortBindings;
        Assert.Equal("X01", Assert.Single(bindings, b => b.EngineeringEndpointId == "OPAQUE-PORT-ID").DisplayLabel);
        Assert.Equal("X01 / 1 L+", Assert.Single(bindings, b => b.EngineeringEndpointId == "OPAQUE-PIN-ID").DisplayLabel);
        Assert.Equal("X01", Assert.Single(DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(input))
            .Representations).PortBindings.Single(b => b.EngineeringEndpointId == "OPAQUE-PORT-ID").DisplayLabel);
    }

    [Fact]
    public void Build_CarriesOnlyExplicitCardinalPortSidesToPortAndPinBindings()
    {
        var project = new ElectricalProject
        {
            ProjectId = "P-ORIENTATION",
            Components = [new ComponentInstance
            {
                ComponentInstanceId = "CONVERTER-1", ComponentDefinitionId = "CONVERTER", TypeKey = "Power",
                Ports =
                [
                    new ComponentPort { PortId = "INPUT", Name = "INPUT", PhysicalLocation = new PhysicalPortLocation { Side = "Left" },
                        Pins = [new ComponentPin { PinId = "INPUT-PIN", PinNumber = "1" }] },
                    new ComponentPort { PortId = "OUTPUT", Name = "OUTPUT", PhysicalLocation = new PhysicalPortLocation { Side = "Right" },
                        Pins = [new ComponentPin { PinId = "OUTPUT-PIN", PinNumber = "2" }] },
                    new ComponentPort { PortId = "ACCESSORY", Name = "ACCESSORY", PhysicalLocation = new PhysicalPortLocation { Side = "Accessory side" } }
                ]
            }]
        };

        var representation = Assert.Single(new DrawingPlanningInputBuilder(new RepresentationPolicy(new NoAssets())).Build(project).Representations);
        Assert.All(representation.PortBindings.Where(b => b.EngineeringEndpointId.StartsWith("INPUT", StringComparison.Ordinal)),
            binding => Assert.Equal("Left", binding.PhysicalSide));
        Assert.All(representation.PortBindings.Where(b => b.EngineeringEndpointId.StartsWith("OUTPUT", StringComparison.Ordinal)),
            binding => Assert.Equal("Right", binding.PhysicalSide));
        Assert.Null(Assert.Single(representation.PortBindings, b => b.EngineeringEndpointId == "ACCESSORY").PhysicalSide);
        var roundTrip = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(new DrawingPlanningInput
        {
            ProjectId = project.ProjectId, Representations = [representation]
        }));
        Assert.Equal("Left", Assert.Single(roundTrip.Representations[0].PortBindings,
            b => b.EngineeringEndpointId == "INPUT-PIN").PhysicalSide);
    }

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
