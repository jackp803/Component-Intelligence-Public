using ComponentIntelligence.Electrical.Domain;

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
}
