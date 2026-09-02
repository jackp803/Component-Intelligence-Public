using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
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

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"component-intelligence-cp2e1-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }
}
