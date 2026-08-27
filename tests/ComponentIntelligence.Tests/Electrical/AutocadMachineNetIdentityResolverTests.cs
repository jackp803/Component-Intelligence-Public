using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadMachineNetIdentityResolverTests : IDisposable
{
    private const string ExpectedDerivedIdentity = "NET-TBD-8C3D170C3A6A";
    private readonly List<string> _temporaryPaths = [];

    [Fact]
    public void BlankNetId_ConnectedGraphUsesOneDeterministicExistingCompatibleIdentity()
    {
        var first = ConnectedProject(reverseOrder: false);
        var second = ConnectedProject(reverseOrder: true);

        var firstIdentities = first.Connections
            .Select(connection => AutocadMachineNetIdentityResolver.Resolve(first, connection))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var secondIdentities = second.Connections
            .Select(connection => AutocadMachineNetIdentityResolver.Resolve(second, connection))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([ExpectedDerivedIdentity], firstIdentities);
        Assert.Equal([ExpectedDerivedIdentity], secondIdentities);
    }

    [Fact]
    public void SoleExplicitNetId_PropagatesAcrossConnectedComponentRegardlessOfOrder()
    {
        var first = ConnectedProject(reverseOrder: false);
        first.Connections[0].NetId = "  explicit-power-net  ";
        var second = ConnectedProject(reverseOrder: true);
        second.Connections[1].NetId = "explicit-power-net";

        var firstIdentities = first.Connections
            .Select(connection => AutocadMachineNetIdentityResolver.Resolve(first, connection))
            .ToArray();
        var secondIdentities = second.Connections
            .Select(connection => AutocadMachineNetIdentityResolver.Resolve(second, connection))
            .ToArray();

        Assert.Equal(["explicit-power-net", "explicit-power-net"], firstIdentities);
        Assert.Equal(["explicit-power-net", "explicit-power-net"], secondIdentities);
    }

    [Fact]
    public void ConflictingExplicitNetIds_BlockBuilderPreflight()
    {
        var project = ConnectedProject(reverseOrder: false);
        project.Connections[0].NetId = "net-one";
        project.Connections[1].NetId = "net-two";

        var preparation = new AutocadStagingGraphBuilder().Prepare(project, Bindings("A", "B", "C"));

        Assert.False(preparation.Preflight.CanStageForReview);
        Assert.Null(preparation.Graph);
        Assert.Contains(preparation.Preflight.Issues,
            issue => issue.Code == AutocadExportPreflightIssueCode.ConflictingExplicitNetIdentity &&
                     issue.Severity == AutocadExportPreflightSeverity.Error);
    }

    [Fact]
    public void LoaderRejectsEvidenceForComponentWithConflictingExplicitNetIds()
    {
        var project = ConnectedProject(reverseOrder: false);
        project.Connections[0].NetId = "net-one";
        project.Connections[1].NetId = "net-two";
        var path = WriteEvidence(project, "net-one");

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(project, path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues,
            issue => issue.Code == "EngineeringDrawingEvidenceConflictingNetIdentity" && issue.Severity == "Error");
    }

    [Fact]
    public void LoaderRejectsUnknownDerivedIdentity()
    {
        var project = ConnectedProject(reverseOrder: false);
        var path = WriteEvidence(project, "NET-TBD-000000000000");

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(project, path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "EngineeringDrawingEvidenceUnknownNet");
    }

    [Fact]
    public void BuilderAndLoaderAcceptTheSameDerivedIdentity()
    {
        var project = ConnectedProject(reverseOrder: false);
        var path = WriteEvidence(project, ExpectedDerivedIdentity);

        var load = AutocadEngineeringDrawingEvidenceLoader.Load(project, path);
        var preparation = new AutocadStagingGraphBuilder().Prepare(
            project,
            Bindings("A", "B", "C"),
            load.Evidence);

        Assert.True(load.Succeeded);
        Assert.True(preparation.Preflight.CanStageForReview);
        var route = Assert.Single(Assert.IsType<AutocadStagingGraphContract>(preparation.Graph).Routes);
        Assert.Equal(ExpectedDerivedIdentity, route.NetIdentity);
        Assert.Equal(ExpectedDerivedIdentity, Assert.Single(load.Evidence.PowerFlowOrientations).NetIdentity);
    }

    [Fact]
    public void BuilderAndLoaderUseTheSamePropagatedExplicitIdentity()
    {
        var project = ConnectedProject(reverseOrder: false);
        project.Connections[0].NetId = "  explicit-power-net  ";
        var path = WriteEvidence(project, "explicit-power-net");

        var load = AutocadEngineeringDrawingEvidenceLoader.Load(project, path);
        var preparation = new AutocadStagingGraphBuilder().Prepare(
            project,
            Bindings("A", "B", "C"),
            load.Evidence);

        Assert.True(load.Succeeded);
        Assert.True(preparation.Preflight.CanStageForReview);
        var route = Assert.Single(Assert.IsType<AutocadStagingGraphContract>(preparation.Graph).Routes);
        Assert.Equal("explicit-power-net", route.NetIdentity);
        Assert.Equal("explicit-power-net", Assert.Single(load.Evidence.PowerFlowOrientations).NetIdentity);
    }

    public void Dispose()
    {
        foreach (var path in _temporaryPaths.Where(File.Exists)) File.Delete(path);
    }

    private static ElectricalProject ConnectedProject(bool reverseOrder)
    {
        var project = new ElectricalProject { ProjectId = "derived-net-project" };
        project.Components.Add(Component("A"));
        project.Components.Add(Component("B"));
        project.Components.Add(Component("C"));
        if (reverseOrder)
        {
            project.Connections.Add(Connection("different-connection-id-1", "C", "B", null));
            project.Connections.Add(Connection("different-connection-id-2", "B", "A", " "));
        }
        else
        {
            project.Connections.Add(Connection("wire-b-c", "B", "C", null));
            project.Connections.Add(Connection("wire-a-b", "A", "B", " "));
        }
        return project;
    }

    private static ComponentInstance Component(string endpointId) => new()
    {
        ComponentInstanceId = $"component-{endpointId}",
        ComponentDefinitionId = $"definition-{endpointId}",
        TypeKey = "MUST-NOT-AFFECT-NET-IDENTITY",
        Ports =
        {
            new ComponentPort
            {
                PortId = $"port-{endpointId}",
                Name = endpointId,
                Pins =
                {
                    new ComponentPin
                    {
                        PinId = endpointId,
                        PinNumber = "1",
                        Layer = ElectricalLayer.Power,
                        Status = PinStatus.Normal
                    }
                }
            }
        }
    };

    private static ElectricalConnection Connection(string id, string from, string to, string? netId) => new()
    {
        ConnectionId = id,
        FromEndpointId = from,
        ToEndpointId = to,
        NetId = netId
    };

    private string WriteEvidence(ElectricalProject project, string netIdentity)
    {
        var roles = string.Join(',', project.Components.Select(component =>
            $$"""{"componentInstanceId":"{{component.ComponentInstanceId}}","role":"ConsumerOrConverter","status":"Confirmed","evidenceSource":"engineer"}"""));
        var json = $$"""
            {"schemaVersion":"ci-autocad-engineering-drawing-evidence.v1","projectId":"{{project.ProjectId}}",
             "componentRoles":[{{roles}}],
             "powerFlowOrientations":[
               {"netIdentity":"{{netIdentity}}","orientation":"LeftToRight","sourceEndpointId":"A","destinationEndpointId":"C","status":"Confirmed","evidenceSource":"engineer"}
             ],
             "crossPageContinuations":[],"cableInstanceOverrides":[]}
            """;
        var path = Path.Combine(Path.GetTempPath(), $"ci-machine-net-evidence-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        _temporaryPaths.Add(path);
        return path;
    }

    private static IReadOnlyList<AutocadConnectionPointBinding> Bindings(params string[] endpointIds) =>
        endpointIds.Select(endpointId => new AutocadConnectionPointBinding
        {
            EndpointId = endpointId,
            SymbolKey = $"SYMBOL-{endpointId}",
            ConnectionPointId = $"XTERM-{endpointId}"
        }).ToArray();
}
