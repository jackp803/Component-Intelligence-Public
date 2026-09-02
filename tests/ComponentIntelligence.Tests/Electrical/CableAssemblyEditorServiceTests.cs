using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableAssemblyEditorServiceTests
{
    private readonly CableAssemblyEditorService _service = new();

    [Fact]
    public void PrepareNewRequiresTwoUniqueExplicitCableInstances()
    {
        var project = ProjectWithCables("cable-a", "cable-b");

        Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareNewFromConnections(project, ["conn-a"]));

        project.Connections.Add(Connection("conn-a-duplicate", "cable-a"));
        Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareNewFromConnections(project, ["conn-a", "conn-a-duplicate"]));
    }

    [Fact]
    public void PrepareNewRejectsOrdinaryWireWithoutCreatingCableAuthority()
    {
        var project = ProjectWithCables("cable-a");
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "wire-only",
            FromEndpointId = "left",
            ToEndpointId = "right",
            Kind = ConnectionKind.Wire
        });
        var before = Snapshot(project);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareNewFromConnections(project, ["conn-a", "wire-only"]));

        Assert.Contains("wire-only", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(project));
    }

    [Fact]
    public void PrepareNewRejectsMissingConnectionCableAndExistingOwnership()
    {
        var project = ProjectWithCables("cable-a", "cable-b");

        Assert.Contains("missing", Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareNewFromConnections(project, ["conn-a", "missing"])).Message);

        project.Connections.Single(item => item.ConnectionId == "conn-b").CableInstanceId = "missing-cable";
        Assert.Contains("missing-cable", Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"])).Message);

        project.Connections.Single(item => item.ConnectionId == "conn-b").CableInstanceId = "cable-b";
        project.CableAssemblies.Add(Assembly("assembly-owner", "cable-b"));
        Assert.Contains("assembly-owner", Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"])).Message);
    }

    [Fact]
    public void PrepareNewReturnsDetachedUnknownDraftWithoutInference()
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        project.Cables[0].DisplayName = "TRUNK M12";
        project.Cables[0].CableDefinitionId = "PURCHASED-LOOKING-ID";
        var before = Snapshot(project);

        var draft = _service.PrepareNewFromConnections(project, ["conn-b", "conn-a"]);

        Assert.True(draft.IsNew);
        Assert.StartsWith("cable-assembly-", draft.CableAssemblyId, StringComparison.Ordinal);
        Assert.Equal(CableConstructionType.Unknown, draft.CableConstructionType);
        Assert.Equal(["cable-a", "cable-b"], draft.Members.Select(item => item.CableInstanceId).OrderBy(item => item));
        Assert.All(draft.Members, member =>
        {
            Assert.Equal(CableAssemblySegmentRoleType.Unknown, member.SegmentRoleType);
            Assert.Null(member.SegmentRoleIndex);
            Assert.Null(member.SegmentRoleName);
        });

        draft.CableConstructionType = CableConstructionType.Custom;
        draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;
        Assert.Equal(before, Snapshot(project));
    }

    [Fact]
    public void PrepareExistingDistinguishesNoOwnerFoundAndAmbiguousOwner()
    {
        var project = ProjectWithCables("cable-a", "cable-b");

        var noOwner = _service.PrepareExistingFromConnection(project, "conn-a");

        Assert.Equal(CableAssemblyOpenStatus.NotInAssembly, noOwner.Status);
        Assert.Null(noOwner.Draft);

        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            ReferenceDesignator = "CBL-A",
            CableConstructionType = CableConstructionType.Custom,
            Members =
            {
                new CableAssemblyMember
                {
                    CableInstanceId = "cable-a",
                    SegmentRoleType = CableAssemblySegmentRoleType.Branch,
                    SegmentRoleIndex = 3
                },
                new CableAssemblyMember
                {
                    CableInstanceId = "cable-b",
                    SegmentRoleType = CableAssemblySegmentRoleType.Trunk
                }
            }
        });

        var found = _service.PrepareExistingFromConnection(project, "conn-a");

        Assert.Equal(CableAssemblyOpenStatus.Found, found.Status);
        Assert.NotNull(found.Draft);
        Assert.False(found.Draft!.IsNew);
        Assert.Equal("assembly-1", found.Draft.CableAssemblyId);
        Assert.Equal("CBL-A", found.Draft.ReferenceDesignator);
        Assert.Equal(CableConstructionType.Custom, found.Draft.CableConstructionType);
        Assert.Equal(3, found.Draft.Members.Single(item => item.CableInstanceId == "cable-a").SegmentRoleIndex);

        found.Draft.Members.Clear();
        Assert.Equal(2, project.CableAssemblies.Single().Members.Count);

        project.CableAssemblies.Add(Assembly("assembly-2", "cable-a"));
        Assert.Throws<InvalidOperationException>(() =>
            _service.PrepareExistingFromConnection(project, "conn-a"));
    }

    [Fact]
    public void BranchSuggestionUsesMaximumPositiveIndexWithoutFillingGaps()
    {
        var project = ProjectWithCables("cable-a", "cable-b", "cable-c");
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b", "conn-c"]);

        Assert.Equal(1, _service.SuggestNextBranchIndex(draft));
        draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Branch;
        draft.Members[0].SegmentRoleIndex = 1;
        draft.Members[1].SegmentRoleType = CableAssemblySegmentRoleType.Branch;
        draft.Members[1].SegmentRoleIndex = 3;

        Assert.Equal(4, _service.SuggestNextBranchIndex(draft));
    }

    private static ElectricalProject ProjectWithCables(params string[] cableIds)
    {
        var project = new ElectricalProject { ProjectId = "project-1" };
        foreach (var cableId in cableIds)
        {
            project.Cables.Add(new CableInstance
            {
                CableInstanceId = cableId,
                CableDefinitionId = $"definition-{cableId}",
                DisplayName = cableId
            });
            project.Connections.Add(Connection($"conn-{cableId[^1]}", cableId));
        }

        return project;
    }

    private static ElectricalConnection Connection(string connectionId, string cableId) => new()
    {
        ConnectionId = connectionId,
        FromEndpointId = $"{connectionId}-from",
        ToEndpointId = $"{connectionId}-to",
        Kind = ConnectionKind.Cable,
        CableInstanceId = cableId
    };

    private static CableAssembly Assembly(string assemblyId, string cableId) => new()
    {
        CableAssemblyId = assemblyId,
        Members = { new CableAssemblyMember { CableInstanceId = cableId } }
    };

    private static string Snapshot(ElectricalProject project)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(project, options);
    }
}
