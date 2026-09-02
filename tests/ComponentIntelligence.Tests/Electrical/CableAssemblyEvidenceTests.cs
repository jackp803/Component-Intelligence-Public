using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;

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

    private static ElectricalProject Project(string schemaVersion) => new()
    {
        SchemaVersion = schemaVersion,
        ProjectId = $"project-{schemaVersion}"
    };
}
