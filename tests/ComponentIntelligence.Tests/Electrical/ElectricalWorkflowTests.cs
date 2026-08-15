using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Naming;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalWorkflowTests
{
    [Fact]
    public void Migrator_UpgradesV01WithoutChangingEngineeringObjects()
    {
        var oldProject = new ElectricalProject
        {
            SchemaVersion = "0.1",
            ProjectId = "p1",
            Name = "Legacy",
            Nets = { new NetDefinition { NetId = "n1", Label = "54V+", Layer = ElectricalLayer.Power } },
            Components =
            {
                new ComponentInstance
                {
                    ComponentInstanceId = "c1",
                    ComponentDefinitionId = "def1",
                    TypeKey = "SENSOR",
                    ReferenceDesignator = "S01"
                }
            }
        };

        var migrated = ElectricalProjectMigrator.Migrate(oldProject);

        Assert.Equal(ElectricalProjectMigrator.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal("p1", migrated.ProjectId);
        Assert.Same(oldProject.Components, migrated.Components);
        Assert.Same(oldProject.Nets, migrated.Nets);
        Assert.Empty(migrated.TopologyPlacements);
    }

    [Fact]
    public void ComponentService_CreatesRequestedQuantityAndUsesConfiguredNamingOnly()
    {
        var source = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "ifm-o5d100", Manufacturer = "IFM", Model = "O5D100" },
            Pins = new[]
            {
                new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "1", Function = "+24V", SignalType = "Power" },
                new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "3", Function = "0V", SignalType = "Power" }
            }
        };
        var project = new ElectricalProject { ProjectId = "p1" };
        var policy = new NamingPolicy { PrefixByTypeKey = { ["SENSOR"] = "S" } };

        var result = new ElectricalProjectComponentService().AddInstances(project, new ComponentInstantiationRequest
        {
            Component = source,
            Quantity = 2,
            TypeKey = "SENSOR",
            NamingPolicy = policy,
            EquipmentTagPrefix = "PHOTO"
        });

        Assert.Equal(2, result.Instances.Count);
        Assert.Equal(new[] { "S01", "S02" }, result.Instances.Select(item => item.ReferenceDesignator));
        Assert.Equal(new[] { "PHOTO-01", "PHOTO-02" }, result.Instances.Select(item => item.EquipmentTag));
        Assert.All(result.Instances, instance => Assert.Equal("ifm-o5d100", instance.ComponentDefinitionId));
        Assert.All(result.Instances, instance => Assert.Equal(2, instance.Ports.Single().Pins.Count));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ComponentService_DoesNotInventReferenceWhenPolicyDoesNotDefineType()
    {
        var source = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "x", Manufacturer = "Vendor", Model = "X" }
        };
        var project = new ElectricalProject { ProjectId = "p1" };

        var result = new ElectricalProjectComponentService().AddInstances(project, new ComponentInstantiationRequest
        {
            Component = source,
            Quantity = 1,
            TypeKey = "SPECIAL",
            NamingPolicy = new NamingPolicy()
        });

        Assert.Null(result.Instances.Single().ReferenceDesignator);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void TopologyMove_DoesNotModifyPhysicalPlacement()
    {
        var project = new ElectricalProject { ProjectId = "p1" };
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = "c1",
            ComponentDefinitionId = "def1",
            TypeKey = "PLC",
            ReferenceDesignator = "PLC01",
            Footprint = new PhysicalFootprint { WidthMm = 100, HeightMm = 120 },
            Placement = new PhysicalPlacement { ParentContainerId = "cab", XMm = 10, YMm = 20 }
        });
        var topology = new TopologyProjection();
        topology.EnsurePlacements(project);

        topology.Move(project, "c1", 700, 400);
        topology.Rotate(project, "c1");

        var visual = topology.GetPlacement(project, "c1");
        Assert.Equal(700, visual.X);
        Assert.Equal(400, visual.Y);
        Assert.Equal(90, visual.RotationDegrees);
        Assert.Equal(10, project.Components.Single().Placement!.XMm);
        Assert.Equal(20, project.Components.Single().Placement!.YMm);
    }

    [Fact]
    public void TopologyLayerFilter_UsesNetLayerWithoutDuplicatingProjectData()
    {
        var project = new ElectricalProject { ProjectId = "p1" };
        project.Nets.Add(new NetDefinition { NetId = "n-power", Label = "24V", Layer = ElectricalLayer.Power });
        project.Nets.Add(new NetDefinition { NetId = "n-rs485", Label = "RS485", Layer = ElectricalLayer.Communication });
        project.Components.Add(Device("c1", "A", "a-power", ElectricalLayer.Power, "a-rs", ElectricalLayer.Communication));
        project.Components.Add(Device("c2", "B", "b-power", ElectricalLayer.Power, "b-rs", ElectricalLayer.Communication));
        project.Connections.Add(new ElectricalConnection { ConnectionId = "w1", FromEndpointId = "a-power", ToEndpointId = "b-power", NetId = "n-power" });
        project.Connections.Add(new ElectricalConnection { ConnectionId = "w2", FromEndpointId = "a-rs", ToEndpointId = "b-rs", NetId = "n-rs485" });
        var topology = new TopologyProjection();
        topology.EnsurePlacements(project);

        var communication = topology.Build(project, ElectricalLayer.Communication);
        var power = topology.Build(project, ElectricalLayer.Power);

        Assert.Single(communication.Edges);
        Assert.Equal("w2", communication.Edges[0].ConnectionId);
        Assert.Single(power.Edges);
        Assert.Equal("w1", power.Edges[0].ConnectionId);
        Assert.Equal(2, project.Connections.Count);
    }

    private static ComponentInstance Device(
        string id,
        string reference,
        string firstPinId,
        ElectricalLayer firstLayer,
        string secondPinId,
        ElectricalLayer secondLayer) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"def-{id}",
        TypeKey = "DEVICE",
        ReferenceDesignator = reference,
        Ports =
        {
            new ComponentIntelligence.Electrical.Domain.ComponentPort
            {
                PortId = $"{id}:p1",
                Name = "P1",
                Pins =
                {
                    new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = firstPinId, PinNumber = "1", Layer = firstLayer },
                    new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = secondPinId, PinNumber = "2", Layer = secondLayer }
                }
            }
        }
    };
}
