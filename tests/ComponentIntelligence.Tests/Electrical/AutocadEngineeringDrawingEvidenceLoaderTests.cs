using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class AutocadEngineeringDrawingEvidenceLoaderTests : IDisposable
{
    private readonly List<string> _temporaryPaths = [];

    [Fact]
    public void MissingSidecar_IsAnErrorAndDoesNotCreateAFile()
    {
        var path = NewPath();

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(Project(), path);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(path));
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Error", issue.Severity);
        Assert.Equal("EngineeringDrawingEvidenceSidecarMissing", issue.Code);
    }

    [Fact]
    public void WrongSchema_IsAnError()
    {
        var path = Write("""
            {"schemaVersion":"ci-autocad-engineering-drawing-evidence.v2","projectId":"project-1",
             "componentRoles":[],"powerFlowOrientations":[],"crossPageContinuations":[],"cableInstanceOverrides":[]}
            """);

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(Project(), path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == "EngineeringDrawingEvidenceSidecarInvalid");
    }

    [Fact]
    public void CompleteV1Sidecar_LoadsEngineerApprovedEvidenceWithoutDerivingValues()
    {
        var path = Write("""
            {
              "schemaVersion":"ci-autocad-engineering-drawing-evidence.v1",
              "projectId":"project-1",
              "componentRoles":[
                {"componentInstanceId":"left","role":"PowerSource","status":"Confirmed","evidenceSource":"engineer-ticket-12"},
                {"componentInstanceId":"right","role":"ConsumerOrConverter","status":"Confirmed","evidenceSource":"engineer-ticket-12"}
              ],
              "pageArchetypeHint":{"archetype":"PowerDistribution","status":"Confirmed","evidenceSource":"engineer-ticket-12"},
              "powerFlowOrientations":[
                {"netIdentity":"net-power","orientation":"LeftToRight","sourceEndpointId":"left:1","destinationEndpointId":"right:1","status":"Confirmed","evidenceSource":"engineer-ticket-12"}
              ],
              "crossPageContinuations":[
                {"pairIdentity":"pair-1","sourceEndpointId":"left:1","destinationEndpointId":"right:1","sourcePageId":"P-01","destinationPageId":"P-02","status":"Confirmed","evidenceSource":"engineer-ticket-12"}
              ],
              "cableInstanceOverrides":[
                {"cableInstanceId":"cable-1","specificationOverride":"2x1.5 mm2","catalogOverride":"CAT-100"}
              ]
            }
            """);

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(Project(), path);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Issues);
        Assert.Equal(ComponentDrawingRole.PowerSource, result.Evidence.ComponentRoles[0].Role);
        Assert.Equal(DrawingPageArchetype.PowerDistribution, result.Evidence.PageArchetypeHint!.Archetype);
        Assert.Equal(PowerFlowOrientation.LeftToRight, result.Evidence.PowerFlowOrientations[0].Orientation);
        Assert.Equal("pair-1", result.Evidence.CrossPageContinuations[0].PairIdentity);
        Assert.Equal(("2x1.5 mm2", "CAT-100"),
            (result.Evidence.CableInstanceOverrides[0].SpecificationOverride,
             result.Evidence.CableInstanceOverrides[0].CatalogOverride));
    }

    [Theory]
    [InlineData("componentRoles", "{\"componentInstanceId\":\"left\",\"role\":\"PowerSource\",\"status\":\"Confirmed\"}", "EngineeringDrawingEvidenceDuplicateComponentRole")]
    [InlineData("powerFlowOrientations", "{\"netIdentity\":\"net-power\",\"orientation\":\"LeftToRight\",\"sourceEndpointId\":\"left:1\",\"destinationEndpointId\":\"right:1\",\"status\":\"Confirmed\"}", "EngineeringDrawingEvidenceDuplicatePowerFlow")]
    [InlineData("crossPageContinuations", "{\"pairIdentity\":\"pair-1\",\"sourceEndpointId\":\"left:1\",\"destinationEndpointId\":\"right:1\",\"sourcePageId\":\"P-01\",\"destinationPageId\":\"P-02\",\"status\":\"Confirmed\"}", "EngineeringDrawingEvidenceDuplicateCrossPageContinuation")]
    [InlineData("cableInstanceOverrides", "{\"cableInstanceId\":\"cable-1\",\"catalogOverride\":\"CAT-100\"}", "EngineeringDrawingEvidenceDuplicateCableOverride")]
    public void DuplicateEvidenceIdentity_IsAnError(string propertyName, string itemJson, string expectedCode)
    {
        var path = Write($$"""
            {"schemaVersion":"ci-autocad-engineering-drawing-evidence.v1","projectId":"project-1",
             "componentRoles":{{(propertyName == "componentRoles" ? $"[{itemJson},{itemJson}]" : "[]")}},
             "powerFlowOrientations":{{(propertyName == "powerFlowOrientations" ? $"[{itemJson},{itemJson}]" : "[]")}},
             "crossPageContinuations":{{(propertyName == "crossPageContinuations" ? $"[{itemJson},{itemJson}]" : "[]")}},
             "cableInstanceOverrides":{{(propertyName == "cableInstanceOverrides" ? $"[{itemJson},{itemJson}]" : "[]")}}}
            """);

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(Project(), path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Theory]
    [InlineData("wrong-project", "left", "net-power", "left:1", "cable-1", "EngineeringDrawingEvidenceProjectMismatch")]
    [InlineData("project-1", "missing-component", "net-power", "left:1", "cable-1", "EngineeringDrawingEvidenceUnknownComponent")]
    [InlineData("project-1", "left", "missing-net", "left:1", "cable-1", "EngineeringDrawingEvidenceUnknownNet")]
    [InlineData("project-1", "left", "net-power", "missing:endpoint", "cable-1", "EngineeringDrawingEvidenceUnknownEndpoint")]
    [InlineData("project-1", "left", "net-power", "left:1", "missing-cable", "EngineeringDrawingEvidenceUnknownCable")]
    public void EvidenceReferencingUnknownProjectObject_IsAnError(
        string projectId,
        string componentId,
        string netIdentity,
        string sourceEndpointId,
        string cableId,
        string expectedCode)
    {
        var path = Write($$"""
            {"schemaVersion":"ci-autocad-engineering-drawing-evidence.v1","projectId":"{{projectId}}",
             "componentRoles":[{"componentInstanceId":"{{componentId}}","role":"PowerSource","status":"Confirmed"}],
             "powerFlowOrientations":[{"netIdentity":"{{netIdentity}}","orientation":"LeftToRight","sourceEndpointId":"{{sourceEndpointId}}","destinationEndpointId":"right:1","status":"Confirmed"}],
             "crossPageContinuations":[{"pairIdentity":"pair-1","sourceEndpointId":"{{sourceEndpointId}}","destinationEndpointId":"right:1","sourcePageId":"P-01","destinationPageId":"P-02","status":"Confirmed"}],
             "cableInstanceOverrides":[{"cableInstanceId":"{{cableId}}","catalogOverride":"CAT-100"}]}
            """);

        var result = AutocadEngineeringDrawingEvidenceLoader.Load(Project(), path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    public void Dispose()
    {
        foreach (var path in _temporaryPaths.Where(File.Exists)) File.Delete(path);
    }

    private static ElectricalProject Project()
    {
        var project = new ElectricalProject { ProjectId = "project-1" };
        project.Nets.Add(new NetDefinition { NetId = "net-power", Label = "+24V", Layer = ElectricalLayer.Power });
        project.Components.Add(Component("left"));
        project.Components.Add(Component("right"));
        project.Cables.Add(new CableInstance { CableInstanceId = "cable-1", CableDefinitionId = "cable-definition-1" });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-1",
            FromEndpointId = "left:1",
            ToEndpointId = "right:1",
            NetId = "net-power"
        });
        return project;
    }

    private static ComponentInstance Component(string id) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"definition:{id}",
        TypeKey = "MUST-NOT-BE-EVIDENCE",
        DisplayName = "MUST NOT BE USED AS EVIDENCE",
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

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ci-drawing-evidence-{Guid.NewGuid():N}.json");
        _temporaryPaths.Add(path);
        return path;
    }

    private string Write(string contents)
    {
        var path = NewPath();
        File.WriteAllText(path, contents);
        return path;
    }
}
