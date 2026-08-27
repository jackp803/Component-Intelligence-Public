using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingGraphV2AdapterTests
{
    private static readonly string[] PinnedV2RequiredArrays =
    [
        "pageIntents",
        "powerFlowOrientation",
        "cableFamilies",
        "cableInstances",
        "terminalContinuities",
        "crossPageContinuations",
        "deviceRoles",
        "heavyDutyConnectors"
    ];

    [Fact]
    public void Serialization_UsesPinnedV2Schema_AndAlwaysEmitsEightEvidenceArrays()
    {
        var project = DirectWireProject();
        var evidence = ConfirmedRoleEvidence(project);

        var result = new AutocadStagingGraphV2Builder().Prepare(
            project,
            Bindings("left:pin", "right:pin"),
            evidence);

        Assert.True(result.Preflight.CanStageForReview);
        var graph = Assert.IsType<AutocadStagingGraphV2Contract>(result.Graph);
        var json = JsonSerializer.Serialize(graph);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("lrdu-staging-route.v2", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("lrdu-staging-route.v1", root.GetProperty("sourceGraphSchemaVersion").GetString());
        foreach (var propertyName in PinnedV2RequiredArrays)
        {
            Assert.True(root.TryGetProperty(propertyName, out var value), propertyName);
            Assert.Equal(JsonValueKind.Array, value.ValueKind);
        }

        Assert.Equal("Missing", root.GetProperty("wireLayerPolicy").GetProperty("approvalStatus").GetString());
        Assert.Equal("ValidateOnly", root.GetProperty("exportMode").GetString());
        Assert.Equal("NotRun", root.GetProperty("writerInterface").GetProperty("execution").GetString());
    }

    [Fact]
    public void ExistingExplicitEvidence_IsPreservedWithoutInventingPlannerOwnedPageSemantics()
    {
        var project = DirectWireProject();
        var evidence = ConfirmedRoleEvidence(project) with
        {
            PageArchetypeHint = new AutocadPageArchetypeHint
            {
                Archetype = DrawingPageArchetype.DeviceLoop,
                Status = DrawingEvidenceStatus.Confirmed,
                EvidenceSource = "engineer-page-hint"
            },
            PowerFlowOrientations =
            [
                new AutocadPowerFlowOrientationEvidence
                {
                    NetIdentity = "net-control",
                    Orientation = PowerFlowOrientation.LeftToRight,
                    SourceEndpointId = "left:pin",
                    DestinationEndpointId = "right:pin",
                    Status = DrawingEvidenceStatus.Confirmed,
                    EvidenceSource = "engineer-orientation"
                }
            ],
            CrossPageContinuations =
            [
                new AutocadCrossPageContinuationEvidence
                {
                    PairIdentity = "pair-1",
                    SourceEndpointId = "left:pin",
                    DestinationEndpointId = "right:pin",
                    SourcePageId = "P-01",
                    DestinationPageId = "P-02",
                    Status = DrawingEvidenceStatus.Confirmed,
                    EvidenceSource = "engineer-cross-page"
                }
            ]
        };

        var graph = Assert.IsType<AutocadStagingGraphV2Contract>(
            new AutocadStagingGraphV2Builder().Prepare(
                project,
                Bindings("left:pin", "right:pin"),
                evidence).Graph);

        var pageIntent = Assert.Single(graph.PageIntents);
        Assert.Equal("DeviceLoop", pageIntent.PageArchetypeHint);
        Assert.Empty(pageIntent.PageId);
        Assert.Empty(pageIntent.DrawingRole);
        Assert.Equal("PAGE_ID_AND_MEMBER_ASSIGNMENT_EVIDENCE_REQUIRED", pageIntent.BlockingReason);

        var power = Assert.Single(graph.PowerFlowOrientation);
        Assert.Equal("net-control", power.NetIdentity);
        Assert.Equal(PowerFlowOrientation.LeftToRight, power.SourceOrientation);
        Assert.Equal("Unknown", power.Orientation);
        Assert.Empty(power.PageId);
        Assert.Equal("PAGE_LEVEL_POWER_SOURCE_TRUNK_EVIDENCE_REQUIRED", power.BlockingReason);

        var continuation = Assert.Single(graph.CrossPageContinuations);
        Assert.Equal("pair-1", continuation.PairIdentity);
        Assert.Equal("segment:wire-1", continuation.SegmentId);
        Assert.Equal("node:left:pin", continuation.SourceNodeId);
        Assert.Equal("node:right:pin", continuation.DestinationNodeId);

        Assert.Equal(2, graph.DeviceRoles.Count);
        Assert.All(graph.DeviceRoles, item => Assert.Equal("SensorOrControlDevice", item.DeviceRole));
        Assert.Empty(graph.HeavyDutyConnectors);
    }

    [Fact]
    public void EquivalentInputPermutations_ProduceIdenticalLogicalV2Contract()
    {
        var firstProject = TwoNetProject(reverse: false);
        var secondProject = TwoNetProject(reverse: true);
        var firstEvidence = ConfirmedRoleEvidence(firstProject);
        var secondEvidence = ConfirmedRoleEvidence(secondProject) with
        {
            ComponentRoles = ConfirmedRoleEvidence(secondProject).ComponentRoles.Reverse().ToArray()
        };

        var first = Assert.IsType<AutocadStagingGraphV2Contract>(
            new AutocadStagingGraphV2Builder().Prepare(
                firstProject,
                Bindings("a:pin", "b:pin", "c:pin", "d:pin"),
                firstEvidence).Graph);
        var second = Assert.IsType<AutocadStagingGraphV2Contract>(
            new AutocadStagingGraphV2Builder().Prepare(
                secondProject,
                Bindings("d:pin", "c:pin", "b:pin", "a:pin"),
                secondEvidence).Graph);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData("lrdu-staging-route.v1")]
    [InlineData("lrdu-staging-route.v0")]
    [InlineData("")]
    [InlineData(null)]
    public void LegacyOrUnsupportedSchema_IsRejectedInsteadOfSilentlyTreatedAsV2(string? schemaVersion)
    {
        Assert.Throws<NotSupportedException>(() =>
            AutocadStagingGraphV2Contract.EnsureSupportedSchema(schemaVersion));
    }

    [Fact]
    public void ExactPinnedV2Schema_IsAcceptedByCompatibilityGuard()
    {
        AutocadStagingGraphV2Contract.EnsureSupportedSchema("lrdu-staging-route.v2");
    }

    private static ElectricalProject DirectWireProject()
    {
        var project = new ElectricalProject { ProjectId = "2fe3fb260c7d4c0eb3f5661e4b112a01" };
        project.Nets.Add(new NetDefinition { NetId = "net-control", Label = "CONTROL" });
        project.Components.Add(Component("left", "left:port", "left:pin"));
        project.Components.Add(Component("right", "right:port", "right:pin"));
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-1",
            FromEndpointId = "left:pin",
            ToEndpointId = "right:pin",
            NetId = "net-control",
            Kind = ConnectionKind.Wire
        });
        return project;
    }

    private static ElectricalProject TwoNetProject(bool reverse)
    {
        var project = new ElectricalProject { ProjectId = "permutation-project" };
        var nets = new[]
        {
            new NetDefinition { NetId = "net-a", Label = "NET-A" },
            new NetDefinition { NetId = "net-b", Label = "NET-B" }
        };
        var components = new[]
        {
            Component("a", "a:port", "a:pin"),
            Component("b", "b:port", "b:pin"),
            Component("c", "c:port", "c:pin"),
            Component("d", "d:port", "d:pin")
        };
        var connections = new[]
        {
            new ElectricalConnection
            {
                ConnectionId = "wire-a",
                FromEndpointId = "a:pin",
                ToEndpointId = "b:pin",
                NetId = "net-a",
                Kind = ConnectionKind.Wire
            },
            new ElectricalConnection
            {
                ConnectionId = "wire-b",
                FromEndpointId = "c:pin",
                ToEndpointId = "d:pin",
                NetId = "net-b",
                Kind = ConnectionKind.Wire
            }
        };

        foreach (var net in reverse ? nets.AsEnumerable().Reverse() : nets) project.Nets.Add(net);
        foreach (var component in reverse ? components.AsEnumerable().Reverse() : components) project.Components.Add(component);
        foreach (var connection in reverse ? connections.AsEnumerable().Reverse() : connections) project.Connections.Add(connection);
        return project;
    }

    private static ComponentInstance Component(string id, string portId, string pinId) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"def:{id}",
        TypeKey = "TEST",
        Ports =
        {
            new ComponentPort
            {
                PortId = portId,
                Name = id,
                Pins =
                {
                    new ComponentPin
                    {
                        PinId = pinId,
                        PinNumber = "1",
                        Function = "CONTROL",
                        Status = PinStatus.Normal
                    }
                }
            }
        }
    };

    private static AutocadEngineeringDrawingEvidence ConfirmedRoleEvidence(ElectricalProject project) => new()
    {
        ComponentRoles = project.Components.Select(component => new AutocadComponentDrawingRoleEvidence
        {
            ComponentInstanceId = component.ComponentInstanceId,
            Role = ComponentDrawingRole.SensorOrControlDevice,
            Status = DrawingEvidenceStatus.Confirmed,
            EvidenceSource = "test-engineer"
        }).ToArray()
    };

    private static IReadOnlyList<AutocadConnectionPointBinding> Bindings(params string[] endpointIds) => endpointIds
        .Select(endpointId => new AutocadConnectionPointBinding
        {
            EndpointId = endpointId,
            SymbolKey = $"SYM:{endpointId}",
            ConnectionPointId = $"XTERM:{endpointId}"
        }).ToArray();
}
