using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadExportPreflightServiceTests
{
    [Fact]
    public void ErrorConditions_BlockStagingForReview()
    {
        var request = new AutocadExportPreflightRequest
        {
            ConfirmedEndpoints =
            {
                Endpoint("pin-a", "net-a", isResolved: false),
                Endpoint("pin-b", "net-b", hasSymbolConnectionPoint: false)
            },
            ConfirmedEdges =
            {
                new AutocadExportPreflightEdge
                {
                    EdgeId = "edge-a",
                    FromEndpointId = "pin-a",
                    ToEndpointId = "missing-pin",
                    IsContinuous = false
                }
            }
        };

        var report = new AutocadExportPreflightService().Evaluate(request);

        Assert.False(report.CanStageForReview);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.UnresolvedPinEndpoint && issue.Severity == AutocadExportPreflightSeverity.Error);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.SymbolConnectionPointMissing && issue.Severity == AutocadExportPreflightSeverity.Error);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.TopologyDiscontinuity && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void WarningsAndInfo_DoNotBlockStagingForReview()
    {
        var request = new AutocadExportPreflightRequest
        {
            OpenItems =
            {
                Item("shield-1", AutocadExportPreflightOpenItemKind.ShieldTerminationTbd),
                Item("cable-1", AutocadExportPreflightOpenItemKind.CableLengthTbd),
                Item("power-boundary", AutocadExportPreflightOpenItemKind.PowerTeamResponsibilityBoundary),
                Item("procurement-1", AutocadExportPreflightOpenItemKind.ProcurementTbd),
                Item("bom-1", AutocadExportPreflightOpenItemKind.BomTbd),
                Item("layout-1", AutocadExportPreflightOpenItemKind.LayoutTbd)
            }
        };

        var report = new AutocadExportPreflightService().Evaluate(request);

        Assert.True(report.CanStageForReview);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.ShieldTerminationTbd && issue.Severity == AutocadExportPreflightSeverity.Warning);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.CableLengthTbd && issue.Severity == AutocadExportPreflightSeverity.Warning);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.PowerTeamResponsibilityBoundary && issue.Severity == AutocadExportPreflightSeverity.Warning);
        Assert.All(report.Issues.Where(issue => issue.Code is AutocadExportPreflightIssueCode.ProcurementTbd or AutocadExportPreflightIssueCode.BomTbd or AutocadExportPreflightIssueCode.LayoutTbd), issue => Assert.Equal(AutocadExportPreflightSeverity.Info, issue.Severity));
    }

    [Fact]
    public void KnownFieldBoundary_CanWarnWithoutPromotingItToAPinOrSymbolConnectionPoint()
    {
        var request = new AutocadExportPreflightRequest
        {
            ConfirmedEndpoints =
            {
                new AutocadExportPreflightEndpoint
                {
                    EndpointId = "known-port",
                    MachineNetIdentity = "machine-net",
                    IsResolved = false,
                    HasSymbolConnectionPoint = false,
                    AllowsFieldBoundary = true
                }
            },
            AdditionalIssues =
            {
                new AutocadExportPreflightIssue
                {
                    Code = AutocadExportPreflightIssueCode.PortLevelEndpoint,
                    Severity = AutocadExportPreflightSeverity.Warning,
                    Message = "No pin was selected.",
                    SourceObjectIds = ["known-port"]
                }
            }
        };

        var report = new AutocadExportPreflightService().Evaluate(request);

        Assert.True(report.CanStageForReview);
        Assert.DoesNotContain(report.Issues, issue => issue.Code is AutocadExportPreflightIssueCode.UnresolvedPinEndpoint or AutocadExportPreflightIssueCode.SymbolConnectionPointMissing);
    }

    [Fact]
    public void ConfirmedTopologyWithoutAuditedSymbolConnectionPoint_BlocksEvenWhenMarkedUndrawn()
    {
        var request = new AutocadExportPreflightRequest
        {
            ConfirmedEndpoints =
            {
                new AutocadExportPreflightEndpoint
                {
                    EndpointId = "confirmed-pin",
                    MachineNetIdentity = "machine-net",
                    IsResolved = true,
                    HasSymbolConnectionPoint = false,
                    AllowsUndrawnConfirmedTopology = true
                }
            },
        };

        var report = new AutocadExportPreflightService().Evaluate(request);

        Assert.False(report.CanStageForReview);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.SymbolConnectionPointMissing && issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void VisibleLabel_PreservesMachineIdentityAndPrefersTopologySignalThenPotential()
    {
        var labels = AutocadExportPreflightService.ResolveVisibleLabels(new[]
        {
            Endpoint("machine-net-1", "machine-net-1", topologySignal: "ESTOP"),
            Endpoint("machine-net-1-potential", "machine-net-1", topologyPotential: "+24V"),
            Endpoint("machine-net-2", "machine-net-2", topologyPotential: "+24V")
        });

        Assert.Collection(labels,
            label =>
            {
                Assert.Equal("machine-net-1", label.MachineNetIdentity);
                Assert.Equal("ESTOP", label.VisibleLabel);
            },
            label =>
            {
                Assert.Equal("machine-net-2", label.MachineNetIdentity);
                Assert.Equal("+24V", label.VisibleLabel);
            });
    }

    [Fact]
    public void VisibleLabel_UsesDeterministicWxxFallbackWhenTopologyHasNoLabel()
    {
        var labels = AutocadExportPreflightService.ResolveVisibleLabels(new[]
        {
            Endpoint("endpoint-b", "machine-net-b"),
            Endpoint("endpoint-a", "machine-net-a")
        });

        Assert.Collection(labels,
            label => Assert.Equal(("machine-net-a", "W01"), (label.MachineNetIdentity, label.VisibleLabel)),
            label => Assert.Equal(("machine-net-b", "W02"), (label.MachineNetIdentity, label.VisibleLabel)));
    }

    [Fact]
    public void VisibleLabel_DisambiguatesNaturalLabelCollisionsAndSkipsReservedWxx()
    {
        var labels = AutocadExportPreflightService.ResolveVisibleLabels(new[]
        {
            Endpoint("endpoint-a", "machine-net-a", topologySignal: "+24V"),
            Endpoint("endpoint-b", "machine-net-b", topologySignal: "+24V"),
            Endpoint("endpoint-c", "machine-net-c", topologySignal: "W01"),
            Endpoint("endpoint-d", "machine-net-d")
        });

        Assert.Collection(labels,
            label => Assert.Equal(("machine-net-a", "+24V"), (label.MachineNetIdentity, label.VisibleLabel)),
            label => Assert.Equal(("machine-net-b", "+24V-02"), (label.MachineNetIdentity, label.VisibleLabel)),
            label => Assert.Equal(("machine-net-c", "W01"), (label.MachineNetIdentity, label.VisibleLabel)),
            label => Assert.Equal(("machine-net-d", "W02"), (label.MachineNetIdentity, label.VisibleLabel)));
    }

    [Fact]
    public void DuplicateIdsAndMissingMachineIdentity_BlockStagingForReview()
    {
        var request = new AutocadExportPreflightRequest
        {
            ConfirmedEndpoints =
            {
                Endpoint("pin-a", ""),
                Endpoint("PIN-A", "net-a")
            },
            ConfirmedEdges =
            {
                Edge("edge-a", "pin-a", "pin-a"),
                Edge("EDGE-A", "pin-a", "pin-a")
            }
        };

        var report = new AutocadExportPreflightService().Evaluate(request);

        Assert.False(report.CanStageForReview);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.DuplicateEndpointId);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.DuplicateEdgeId);
        Assert.Contains(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.MissingMachineNetIdentity);
    }

    [Fact]
    public void PowerTeamBoundary_RemainsWarningAndDoesNotCreateWiring()
    {
        var request = new AutocadExportPreflightRequest
        {
            ConfirmedEndpoints = { Endpoint("confirmed-pin", "machine-net") },
            OpenItems = { Item("power-boundary", AutocadExportPreflightOpenItemKind.PowerTeamResponsibilityBoundary) }
        };

        var report = new AutocadExportPreflightService().Evaluate(request);

        Assert.True(report.CanStageForReview);
        Assert.Single(request.ConfirmedEndpoints);
        Assert.Empty(request.ConfirmedEdges);
        Assert.Single(report.Issues, issue => issue.Code == AutocadExportPreflightIssueCode.PowerTeamResponsibilityBoundary);
    }

    private static AutocadExportPreflightEndpoint Endpoint(
        string endpointId,
        string machineNetIdentity,
        string? topologySignal = null,
        string? topologyPotential = null,
        bool isResolved = true,
        bool hasSymbolConnectionPoint = true) => new()
    {
        EndpointId = endpointId,
        MachineNetIdentity = machineNetIdentity,
        TopologySignal = topologySignal,
        TopologyPotential = topologyPotential,
        IsResolved = isResolved,
        HasSymbolConnectionPoint = hasSymbolConnectionPoint
    };

    private static AutocadExportPreflightOpenItem Item(string id, AutocadExportPreflightOpenItemKind kind) => new()
    {
        ItemId = id,
        Kind = kind
    };

    private static AutocadExportPreflightEdge Edge(string id, string from, string to) => new()
    {
        EdgeId = id,
        FromEndpointId = from,
        ToEndpointId = to
    };
}
