using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadStagingGraphBuilderTests
{
    [Fact]
    public void ConfirmedPinToPinWire_BuildsUniqueNodesAndSegmentFromAuditedBindings()
    {
        var project = DirectWireProject();

        var result = Prepare(project, Bindings("left:pin", "right:pin"));

        Assert.True(result.Preflight.CanStageForReview);
        var graph = Assert.IsType<AutocadStagingGraphContract>(result.Graph);
        var route = Assert.Single(graph.Routes);
        Assert.Equal("net-control", route.NetIdentity);
        Assert.Equal("CONTROL", route.VisibleLabel);
        Assert.Equal(2, route.Nodes.Select(node => node.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var segment = Assert.Single(route.Segments);
        Assert.Equal("segment:wire-1", segment.SegmentId);
        Assert.Equal("InternalWire", segment.Kind);
        Assert.All(route.Nodes, node => Assert.NotNull(node.ConnectionPoint));
    }

    [Fact]
    public void MissingAuditedBinding_BlocksGraphInsteadOfUsingAPlaceholder()
    {
        var project = DirectWireProject();
        var result = Prepare(project, Bindings("left:pin"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.SymbolConnectionPointMissing && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void PortLevelEndpoint_BlocksInsteadOfAutomaticallySelectingAPin()
    {
        var project = DirectWireProject();
        project.Connections.Clear();
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "port-wire",
            FromEndpointId = "left:port",
            ToEndpointId = "right:pin",
            NetId = "net-control",
            Kind = ConnectionKind.Wire
        });

        var result = Prepare(project, Bindings("right:pin"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.PortLevelEndpoint && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void CollisionSafeVisibleLabels_ArePreservedInGraphRoutes()
    {
        var project = DirectWireProject();
        project.Nets.Add(new NetDefinition { NetId = "net-other", Label = "CONTROL" });
        project.Components.Add(Component("third", "third:port", "third:pin"));
        project.Components.Add(Component("fourth", "fourth:port", "fourth:pin"));
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-2",
            FromEndpointId = "third:pin",
            ToEndpointId = "fourth:pin",
            NetId = "net-other",
            Kind = ConnectionKind.Wire
        });

        var result = Prepare(project,
            Bindings("left:pin", "right:pin", "third:pin", "fourth:pin"));

        var graph = Assert.IsType<AutocadStagingGraphContract>(result.Graph);
        Assert.Equal(new[] { "CONTROL", "CONTROL-02" }, graph.Routes.Select(route => route.VisibleLabel).OrderBy(label => label, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, graph.Routes.Select(route => route.NetIdentity).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CableWithoutExactCoreEndpointEvidence_BlocksGraph()
    {
        var project = DirectWireProject();
        project.Connections[0].Kind = ConnectionKind.Cable;
        project.Connections[0].CableInstanceId = "cable-1";
        project.Cables.Add(new CableInstance { CableInstanceId = "cable-1", CableDefinitionId = "UNRESOLVED-CABLE" });

        var result = Prepare(project, Bindings("left:pin", "right:pin"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.UnprovenPhysicalSegment && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void UnprovenCableCore_AndMissingSymbolBinding_AreBothReportedAsErrors()
    {
        var project = DirectWireProject();
        project.Connections[0].Kind = ConnectionKind.Cable;
        project.Connections[0].CableInstanceId = "cable-1";
        project.Cables.Add(new CableInstance { CableInstanceId = "cable-1", CableDefinitionId = "UNRESOLVED-CABLE" });

        var result = Prepare(project, Bindings("left:pin"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.UnprovenPhysicalSegment && issue.Severity == AutocadExportPreflightSeverity.Error);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.SymbolConnectionPointMissing && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void UnresolvedEndpoint_RemainsHardErrorAndDoesNotProduceGraph()
    {
        var project = DirectWireProject();
        project.Connections.Clear();
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-missing-endpoint",
            FromEndpointId = "left:pin",
            ToEndpointId = "missing:pin",
            NetId = "net-control",
            Kind = ConnectionKind.Wire
        });

        var result = Prepare(project, Bindings("left:pin"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.UnresolvedPinEndpoint && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void DuplicateConnectionId_RemainsHardErrorAndDoesNotProduceGraph()
    {
        var project = DirectWireProject();
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-1",
            FromEndpointId = "left:pin",
            ToEndpointId = "right:pin",
            NetId = "net-control",
            Kind = ConnectionKind.Wire
        });

        var result = Prepare(project, Bindings("left:pin", "right:pin"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.DuplicateEdgeId && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void ProvenCableWithProcurementTbd_CarriesInterventionAndKeepsConfirmedTopology()
    {
        var project = DirectWireProject();
        project.Connections[0].Kind = ConnectionKind.Cable;
        project.Connections[0].CableInstanceId = "cable-1";
        project.Connections[0].CableCoreId = "core-1";
        project.Cables.Add(new CableInstance
        {
            CableInstanceId = "cable-1",
            CableDefinitionId = "UNRESOLVED-CABLE",
            ProvidedLengthMm = 1000,
            CoreAssignments =
            {
                new CoreAssignment
                {
                    CoreId = "core-1",
                    Status = "ASSIGNED",
                    FromEndpointId = "left:pin",
                    ToEndpointId = "right:pin"
                }
            }
        });

        var result = Prepare(project, Bindings("left:pin", "right:pin"));

        Assert.True(result.Preflight.CanStageForReview);
        var graph = Assert.IsType<AutocadStagingGraphContract>(result.Graph);
        var segment = Assert.Single(Assert.Single(graph.Routes).Segments);
        Assert.Equal("TBD", segment.ProcurementStatus);
        Assert.NotNull(segment.InterventionId);
        Assert.Contains(graph.Interventions, item => item.InterventionId == segment.InterventionId);
    }

    [Fact]
    public void OutOfScopeBoundary_StopsAtFieldBoundaryWithoutDownstreamWiring()
    {
        var project = DirectWireProject();
        project.Components[1].ResponsibilityScope = ResponsibilityScope.OutOfScope;

        var result = Prepare(project, Bindings("left:pin"));

        Assert.True(result.Preflight.CanStageForReview);
        var route = Assert.Single(Assert.IsType<AutocadStagingGraphContract>(result.Graph).Routes);
        Assert.Equal("FieldBoundary", route.TopologyStatus);
        Assert.Contains(route.Nodes, node => node.Kind == "FieldBoundary");
        var confirmed = Assert.Single(route.Segments, segment => segment.TopologyStatus == "Confirmed");
        Assert.Equal("SourceArrow", confirmed.DrawingRepresentation);
        Assert.Contains("TBD", confirmed.SignalCode);
        var unknown = Assert.Single(route.Segments, segment => segment.Kind == "UnknownFieldSegment");
        Assert.Equal("NotDrawn", unknown.DrawingRepresentation);
        Assert.NotNull(unknown.InterventionId);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.PowerTeamResponsibilityBoundary);
    }

    [Fact]
    public void ExplicitShieldPath_RemainsSpecialAndTerminationTbd()
    {
        var project = DirectWireProject();
        project.Components[0].Ports[0].Pins[0].GroundReferenceType = GroundReferenceType.Shield;
        project.Components[1].Ports[0].Pins[0].GroundReferenceType = GroundReferenceType.Shield;

        var result = Prepare(project, Bindings("left:pin", "right:pin"));

        var route = Assert.Single(Assert.IsType<AutocadStagingGraphContract>(result.Graph).Routes);
        Assert.Equal("Unknown", route.Shield.Status);
        Assert.Equal("Shield", Assert.Single(route.Segments).Kind);
        Assert.Equal("NotDrawn", Assert.Single(route.Segments).DrawingRepresentation);
        Assert.Contains(result.Preflight.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.ShieldTerminationTbd);
    }

    [Fact]
    public void Serialization_UsesV1PropertyNamesAndValidateOnlyWriterGate()
    {
        var project = DirectWireProject();
        var graph = Assert.IsType<AutocadStagingGraphContract>(
            Prepare(project, Bindings("left:pin", "right:pin")).Graph);

        var json = JsonSerializer.Serialize(graph);
        Assert.DoesNotContain("null", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("lrdu-staging-route.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("ValidateOnly", root.GetProperty("exportMode").GetString());
        Assert.Equal("NotRun", root.GetProperty("writerInterface").GetProperty("execution").GetString());
        Assert.False(root.GetProperty("writerInterface").GetProperty("formalWriterAuthorized").GetBoolean());
        var route = root.GetProperty("routes")[0];
        Assert.True(route.TryGetProperty("netIdentity", out _));
        Assert.True(route.TryGetProperty("visibleLabel", out _));
        var node = route.GetProperty("nodes")[0];
        Assert.True(node.GetProperty("connectionPoint").TryGetProperty("connectionPointId", out _));
        Assert.Equal("def:left", node.GetProperty("componentDefinitionId").GetString());
        Assert.Equal("TEST", node.GetProperty("componentTypeKey").GetString());
        Assert.Equal("1", node.GetProperty("pinNumber").GetString());
        Assert.Equal("left", node.GetProperty("portName").GetString());
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
                Pins = { new ComponentPin { PinId = pinId, PinNumber = "1", Function = "CONTROL", Status = PinStatus.Normal } }
            }
        }
    };

    private static AutocadStagingGraphPreparationResult Prepare(
        ElectricalProject project,
        IReadOnlyList<AutocadConnectionPointBinding> bindings) =>
        new AutocadStagingGraphBuilder().Prepare(project, bindings, new AutocadEngineeringDrawingEvidence
        {
            ComponentRoles = project.Components.Select(component => new AutocadComponentDrawingRoleEvidence
            {
                ComponentInstanceId = component.ComponentInstanceId,
                Role = ComponentDrawingRole.SensorOrControlDevice,
                Status = DrawingEvidenceStatus.Confirmed,
                EvidenceSource = "test-engineer"
            }).ToArray()
        });

    private static IReadOnlyList<AutocadConnectionPointBinding> Bindings(params string[] endpointIds) => endpointIds
        .Select(endpointId => new AutocadConnectionPointBinding
        {
            EndpointId = endpointId,
            SymbolKey = $"SYM:{endpointId}",
            ConnectionPointId = $"XTERM:{endpointId}"
        }).ToArray();
}
