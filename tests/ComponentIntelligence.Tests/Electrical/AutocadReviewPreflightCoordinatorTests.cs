using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadReviewPreflightCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-acade-ui-preflight-{Guid.NewGuid():N}");

    [Fact]
    public void CompleteSidecars_AreLoadedAndDrawingEvidenceReachesGraphBuilder()
    {
        var project = Project();
        var bindingPath = Write("bindings.json", """
            {"schemaVersion":"ci-acade-connection-points.v1","bindings":[
              {"endpointId":"left:1","symbolKey":"SYM-LEFT","connectionPointId":"X1TERM01"},
              {"endpointId":"right:1","symbolKey":"SYM-RIGHT","connectionPointId":"X2TERM01"}
            ]}
            """);
        var evidencePath = Write("evidence.json", EvidenceJson());
        var registryPath = Write("cm-lrdu-symbol-acceptance-registry.v1.json", "{}");

        var result = new AutocadReviewPreflightCoordinator().Prepare(
            project, bindingPath, evidencePath, registryPath);

        Assert.True(result.CanLaunch);
        var graph = Assert.IsType<AutocadStagingGraphContract>(result.Preparation!.Graph);
        Assert.Equal("lrdu-staging-route.v1", graph.SchemaVersion);
        Assert.Equal(DrawingPageArchetype.Interface, graph.PageArchetypeHint.Archetype);
        var roles = Assert.Single(graph.Routes).Nodes
            .Where(node => node.ComponentInstanceId is not null)
            .ToDictionary(node => node.ComponentInstanceId!, node => node.DrawingRole);
        Assert.Equal(ComponentDrawingRole.PowerSource, roles["left"]);
        Assert.Equal(ComponentDrawingRole.ConsumerOrConverter, roles["right"]);
    }

    [Fact]
    public void MissingRegistry_IsUiPreflightErrorAndCannotLaunch()
    {
        var project = Project();
        var bindingPath = Write("bindings.json", """
            {"schemaVersion":"ci-acade-connection-points.v1","bindings":[
              {"endpointId":"left:1","symbolKey":"SYM-LEFT","connectionPointId":"X1TERM01"},
              {"endpointId":"right:1","symbolKey":"SYM-RIGHT","connectionPointId":"X2TERM01"}
            ]}
            """);
        var evidencePath = Write("evidence.json", EvidenceJson());
        var missingRegistry = Path.Combine(_root, "missing-registry.json");

        var result = new AutocadReviewPreflightCoordinator().Prepare(
            project, bindingPath, evidencePath, missingRegistry);

        Assert.False(result.CanLaunch);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "SymbolAcceptanceRegistryMissing" && issue.Severity == "Error");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static ElectricalProject Project()
    {
        var project = new ElectricalProject { ProjectId = "ui-project" };
        project.Nets.Add(new NetDefinition { NetId = "net-control", Label = "CONTROL", Layer = ElectricalLayer.Digital });
        project.Components.Add(Component("left"));
        project.Components.Add(Component("right"));
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-1",
            FromEndpointId = "left:1",
            ToEndpointId = "right:1",
            NetId = "net-control"
        });
        return project;
    }

    private static ComponentInstance Component(string id) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"definition:{id}",
        TypeKey = "NOT-EVIDENCE",
        Ports =
        {
            new ComponentPort
            {
                PortId = $"{id}:port",
                Name = id,
                Pins = { new ComponentPin { PinId = $"{id}:1", PinNumber = "1" } }
            }
        }
    };

    private static string EvidenceJson() => """
        {"schemaVersion":"ci-autocad-engineering-drawing-evidence.v1","projectId":"ui-project",
         "componentRoles":[
           {"componentInstanceId":"left","role":"PowerSource","status":"Confirmed","evidenceSource":"engineer"},
           {"componentInstanceId":"right","role":"ConsumerOrConverter","status":"Confirmed","evidenceSource":"engineer"}
         ],
         "pageArchetypeHint":{"archetype":"Interface","status":"Confirmed","evidenceSource":"engineer"},
         "powerFlowOrientations":[],"crossPageContinuations":[],"cableInstanceOverrides":[]}
        """;

    private string Write(string name, string contents)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
