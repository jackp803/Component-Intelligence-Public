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

    [Fact]
    public void UnknownConstructionAndRolesAreSaveableWarnings()
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);

        var validation = _service.Validate(project, draft);

        Assert.True(validation.CanSave);
        Assert.Equal(3, validation.Issues.Count(issue => issue.Severity == CableAssemblyEditIssueSeverity.Warning));
        Assert.Empty(validation.Issues.Where(issue => issue.IsBlocking));
    }

    [Theory]
    [InlineData("RULE-CABLE-ASSEMBLY-001")]
    [InlineData("RULE-CABLE-ASSEMBLY-002")]
    [InlineData("RULE-CABLE-ASSEMBLY-003")]
    [InlineData("RULE-CABLE-ASSEMBLY-004")]
    [InlineData("RULE-CABLE-ASSEMBLY-005")]
    [InlineData("RULE-CABLE-ASSEMBLY-006")]
    [InlineData("RULE-CABLE-ASSEMBLY-007")]
    public void ExistingStructuralRulesBlockCandidateSave(string ruleId)
    {
        var project = ProjectWithCables("cable-a", "cable-b", "cable-c");
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b", "conn-c"]);
        ConfigureInvalidDraft(draft, ruleId);

        var validation = _service.Validate(project, draft);

        Assert.False(validation.CanSave);
        Assert.Contains(validation.Issues, issue => issue.Code == ruleId && issue.IsBlocking);
    }

    [Fact]
    public void FailedApplyAndAbandonedDraftLeaveProjectUnchanged()
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        var before = Snapshot(project);
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);
        draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;
        draft.Members[1].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;

        Assert.Throws<InvalidOperationException>(() => _service.Apply(project, draft));
        Assert.Equal(before, Snapshot(project));

        var abandoned = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);
        abandoned.CableConstructionType = CableConstructionType.Custom;
        abandoned.Members.Clear();
        Assert.Equal(before, Snapshot(project));
    }

    [Fact]
    public void SuccessfulApplyAddsOneAssemblyWithoutGeneratingMemberTags()
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        project.Cables[0].ReferenceDesignator = "208CBL";
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);
        draft.CableConstructionType = CableConstructionType.Custom;
        draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;
        draft.Members[1].SegmentRoleType = CableAssemblySegmentRoleType.Branch;
        draft.Members[1].SegmentRoleIndex = 1;

        var assembly = _service.Apply(project, draft);

        Assert.Single(project.CableAssemblies);
        Assert.Same(assembly, project.CableAssemblies.Single());
        Assert.Equal(CableConstructionType.Custom, assembly.CableConstructionType);
        Assert.Equal("208CBL", project.Cables[0].ReferenceDesignator);
        Assert.Null(project.Cables[1].ReferenceDesignator);
    }

    [Fact]
    public void LengthEditsPreserveUntouchedProvenanceAndApplyExplicitUserChanges()
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        project.Cables[0].ProvidedLengthMm = 1250;
        project.Cables[0].LengthSource = CableLengthSource.Imported;
        project.Cables[1].ProvidedLengthMm = 900;
        project.Cables[1].LengthSource = CableLengthSource.Mechanical;
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);

        _service.SetLengthMetres(draft.Members.Single(item => item.CableInstanceId == "cable-b"), "2.75");
        _service.Apply(project, draft);

        Assert.Equal(1250, project.Cables[0].ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Imported, project.Cables[0].LengthSource);
        Assert.Equal(2750, project.Cables[1].ProvidedLengthMm);
        Assert.Equal(CableLengthSource.User, project.Cables[1].LengthSource);

        var existing = _service.PrepareExistingFromConnection(project, "conn-a").Draft!;
        _service.SetLengthMetres(existing.Members.Single(item => item.CableInstanceId == "cable-a"), null);
        _service.Apply(project, existing);
        Assert.Null(project.Cables[0].ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Unknown, project.Cables[0].LengthSource);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void InvalidLengthBlocksSave(string input)
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);

        _service.SetLengthMetres(draft.Members[0], input);

        var result = _service.Validate(project, draft);
        Assert.False(result.CanSave);
        Assert.Contains(result.Issues, issue => issue.Code == "INPUT-CABLE-LENGTH" && issue.IsBlocking);
    }

    [Fact]
    public void RemoveMemberPreservesCableConnectionRouteAndBranchGaps()
    {
        var project = ProjectWithCables("cable-a", "cable-b", "cable-c");
        project.TopologyRoutes.Add(new TopologyRouteGeometry
        {
            ConnectionId = "conn-b",
            Points = { new TopologyRoutePoint { X = 1, Y = 2 }, new TopologyRoutePoint { X = 3, Y = 4 } }
        });
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b", "conn-c"]);
        draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Branch;
        draft.Members[0].SegmentRoleIndex = 1;
        draft.Members[2].SegmentRoleType = CableAssemblySegmentRoleType.Branch;
        draft.Members[2].SegmentRoleIndex = 3;

        _service.RemoveMember(draft, "cable-b");
        _service.Apply(project, draft);

        Assert.Equal([1, 3], project.CableAssemblies.Single().Members
            .Where(item => item.SegmentRoleType == CableAssemblySegmentRoleType.Branch)
            .Select(item => item.SegmentRoleIndex));
        Assert.Contains(project.Cables, item => item.CableInstanceId == "cable-b");
        Assert.Contains(project.Connections, item => item.ConnectionId == "conn-b");
        Assert.Contains(project.TopologyRoutes, item => item.ConnectionId == "conn-b");
    }

    [Fact]
    public void AddMemberBlocksCrossAssemblyReparenting()
    {
        var project = ProjectWithCables("cable-a", "cable-b", "cable-c");
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);
        project.CableAssemblies.Add(Assembly("other-owner", "cable-c"));

        Assert.Throws<InvalidOperationException>(() => _service.AddMember(project, draft, "cable-c"));
        Assert.DoesNotContain(draft.Members, item => item.CableInstanceId == "cable-c");
    }

    [Fact]
    public void ExistingAssemblyMayRemoveMembershipWithoutInventingANewStructuralRule()
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-existing",
            IsCustom = true,
            Members =
            {
                new CableAssemblyMember { CableInstanceId = "cable-a", Purpose = "legacy-purpose" },
                new CableAssemblyMember { CableInstanceId = "cable-b" }
            }
        });
        var draft = _service.PrepareExistingFromConnection(project, "conn-a").Draft!;

        _service.RemoveMember(draft, "cable-b");
        var applied = _service.Apply(project, draft);

        Assert.Equal("assembly-existing", applied.CableAssemblyId);
        Assert.True(applied.IsCustom);
        Assert.Single(applied.Members);
        Assert.Equal("legacy-purpose", applied.Members[0].Purpose);
        Assert.Contains(project.Cables, cable => cable.CableInstanceId == "cable-b");
        Assert.Contains(project.Connections, connection => connection.CableInstanceId == "cable-b");
    }

    [Theory]
    [InlineData(CableLengthSource.Unknown, null)]
    [InlineData(CableLengthSource.User, 1000d)]
    [InlineData(CableLengthSource.Imported, 2000d)]
    [InlineData(CableLengthSource.Mechanical, 3000d)]
    public void UntouchedLengthEvidenceIsPreservedExactly(CableLengthSource source, double? length)
    {
        var project = ProjectWithCables("cable-a", "cable-b");
        project.Cables[0].LengthSource = source;
        project.Cables[0].ProvidedLengthMm = length;
        project.TopologyRoutes.Add(new TopologyRouteGeometry
        {
            ConnectionId = "conn-a",
            Points = { new TopologyRoutePoint { X = 0, Y = 0 }, new TopologyRoutePoint { X = 9999, Y = 9999 } }
        });
        var draft = _service.PrepareNewFromConnections(project, ["conn-a", "conn-b"]);

        _service.Apply(project, draft);

        Assert.Equal(length, project.Cables[0].ProvidedLengthMm);
        Assert.Equal(source, project.Cables[0].LengthSource);
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

    private static void ConfigureInvalidDraft(CableAssemblyEditDraft draft, string ruleId)
    {
        switch (ruleId)
        {
            case "RULE-CABLE-ASSEMBLY-001":
                draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;
                draft.Members[1].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;
                break;
            case "RULE-CABLE-ASSEMBLY-002":
                SetBranch(draft.Members[0], 2);
                SetBranch(draft.Members[1], 2);
                break;
            case "RULE-CABLE-ASSEMBLY-003":
                SetBranch(draft.Members[0], null);
                break;
            case "RULE-CABLE-ASSEMBLY-004":
                draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Trunk;
                draft.Members[0].SegmentRoleIndex = 1;
                break;
            case "RULE-CABLE-ASSEMBLY-005":
                draft.Members[0].SegmentRoleType = CableAssemblySegmentRoleType.Other;
                draft.Members[0].SegmentRoleName = " ";
                break;
            case "RULE-CABLE-ASSEMBLY-006":
                draft.Members.Add(new CableAssemblyMemberDraft
                {
                    CableInstanceId = "missing-cable",
                    DisplayLabel = "Missing cable"
                });
                break;
            case "RULE-CABLE-ASSEMBLY-007":
                draft.Members.Add(new CableAssemblyMemberDraft
                {
                    CableInstanceId = draft.Members[0].CableInstanceId,
                    DisplayLabel = "Duplicate"
                });
                break;
        }
    }

    private static void SetBranch(CableAssemblyMemberDraft member, int? index)
    {
        member.SegmentRoleType = CableAssemblySegmentRoleType.Branch;
        member.SegmentRoleIndex = index;
    }

    private static string Snapshot(ElectricalProject project)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(project, options);
    }
}
