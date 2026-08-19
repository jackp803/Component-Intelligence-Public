using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyTerminalJunctionTests
{
    [Fact]
    public void InlineTerminalCanBecomeParallelBranchJunctionWithoutVisualOnlyContinuity()
    {
        var project = ProjectWithThreePorts();
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-a", ObjectKind = "COMPONENT", X = 10, Y = 20, Width = 140, Height = 76 });
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-b", ObjectKind = "COMPONENT", X = 500, Y = 20, Width = 140, Height = 76 });
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-c", ObjectKind = "COMPONENT", X = 500, Y = 220, Width = 140, Height = 76 });

        var editor = new TopologyConnectionEditor();
        var original = editor.ConnectPorts(project, "cmp-a:pwr", "cmp-b:pwr", "net-24v");
        var terminal = editor.InsertInlineTerminal(project, original.ConnectionId, new InlineTerminalOptions(FunctionTag: "24V"));
        var service = new TopologyTerminalJunctionService();

        var branch = service.Connect(project, TopologyTerminalJunctionService.Selector(terminal.TerminalBlockId), "cmp-c:pwr");

        Assert.Equal(3, project.Connections.Count);
        Assert.Equal("net-24v", branch.NetId);
        var position = Assert.Single(terminal.Positions);
        var level = Assert.Single(position.Levels);
        Assert.Equal("TOPOLOGY_JUNCTION", position.TerminalType);
        Assert.Equal(3, level.ConnectionPoints.Count);
        var branchPoint = level.ConnectionPoints.Single(point => point.PhysicalSide == "BRANCH");
        Assert.Contains(level.InternalConnections, item =>
            string.Equals(item.ToConnectionPointId, branchPoint.ConnectionPointId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.FromConnectionPointId, branchPoint.ConnectionPointId, StringComparison.OrdinalIgnoreCase));
        Assert.True(
            (branch.FromEndpointId == branchPoint.ConnectionPointId && branch.ToEndpointId == "cmp-c:pwr") ||
            (branch.ToEndpointId == branchPoint.ConnectionPointId && branch.FromEndpointId == "cmp-c:pwr"));
    }

    [Fact]
    public void RepeatedBranchesAllocateSeparateTerminalConnectionPoints()
    {
        var project = ProjectWithFourPorts();
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-a", ObjectKind = "COMPONENT", X = 10, Y = 20, Width = 140, Height = 76 });
        project.TopologyPlacements.Add(new TopologyPlacement { ObjectId = "cmp-b", ObjectKind = "COMPONENT", X = 500, Y = 20, Width = 140, Height = 76 });

        var editor = new TopologyConnectionEditor();
        var original = editor.ConnectPorts(project, "cmp-a:pwr", "cmp-b:pwr", "net-24v");
        var terminal = editor.InsertInlineTerminal(project, original.ConnectionId, new InlineTerminalOptions(FunctionTag: "24V"));
        var service = new TopologyTerminalJunctionService();
        var selector = TopologyTerminalJunctionService.Selector(terminal.TerminalBlockId);

        service.Connect(project, selector, "cmp-c:pwr");
        service.Connect(project, selector, "cmp-d:pwr");

        var level = terminal.Positions.Single().Levels.Single();
        Assert.Equal(4, level.ConnectionPoints.Count);
        Assert.Equal(2, level.ConnectionPoints.Count(point => point.PhysicalSide == "BRANCH"));
        Assert.Equal(4, project.Connections.Count);
    }

    [Fact]
    public void TerminalJunctionCanConnectToExactComponentPin()
    {
        var project = ProjectWithThreePorts();
        var targetPort = project.Components.Single(component => component.ComponentInstanceId == "cmp-c").Ports.Single();
        targetPort.Pins.Add(new ComponentPin
        {
            PinId = "cmp-c:pwr:pin:plus",
            PinNumber = "+",
            PinName = "+24V",
            Layer = ElectricalLayer.Power,
            Status = PinStatus.Normal,
            Power = new PowerCapability { Polarity = Polarity.Positive }
        });

        var editor = new TopologyConnectionEditor();
        var original = editor.ConnectPorts(project, "cmp-a:pwr", "cmp-b:pwr", "net-24v");
        var terminal = editor.InsertInlineTerminal(project, original.ConnectionId, new InlineTerminalOptions(FunctionTag: "24V"));
        var service = new TopologyTerminalJunctionService();

        var branch = service.Connect(
            project,
            TopologyTerminalJunctionService.Selector(terminal.TerminalBlockId),
            "cmp-c:pwr:pin:plus");

        Assert.Equal("net-24v", branch.NetId);
        Assert.True(
            string.Equals(branch.FromEndpointId, "cmp-c:pwr:pin:plus", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(branch.ToEndpointId, "cmp-c:pwr:pin:plus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReconnectEndpointPreservesCableSettingsAndClearsOldRoute()
    {
        var project = ProjectWithThreePorts();
        var editor = new TopologyConnectionEditor();
        var connection = editor.ConnectPorts(project, "cmp-a:pwr", "cmp-b:pwr", "net-24v");
        connection.CableInstanceId = "cable-1";
        connection.CableCoreId = "core-1";
        connection.ConductorAreaMm2 = 0.75;
        project.Cables.Add(new CableInstance
        {
            CableInstanceId = "cable-1",
            CableDefinitionId = "IFM-EVC014",
            DisplayName = "IFM EVC014",
            CoreAssignments =
            {
                new CoreAssignment
                {
                    CoreId = "core-1",
                    FromEndpointId = "cmp-a:pwr",
                    ToEndpointId = "cmp-b:pwr",
                    Status = "ASSIGNED"
                }
            }
        });
        project.TopologyRoutes.Add(new TopologyRouteGeometry
        {
            ConnectionId = connection.ConnectionId,
            ManualWaypointX = 123,
            ManualWaypointY = 456
        });

        var updated = new TopologyTerminalJunctionService().ReconnectEndpoint(
            project,
            connection.ConnectionId,
            reconnectFrom: false,
            "cmp-c:pwr");

        Assert.Equal(connection.ConnectionId, updated.ConnectionId);
        Assert.Equal("cmp-a:pwr", updated.FromEndpointId);
        Assert.Equal("cmp-c:pwr", updated.ToEndpointId);
        Assert.Equal("cable-1", updated.CableInstanceId);
        Assert.Equal("core-1", updated.CableCoreId);
        Assert.Equal(0.75, updated.ConductorAreaMm2);
        Assert.Empty(project.TopologyRoutes);
        var assignment = Assert.Single(project.Cables.Single().CoreAssignments);
        Assert.Equal("cmp-c:pwr", assignment.ToEndpointId);
    }

    private static ElectricalProject ProjectWithThreePorts()
    {
        var project = new ElectricalProject { ProjectId = "junction-test" };
        AddPowerPort(project, "cmp-a");
        AddPowerPort(project, "cmp-b");
        AddPowerPort(project, "cmp-c");
        return project;
    }

    private static ElectricalProject ProjectWithFourPorts()
    {
        var project = ProjectWithThreePorts();
        AddPowerPort(project, "cmp-d");
        return project;
    }

    private static void AddPowerPort(ElectricalProject project, string componentId)
    {
        project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = componentId,
            ComponentDefinitionId = "test-power-device",
            TypeKey = "DEVICE",
            ReferenceDesignator = componentId.ToUpperInvariant(),
            Ports =
            {
                new ComponentPort
                {
                    PortId = $"{componentId}:pwr",
                    Name = "24V",
                    MaxConnections = 1
                }
            }
        });
    }
}
