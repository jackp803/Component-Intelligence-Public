using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Validation;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableAssemblyEvidenceTests
{
    [Fact]
    public void CableAssembly_NewAuthority_DefaultsFailClosed()
    {
        var assembly = new CableAssembly { CableAssemblyId = "assembly-1" };

        Assert.Equal(CableConstructionType.Unknown, assembly.CableConstructionType);
        Assert.False(assembly.IsCustom);
    }

    [Fact]
    public void CableAssemblyMember_NewRole_DefaultsUnknown()
    {
        var member = new CableAssemblyMember { CableInstanceId = "cable-1" };

        Assert.Equal(CableAssemblySegmentRoleType.Unknown, member.SegmentRoleType);
        Assert.Null(member.SegmentRoleIndex);
        Assert.Null(member.SegmentRoleName);
    }

    [Fact]
    public void Legacy03_CustomTrue_MigratesTo04Custom()
    {
        var legacy = Project("0.3");
        legacy.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            IsCustom = true
        });

        var migrated = ElectricalProjectMigrator.Migrate(legacy);
        var assembly = Assert.Single(migrated.CableAssemblies);

        Assert.Equal("0.4", migrated.SchemaVersion);
        Assert.Equal(CableConstructionType.Custom, assembly.CableConstructionType);
        Assert.True(assembly.IsCustom);
    }

    [Fact]
    public void Legacy03_CustomFalse_MigratesTo04Unknown_NotPurchased()
    {
        var legacy = Project("0.3");
        legacy.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            IsCustom = false
        });

        var migrated = ElectricalProjectMigrator.Migrate(legacy);
        var assembly = Assert.Single(migrated.CableAssemblies);

        Assert.Equal("0.4", migrated.SchemaVersion);
        Assert.Equal(CableConstructionType.Unknown, assembly.CableConstructionType);
        Assert.NotEqual(CableConstructionType.Purchased, assembly.CableConstructionType);
        Assert.False(assembly.IsCustom);
    }

    [Fact]
    public void Legacy03_Purpose_DoesNotInferSegmentRole()
    {
        var legacy = Project("0.3");
        legacy.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            Members =
            {
                new CableAssemblyMember
                {
                    CableInstanceId = "cable-1",
                    Purpose = "TRUNK"
                }
            }
        });

        var migrated = ElectricalProjectMigrator.Migrate(legacy);
        var member = Assert.Single(Assert.Single(migrated.CableAssemblies).Members);

        Assert.Equal("TRUNK", member.Purpose);
        Assert.Equal(CableAssemblySegmentRoleType.Unknown, member.SegmentRoleType);
        Assert.Null(member.SegmentRoleIndex);
        Assert.Null(member.SegmentRoleName);
    }

    [Theory]
    [InlineData(CableConstructionType.Custom, true)]
    [InlineData(CableConstructionType.Purchased, false)]
    [InlineData(CableConstructionType.Unknown, false)]
    public void Schema04_ExplicitConstructionWinsAndProjectsLegacyBoolean(
        CableConstructionType value,
        bool expectedLegacy)
    {
        var project = Project("0.4");
        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            CableConstructionType = value,
            IsCustom = !expectedLegacy
        });

        var normalized = ElectricalProjectMigrator.Migrate(project);
        var assembly = Assert.Single(normalized.CableAssemblies);

        Assert.Equal(value, assembly.CableConstructionType);
        Assert.Equal(expectedLegacy, assembly.IsCustom);
    }

    [Fact]
    public async Task AssemblyEvidence_RoundTripsThroughTemporarySqlite_WithoutRenumberingBranches()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            var project = Project("0.4");
            project.Cables.AddRange(
            [
                Cable("cable-trunk"),
                Cable("cable-branch-1"),
                Cable("cable-branch-3")
            ]);
            project.CableAssemblies.Add(new CableAssembly
            {
                CableAssemblyId = "assembly-208",
                ReferenceDesignator = "208CBL",
                CableConstructionType = CableConstructionType.Custom,
                Members =
                {
                    new CableAssemblyMember
                    {
                        CableInstanceId = "cable-trunk",
                        SegmentRoleType = CableAssemblySegmentRoleType.Trunk
                    },
                    new CableAssemblyMember
                    {
                        CableInstanceId = "cable-branch-1",
                        SegmentRoleType = CableAssemblySegmentRoleType.Branch,
                        SegmentRoleIndex = 1
                    },
                    new CableAssemblyMember
                    {
                        CableInstanceId = "cable-branch-3",
                        SegmentRoleType = CableAssemblySegmentRoleType.Branch,
                        SegmentRoleIndex = 3
                    }
                }
            });

            await repository.SaveAsync(project);
            var loaded = await repository.GetAsync(project.ProjectId);

            Assert.NotNull(loaded);
            Assert.Equal("0.4", loaded!.SchemaVersion);
            var assembly = Assert.Single(loaded.CableAssemblies);
            Assert.Equal(CableConstructionType.Custom, assembly.CableConstructionType);
            Assert.True(assembly.IsCustom);
            Assert.Equal(CableAssemblySegmentRoleType.Trunk, assembly.Members.Single(member => member.CableInstanceId == "cable-trunk").SegmentRoleType);
            Assert.Equal(
                [1, 3],
                assembly.Members
                    .Where(member => member.SegmentRoleType == CableAssemblySegmentRoleType.Branch)
                    .OrderBy(member => member.SegmentRoleIndex)
                    .Select(member => member.SegmentRoleIndex));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Theory]
    [InlineData(CableConstructionType.Purchased)]
    [InlineData(CableConstructionType.Unknown)]
    public async Task AssemblyConstruction_RoundTripsWithoutInferringCustom(CableConstructionType constructionType)
    {
        var path = TemporaryDatabasePath();
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            var project = Project("0.4");
            project.CableAssemblies.Add(new CableAssembly
            {
                CableAssemblyId = "assembly-1",
                CableConstructionType = constructionType,
                IsCustom = true
            });

            await repository.SaveAsync(project);
            var loaded = await repository.GetAsync(project.ProjectId);

            Assert.NotNull(loaded);
            var assembly = Assert.Single(loaded!.CableAssemblies);
            Assert.Equal(constructionType, assembly.CableConstructionType);
            Assert.False(assembly.IsCustom);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void Validator_BlocksMoreThanOneTrunk_WithStableIdentities()
    {
        var project = AssemblyProject(
            Member("cable-b", CableAssemblySegmentRoleType.Trunk),
            Member("cable-a", CableAssemblySegmentRoleType.Trunk));

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-001");

        Assert.Equal(["assembly-1", "cable-a", "cable-b"], result.SourceObjectIds);
    }

    [Fact]
    public void Validator_BlocksDuplicateBranchIndex_WithStableIdentities()
    {
        var project = AssemblyProject(
            Member("cable-b", CableAssemblySegmentRoleType.Branch, index: 2),
            Member("cable-a", CableAssemblySegmentRoleType.Branch, index: 2));

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-002");

        Assert.Equal(["assembly-1", "cable-a", "cable-b"], result.SourceObjectIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_BlocksMissingOrNonPositiveBranchIndex(int? index)
    {
        var project = AssemblyProject(Member("cable-a", CableAssemblySegmentRoleType.Branch, index));

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-003");

        Assert.Equal(["assembly-1", "cable-a"], result.SourceObjectIds);
    }

    [Fact]
    public void Validator_BlocksTrunkCarryingIndex()
    {
        var project = AssemblyProject(Member("cable-a", CableAssemblySegmentRoleType.Trunk, index: 1));

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-004");

        Assert.Equal(["assembly-1", "cable-a"], result.SourceObjectIds);
    }

    [Fact]
    public void Validator_BlocksOtherWithoutRoleName()
    {
        var project = AssemblyProject(Member("cable-a", CableAssemblySegmentRoleType.Other, name: " "));

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-005");

        Assert.Equal(["assembly-1", "cable-a"], result.SourceObjectIds);
    }

    [Fact]
    public void Validator_BlocksMemberReferencingMissingCableInstance()
    {
        var project = AssemblyProject(Member("missing-cable", CableAssemblySegmentRoleType.Unknown));
        project.Cables.Clear();

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-006");

        Assert.Equal(["assembly-1", "missing-cable"], result.SourceObjectIds);
    }

    [Fact]
    public void Validator_BlocksDuplicateCableInstanceMembership()
    {
        var project = AssemblyProject(
            Member("cable-a", CableAssemblySegmentRoleType.Unknown),
            Member("cable-a", CableAssemblySegmentRoleType.Branch, index: 1));

        var result = AssertRule(project, "RULE-CABLE-ASSEMBLY-007");

        Assert.Equal(["assembly-1", "cable-a"], result.SourceObjectIds);
    }

    [Fact]
    public void Validator_AcceptsTrunkWithNonContiguousBranchIndexes()
    {
        var project = AssemblyProject(
            Member("cable-trunk", CableAssemblySegmentRoleType.Trunk),
            Member("cable-branch-1", CableAssemblySegmentRoleType.Branch, index: 1),
            Member("cable-branch-3", CableAssemblySegmentRoleType.Branch, index: 3));

        Assert.Empty(CableAssemblyResults(project));
    }

    [Fact]
    public void Validator_AcceptsNonContiguousBranchesWithoutTrunk()
    {
        var project = AssemblyProject(
            Member("cable-branch-1", CableAssemblySegmentRoleType.Branch, index: 1),
            Member("cable-branch-3", CableAssemblySegmentRoleType.Branch, index: 3));

        Assert.Empty(CableAssemblyResults(project));
    }

    [Fact]
    public void Validator_PreservesUnknownRoleWithoutInferringFromPurpose()
    {
        var member = Member("cable-a", CableAssemblySegmentRoleType.Unknown);
        member.Purpose = "TRUNK";
        var project = AssemblyProject(member);

        Assert.Empty(CableAssemblyResults(project));
        Assert.Equal(CableAssemblySegmentRoleType.Unknown, member.SegmentRoleType);
        Assert.Null(member.SegmentRoleIndex);
        Assert.Null(member.SegmentRoleName);
    }

    [Fact]
    public void Validator_ProducesDeterministicCableAssemblyDiagnostics()
    {
        var first = AssemblyProject(
            Member("cable-b", CableAssemblySegmentRoleType.Trunk, index: 2),
            Member("cable-a", CableAssemblySegmentRoleType.Trunk, index: 1),
            Member("missing-cable", CableAssemblySegmentRoleType.Other));
        first.Cables.RemoveAll(cable => cable.CableInstanceId == "missing-cable");

        var second = AssemblyProject(
            Member("missing-cable", CableAssemblySegmentRoleType.Other),
            Member("cable-a", CableAssemblySegmentRoleType.Trunk, index: 1),
            Member("cable-b", CableAssemblySegmentRoleType.Trunk, index: 2));
        second.Cables.RemoveAll(cable => cable.CableInstanceId == "missing-cable");

        var expected = new[]
        {
            "RULE-CABLE-ASSEMBLY-001|assembly-1,cable-a,cable-b",
            "RULE-CABLE-ASSEMBLY-004|assembly-1,cable-a",
            "RULE-CABLE-ASSEMBLY-004|assembly-1,cable-b",
            "RULE-CABLE-ASSEMBLY-005|assembly-1,missing-cable",
            "RULE-CABLE-ASSEMBLY-006|assembly-1,missing-cable"
        };

        Assert.Equal(expected, DiagnosticProjection(first));
        Assert.Equal(expected, DiagnosticProjection(second));
    }

    private static ElectricalProject Project(string schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
        ProjectId = $"project-{schemaVersion}"
    };

    private static CableInstance Cable(string cableInstanceId) => new()
    {
        CableInstanceId = cableInstanceId,
        CableDefinitionId = $"definition:{cableInstanceId}"
    };

    private static ElectricalProject AssemblyProject(params CableAssemblyMember[] members)
    {
        var project = Project("0.4");
        project.Cables.AddRange(members
            .Select(member => member.CableInstanceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Cable));
        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            Members = { }
        });
        project.CableAssemblies[0].Members.AddRange(members);
        return project;
    }

    private static CableAssemblyMember Member(
        string cableInstanceId,
        CableAssemblySegmentRoleType role,
        int? index = null,
        string? name = null) => new()
    {
        CableInstanceId = cableInstanceId,
        SegmentRoleType = role,
        SegmentRoleIndex = index,
        SegmentRoleName = name
    };

    private static ValidationResult AssertRule(ElectricalProject project, string ruleId) =>
        Assert.Single(CableAssemblyResults(project), result => result.RuleId == ruleId);

    private static IReadOnlyList<ValidationResult> CableAssemblyResults(ElectricalProject project) =>
        new ElectricalProjectValidator().Validate(project).Results
            .Where(result => result.RuleId.StartsWith("RULE-CABLE-ASSEMBLY-", StringComparison.Ordinal))
            .ToArray();

    private static string[] DiagnosticProjection(ElectricalProject project) =>
        CableAssemblyResults(project)
            .Select(result => $"{result.RuleId}|{string.Join(",", result.SourceObjectIds)}")
            .ToArray();

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"component-intelligence-cp2e1-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }
}
