using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EngineeringDrawingContractTests
{
    [Fact]
    public void TypeKeyDoesNotConfirmDrawingRoleAndUnknownRoleBlocksGraph()
    {
        var project = DirectProject(ElectricalLayer.Digital);
        project.Components[0].TypeKey = "POWER_SOURCE";

        var result = new AutocadStagingGraphBuilder().Prepare(project, Bindings("left:1", "right:1"));

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue =>
            issue.Code == AutocadExportPreflightIssueCode.UnknownComponentDrawingRole &&
            issue.Severity == AutocadExportPreflightSeverity.Error &&
            issue.SourceObjectIds.Contains("left"));
    }

    [Fact]
    public void InferredPowerOrientationIsNotExportedAsConfirmed()
    {
        var project = DirectProject(ElectricalLayer.Power);
        var evidence = EvidenceWithConfirmedRoles(project) with
        {
            PowerFlowOrientations =
            [
                new AutocadPowerFlowOrientationEvidence
                {
                    NetIdentity = "net-1",
                    Orientation = PowerFlowOrientation.LeftToRight,
                    SourceEndpointId = "left:1",
                    DestinationEndpointId = "right:1",
                    Status = DrawingEvidenceStatus.Inferred,
                    EvidenceSource = "visual-order"
                }
            ]
        };

        var result = new AutocadStagingGraphBuilder().Prepare(
            project, Bindings("left:1", "right:1"), evidence);

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue =>
            issue.Code == AutocadExportPreflightIssueCode.PowerFlowOrientationUnknown &&
            issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void ExplicitTerminalContinuityIsPreservedAsConfirmedEvidence()
    {
        var project = new ElectricalProject { ProjectId = "terminal-project" };
        project.TerminalBlocks.Add(new TerminalBlock
        {
            TerminalBlockId = "tb-1",
            ReferenceDesignator = "TB1",
            Positions =
            {
                new TerminalPosition
                {
                    TerminalPositionId = "tb-1:01",
                    PositionLabel = "01",
                    Levels =
                    {
                        new TerminalLevel
                        {
                            LevelId = "level-1",
                            LevelName = "L1",
                            ConnectionPoints =
                            {
                                new TerminalConnectionPoint { ConnectionPointId = "tb-1:01:a", Type = ConnectionPointType.ConductorEntry },
                                new TerminalConnectionPoint { ConnectionPointId = "tb-1:01:b", Type = ConnectionPointType.ConductorEntry }
                            },
                            InternalConnections =
                            {
                                new InternalTerminalConnection
                                {
                                    FromConnectionPointId = "tb-1:01:a",
                                    ToConnectionPointId = "tb-1:01:b"
                                }
                            }
                        }
                    }
                }
            }
        });

        var result = new AutocadStagingGraphBuilder().Prepare(project, []);

        var continuity = Assert.Single(Assert.IsType<AutocadStagingGraphContract>(result.Graph).TerminalContinuities);
        Assert.Equal(("tb-1:01:a", "tb-1:01:b"),
            (continuity.FromConnectionPointId, continuity.ToConnectionPointId));
        Assert.Equal(DrawingEvidenceStatus.Confirmed, continuity.EvidenceStatus);
    }

    [Fact]
    public void CableFamilyUsesInterfacesAndPinCoreMapButNotInstanceLengthOrOverrides()
    {
        var project = CableFamilyProject();
        var evidence = EvidenceWithConfirmedRoles(project) with
        {
            CableInstanceOverrides =
            [
                new AutocadCableInstanceOverride { CableInstanceId = "cable-1", SpecificationOverride = "SPEC-A", CatalogOverride = "CAT-A" },
                new AutocadCableInstanceOverride { CableInstanceId = "cable-2", SpecificationOverride = "SPEC-B", CatalogOverride = "CAT-B" }
            ]
        };

        var result = new AutocadStagingGraphBuilder().Prepare(project, [], evidence);

        var graph = Assert.IsType<AutocadStagingGraphContract>(result.Graph);
        Assert.Equal(2, graph.CableFamilies.Count);
        var cable1 = Assert.Single(graph.CableInstances, item => item.CableInstanceId == "cable-1");
        var cable2 = Assert.Single(graph.CableInstances, item => item.CableInstanceId == "cable-2");
        var cable3 = Assert.Single(graph.CableInstances, item => item.CableInstanceId == "cable-3");
        Assert.Equal(cable1.CableFamilyId, cable2.CableFamilyId);
        Assert.NotEqual(cable1.CableFamilyId, cable3.CableFamilyId);
        Assert.NotEqual(cable1.ProvidedLengthMm, cable2.ProvidedLengthMm);
        Assert.NotEqual(cable1.SpecificationOverride, cable2.SpecificationOverride);
        Assert.NotEqual(cable1.CatalogOverride, cable2.CatalogOverride);
        var family = Assert.Single(graph.CableFamilies, item => item.CableFamilyId == cable1.CableFamilyId);
        Assert.Equal(new[] { "1", "2" }, family.EndA.UsedPins);
        Assert.Equal(new[] { "3" }, family.EndA.UnusedPins);
        Assert.Equal(2, family.PinCoreMap.Count);
    }

    [Fact]
    public void PageHintAndCrossPagePairArePreservedWithoutPageGeneration()
    {
        var project = DirectProject(ElectricalLayer.Digital);
        var evidence = EvidenceWithConfirmedRoles(project) with
        {
            PageArchetypeHint = new AutocadPageArchetypeHint
            {
                Archetype = DrawingPageArchetype.Interface,
                Status = DrawingEvidenceStatus.Confirmed,
                EvidenceSource = "engineer"
            },
            CrossPageContinuations =
            [
                new AutocadCrossPageContinuationEvidence
                {
                    PairIdentity = "pair-001",
                    SourceEndpointId = "left:1",
                    DestinationEndpointId = "right:1",
                    SourcePageId = "PAGE-10",
                    DestinationPageId = "PAGE-20",
                    EvidenceSource = "engineer"
                }
            ]
        };

        var result = new AutocadStagingGraphBuilder().Prepare(
            project, Bindings("left:1", "right:1"), evidence);

        var graph = Assert.IsType<AutocadStagingGraphContract>(result.Graph);
        Assert.Equal(DrawingPageArchetype.Interface, graph.PageArchetypeHint.Archetype);
        var pair = Assert.Single(graph.CrossPageContinuations);
        Assert.Equal(("pair-001", "PAGE-10", "PAGE-20"),
            (pair.PairIdentity, pair.SourcePageId, pair.DestinationPageId));
        Assert.Single(graph.Routes);
        Assert.Single(Assert.Single(graph.Routes).Segments);
    }

    [Theory]
    [InlineData(DrawingPageArchetype.Unknown, DrawingEvidenceStatus.Confirmed)]
    [InlineData(DrawingPageArchetype.Interface, DrawingEvidenceStatus.Inferred)]
    public void InvalidPageArchetypeEvidence_BlocksGraph(
        DrawingPageArchetype archetype,
        DrawingEvidenceStatus status)
    {
        var project = DirectProject(ElectricalLayer.Digital);
        var evidence = EvidenceWithConfirmedRoles(project) with
        {
            PageArchetypeHint = new AutocadPageArchetypeHint { Archetype = archetype, Status = status }
        };

        var result = new AutocadStagingGraphBuilder().Prepare(project, Bindings("left:1", "right:1"), evidence);

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue =>
            issue.Code == AutocadExportPreflightIssueCode.InvalidPageArchetypeEvidence &&
            issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void UnconfirmedCrossPageContinuation_BlocksGraph()
    {
        var project = DirectProject(ElectricalLayer.Digital);
        var evidence = EvidenceWithConfirmedRoles(project) with
        {
            CrossPageContinuations =
            [
                new AutocadCrossPageContinuationEvidence
                {
                    PairIdentity = "pair-inferred",
                    SourceEndpointId = "left:1",
                    DestinationEndpointId = "right:1",
                    SourcePageId = "P-01",
                    DestinationPageId = "P-02",
                    Status = DrawingEvidenceStatus.Inferred
                }
            ]
        };

        var result = new AutocadStagingGraphBuilder().Prepare(project, Bindings("left:1", "right:1"), evidence);

        Assert.False(result.Preflight.CanStageForReview);
        Assert.Null(result.Graph);
        Assert.Contains(result.Preflight.Issues, issue =>
            issue.Code == AutocadExportPreflightIssueCode.InvalidCrossPageContinuation &&
            issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    private static ElectricalProject DirectProject(ElectricalLayer layer)
    {
        var project = new ElectricalProject { ProjectId = "drawing-contract" };
        project.Nets.Add(new NetDefinition { NetId = "net-1", Label = "N1", Layer = layer });
        project.Components.Add(ConnectorComponent("left", ConnectorGender.Male));
        project.Components.Add(ConnectorComponent("right", ConnectorGender.Female));
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-1",
            FromEndpointId = "left:1",
            ToEndpointId = "right:1",
            NetId = "net-1"
        });
        return project;
    }

    private static AutocadEngineeringDrawingEvidence EvidenceWithConfirmedRoles(ElectricalProject project) => new()
    {
        ComponentRoles = project.Components.Select(component => new AutocadComponentDrawingRoleEvidence
        {
            ComponentInstanceId = component.ComponentInstanceId,
            Role = ComponentDrawingRole.CableOrConnector,
            Status = DrawingEvidenceStatus.Confirmed,
            EvidenceSource = "test-engineer"
        }).ToArray()
    };

    private static ElectricalProject CableFamilyProject()
    {
        var project = new ElectricalProject { ProjectId = "cable-families" };
        foreach (var prefix in new[] { "a1", "b1", "a2", "b2", "a3", "b3" })
            project.Components.Add(ConnectorComponent(prefix, prefix.StartsWith('a') ? ConnectorGender.Male : ConnectorGender.Female));
        project.Cables.Add(Cable("cable-1", "DEF-A", 1000, "a1", "b1", crossed: true));
        project.Cables.Add(Cable("cable-2", "DEF-B", 2500, "a2", "b2", crossed: true));
        project.Cables.Add(Cable("cable-3", "DEF-C", 1000, "a3", "b3", crossed: false));
        return project;
    }

    private static CableInstance Cable(
        string id,
        string definitionId,
        double length,
        string endA,
        string endB,
        bool crossed) => new()
    {
        CableInstanceId = id,
        CableDefinitionId = definitionId,
        ProvidedLengthMm = length,
        CoreAssignments =
        {
            new CoreAssignment
            {
                CoreId = "CORE-1", Status = "ASSIGNED",
                FromEndpointId = $"{endA}:1", ToEndpointId = $"{endB}:{(crossed ? 2 : 1)}"
            },
            new CoreAssignment
            {
                CoreId = "CORE-2", Status = "ASSIGNED",
                FromEndpointId = $"{endA}:2", ToEndpointId = $"{endB}:{(crossed ? 1 : 2)}"
            }
        }
    };

    private static ComponentInstance ConnectorComponent(string id, ConnectorGender gender) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"def:{id}",
        TypeKey = "ANY_TYPE_KEY",
        Ports =
        {
            new ComponentPort
            {
                PortId = $"{id}:port",
                Name = "X1",
                Connector = new ConnectorDefinition
                {
                    ConnectorId = $"{id}:connector",
                    Family = "M12",
                    SeriesOrSize = "A-coded",
                    PinCount = 3,
                    Coding = "A",
                    Gender = gender
                },
                Pins =
                {
                    new ComponentPin { PinId = $"{id}:1", PinNumber = "1", Status = PinStatus.Normal },
                    new ComponentPin { PinId = $"{id}:2", PinNumber = "2", Status = PinStatus.Normal },
                    new ComponentPin { PinId = $"{id}:3", PinNumber = "3", Status = PinStatus.Unused }
                }
            }
        }
    };

    private static IReadOnlyList<AutocadConnectionPointBinding> Bindings(params string[] endpointIds) => endpointIds
        .Select(endpointId => new AutocadConnectionPointBinding
        {
            EndpointId = endpointId,
            SymbolKey = $"SYM:{endpointId}",
            ConnectionPointId = $"XTERM:{endpointId}"
        }).ToArray();
}
