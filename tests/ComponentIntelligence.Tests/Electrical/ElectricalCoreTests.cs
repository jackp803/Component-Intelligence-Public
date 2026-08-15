using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;
using ComponentIntelligence.Electrical.Naming;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Validation;
using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalCoreTests
{
    [Fact]
    public void TerminalEndpoints_DoNotBecomeCommonUntilAJumperExists()
    {
        var project = NewProject();
        var block = new TerminalBlock
        {
            TerminalBlockId = "tb-1",
            ReferenceDesignator = "TB1",
            Positions =
            {
                NewTerminalPosition("p1", "TB1:1", "cp1"),
                NewTerminalPosition("p2", "TB1:2", "cp2")
            }
        };
        project.TerminalBlocks.Add(block);
        var engine = new TerminalConnectivityEngine();

        Assert.False(engine.AreConnected(project, "cp1", "cp2"));

        block.Jumpers.Add(new ShortingJumper
        {
            JumperId = "jmp-1",
            ConnectionPointIds = { "cp1", "cp2" }
        });

        Assert.True(engine.AreConnected(project, "cp1", "cp2"));
    }

    [Fact]
    public void NamingEngine_DoesNotReuseDeletedNumbersImplicitly()
    {
        var next = NamingEngine.NextReference(new[] { "S01", "S03" }, "S", 2);
        Assert.Equal("S04", next);
    }

    [Fact]
    public void SameRj45Connector_DoesNotMakeEthernetAndRs485Compatible()
    {
        var project = NewProject();
        var first = NewComponent("c1", "DEV", "D01");
        var firstPort = NewPort("p1", "RJ45", "RS485");
        firstPort.Pins.Add(NewPin("pin-rs", "1", "RS485"));
        first.Ports.Add(firstPort);

        var second = NewComponent("c2", "DEV", "D02");
        var secondPort = NewPort("p2", "RJ45", "Ethernet");
        secondPort.Pins.Add(NewPin("pin-eth", "1", "Ethernet"));
        second.Ports.Add(secondPort);
        project.Components.AddRange(new[] { first, second });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "conn-1",
            FromEndpointId = "pin-rs",
            ToEndpointId = "pin-eth",
            Kind = ConnectionKind.DirectMating
        });

        var report = new ElectricalProjectValidator().Validate(project);

        Assert.Equal(DrawingReadiness.Blocked, report.DrawingReadiness);
        Assert.Contains(report.Results, result => result.RuleId == "RULE-PROTOCOL-001" && result.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public void UnconnectedRequiredPin_RequiresReviewButDoesNotBlockDrawing()
    {
        var project = NewProject();
        var component = NewComponent("c1", "SENSOR", "S01");
        var port = NewPort("p1", "M12", null);
        port.Pins.Add(new ComponentPin
        {
            PinId = "pin-1",
            PinNumber = "1",
            IsRequired = true,
            Status = PinStatus.Normal,
            Layer = ElectricalLayer.Power
        });
        component.Ports.Add(port);
        project.Components.Add(component);

        var report = new ElectricalProjectValidator().Validate(project);

        Assert.Equal(DrawingReadiness.ReviewRequired, report.DrawingReadiness);
        Assert.DoesNotContain(report.Results, result => result.Severity == ValidationSeverity.Block);
        Assert.Contains(report.Results, result => result.RuleId == "RULE-PREEXPORT-UNCONNECTED");
    }

    [Fact]
    public void RequiredPePin_IsErrorAndRequiresPreExportReview()
    {
        var project = NewProject();
        var component = NewComponent("c1", "DEVICE", "D01");
        var port = NewPort("p1", "TERMINAL", null);
        port.Pins.Add(new ComponentPin
        {
            PinId = "pe-1",
            PinNumber = "PE",
            IsRequired = true,
            GroundReferenceType = GroundReferenceType.ProtectiveEarth,
            Layer = ElectricalLayer.Grounding,
            Status = PinStatus.Normal
        });
        component.Ports.Add(port);
        project.Components.Add(component);

        var report = new ElectricalProjectValidator().Validate(project);

        var peIssue = Assert.Single(report.Results.Where(result => result.RuleId == "RULE-GND-VAL-005"));
        Assert.Equal(ValidationSeverity.Error, peIssue.Severity);
        Assert.True(peIssue.RequiresPreExportReview);
        Assert.Equal(DrawingReadiness.ReviewRequired, report.DrawingReadiness);
    }

    [Fact]
    public void Rs485Pair_WithOnlyOneSideConnected_IsReviewRequired()
    {
        var project = NewProject();
        var device = NewComponent("c1", "RS485_DEVICE", "D01");
        var port = NewPort("p1", "RJ45", "RS485");
        port.Pins.Add(new ComponentPin { PinId = "a", PinNumber = "1", Protocol = "RS485", DifferentialRole = DifferentialRole.Positive });
        port.Pins.Add(new ComponentPin { PinId = "b", PinNumber = "2", Protocol = "RS485", DifferentialRole = DifferentialRole.Negative });
        device.Ports.Add(port);
        project.Components.Add(device);

        var other = NewComponent("c2", "OTHER", "D02");
        var otherPort = NewPort("p2", "RJ45", "RS485");
        otherPort.Pins.Add(new ComponentPin { PinId = "a2", PinNumber = "1", Protocol = "RS485", DifferentialRole = DifferentialRole.Positive });
        other.Ports.Add(otherPort);
        project.Components.Add(other);
        project.Connections.Add(new ElectricalConnection { ConnectionId = "conn", FromEndpointId = "a", ToEndpointId = "a2" });

        var report = new ElectricalProjectValidator().Validate(project);

        Assert.Contains(report.Results, result => result.RuleId == "RULE-RS485-008");
        Assert.Equal(DrawingReadiness.ReviewRequired, report.DrawingReadiness);
    }

    [Fact]
    public void LayoutOutsideContainer_IsBlocked()
    {
        var project = NewProject();
        project.LayoutContainers.Add(new LayoutContainer { ContainerId = "cab", Name = "CAB-01", WidthMm = 500, HeightMm = 500 });
        var component = NewComponent("c1", "PLC", "PLC01");
        component.Footprint = new PhysicalFootprint { WidthMm = 100, HeightMm = 100, MountingType = MountingType.Backplate };
        component.Placement = new PhysicalPlacement { ParentContainerId = "cab", XMm = 450, YMm = 450 };
        project.Components.Add(component);

        var issues = new PhysicalLayoutValidator().Validate(project);

        Assert.Contains(issues, issue => issue.RuleId == "RULE-LAYOUT-001" && issue.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public void TerminalConnectionPoint_EnforcesMaxConductors()
    {
        var project = NewProject();
        var block = new TerminalBlock
        {
            TerminalBlockId = "tb-1",
            ReferenceDesignator = "TB1",
            Positions = { NewTerminalPosition("p1", "TB1:1", "cp1") }
        };
        project.TerminalBlocks.Add(block);

        var device = NewComponent("c1", "DEVICE", "D01");
        var port = NewPort("port", "WIRE", null);
        port.Pins.Add(NewPin("pin1", "1", null));
        port.Pins.Add(NewPin("pin2", "2", null));
        device.Ports.Add(port);
        project.Components.Add(device);
        project.Connections.Add(new ElectricalConnection { ConnectionId = "w1", FromEndpointId = "pin1", ToEndpointId = "cp1" });
        project.Connections.Add(new ElectricalConnection { ConnectionId = "w2", FromEndpointId = "pin2", ToEndpointId = "cp1" });

        var report = new ElectricalProjectValidator().Validate(project);

        Assert.Contains(report.Results, result => result.RuleId == "RULE-TERM-006" && result.Severity == ValidationSeverity.Block);
    }

    [Fact]
    public async Task ElectricalProjectRepository_RoundTripsSnapshotInExistingSqliteInfrastructure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"component-intelligence-electrical-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            var project = NewProject();
            project.Name = "Electrical test";
            project.Nets.Add(new NetDefinition { NetId = "net-1", Label = "54V+", Layer = ElectricalLayer.Power });

            await repository.SaveAsync(project);
            var loaded = await repository.GetAsync(project.ProjectId);

            Assert.NotNull(loaded);
            Assert.Equal("Electrical test", loaded!.Name);
            Assert.Single(loaded.Nets);
            Assert.Equal("54V+", loaded.Nets[0].Label);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static ElectricalProject NewProject() => new() { ProjectId = Guid.NewGuid().ToString("N") };

    private static ComponentInstance NewComponent(string id, string typeKey, string reference) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"definition-{id}",
        TypeKey = typeKey,
        ReferenceDesignator = reference
    };

    private static ComponentPort NewPort(string id, string connectorFamily, string? protocol) => new()
    {
        PortId = id,
        Name = id,
        Protocol = protocol,
        Connector = new ConnectorDefinition
        {
            ConnectorId = $"connector-{id}",
            Family = connectorFamily,
            Gender = ConnectorGender.Genderless
        }
    };

    private static ComponentPin NewPin(string id, string number, string? protocol) => new()
    {
        PinId = id,
        PinNumber = number,
        Protocol = protocol,
        Status = PinStatus.Normal
    };

    private static TerminalPosition NewTerminalPosition(string id, string label, string pointId) => new()
    {
        TerminalPositionId = id,
        PositionLabel = label,
        Levels =
        {
            new TerminalLevel
            {
                LevelId = $"level-{id}",
                LevelName = "L1",
                ConnectionPoints =
                {
                    new TerminalConnectionPoint
                    {
                        ConnectionPointId = pointId,
                        Type = ConnectionPointType.ConductorEntry,
                        MaxConductors = 1,
                        MinWireAreaMm2 = 0.25,
                        MaxWireAreaMm2 = 2.5
                    }
                }
            }
        }
    };
}
