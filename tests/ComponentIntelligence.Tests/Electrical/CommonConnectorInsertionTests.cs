using ComponentIntelligence.Electrical.Topology;
using System.Text.Json;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CommonConnectorInsertionTests
{
    [Fact]
    public void PreparationAndIncompleteSelectionDoNotMutateProject()
    {
        var project = UnifiedCableSettingsTests.Project();
        var before = JsonSerializer.Serialize(project);
        var editor = new TopologyConnectionEditor();
        var draft = editor.PrepareCommonConnector(project, "C1", CommonConnectorCatalog.ShieldedCat6Rj45FemaleCouplerId);
        Assert.Null(draft.FromContactId);
        Assert.Null(draft.ToContactId);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Throws<InvalidOperationException>(() => editor.ApplyCommonConnector(project, draft));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ExplicitCatalogContactsAreUsedWithoutSameNumberInference()
    {
        var project = UnifiedCableSettingsTests.Project();
        var untouched = JsonSerializer.Serialize(project.Connections[1]);
        project.Connections[0].MaxVoltageDropPercent = 3;
        project.Connections[0].InstallationMethod = "explicit-installation";
        var editor = new TopologyConnectionEditor();
        var draft = editor.PrepareCommonConnector(project, "C1", CommonConnectorCatalog.ShieldedCat6Rj45FemaleCouplerId);
        var first = draft.Contacts.First();
        var second = draft.Contacts.Last();
        draft.FromContactId = first.EndpointId;
        draft.ToContactId = second.EndpointId;
        var connector = editor.ApplyCommonConnector(project, draft);
        Assert.Equal(CommonConnectorCatalog.ShieldedCat6Rj45FemaleCouplerId, connector.ComponentDefinitionId);
        Assert.Contains(project.Connections, c => c.FromEndpointId == "A1" && c.ToEndpointId == first.EndpointId && c.NetId == "N1");
        Assert.Contains(project.Connections, c => c.FromEndpointId == second.EndpointId && c.ToEndpointId == "B1" && c.NetId == "N1");
        Assert.Equal(untouched, JsonSerializer.Serialize(project.Connections.Single(c => c.ConnectionId == "C2")));
        Assert.Empty(project.Cables);
        Assert.Empty(project.CableAssemblies);
        Assert.All(project.Connections.Where(c => c.ConnectionId != "C2"), c => {
            Assert.Equal(3, c.MaxVoltageDropPercent);
            Assert.Equal("explicit-installation", c.InstallationMethod);
        });
    }

    [Fact]
    public void ChangedSourceConnectionAndForeignContactFailClosed()
    {
        var project = UnifiedCableSettingsTests.Project();
        var editor = new TopologyConnectionEditor();
        var draft = editor.PrepareCommonConnector(project, "C1", CommonConnectorCatalog.ShieldedCat6Rj45FemaleCouplerId);
        draft.FromContactId = "foreign";
        draft.ToContactId = draft.Contacts.Last().EndpointId;
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyCommonConnector(project, draft));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        draft.FromContactId = draft.Contacts.First().EndpointId;
        project.Connections[0] = new() { ConnectionId = "C1", FromEndpointId = "A1", ToEndpointId = "B2", NetId = "N1" };
        before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => editor.ApplyCommonConnector(project, draft));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }
}
