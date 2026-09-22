using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class MultiEndCableEditorServiceTests
{
    private readonly MultiEndCableEditorService _service = new();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void RawWiresBecomeOneCableWithoutChangingElectricalTruth(int branches)
    {
        var p = Fixture(branches);
        var electrical = Truth(p);
        var objects = JsonSerializer.Serialize(p.Components);
        var d = _service.PrepareNew(p, p.Connections.Select(c => c.ConnectionId).ToArray());
        Assert.Null(d.CommonPortId);
        Assert.Equal(CableConstructionType.Unknown, d.ConstructionType);
        Assert.Empty(p.Cables);
        _service.ConfirmEnds(p, d, "common");
        d.ConstructionType = CableConstructionType.Custom;
        d.Reference = "Y-01";
        var a = _service.Apply(p, d);
        Assert.Single(p.Cables);
        Assert.Single(p.CableAssemblies);
        Assert.Equal(branches, a.PhysicalTopology!.Branches.Count);
        Assert.Equal(p.Connections.Count, a.PhysicalTopology.ConnectionIds.Count);
        Assert.All(p.Connections, c => Assert.Equal(p.Cables[0].CableInstanceId, c.CableInstanceId));
        Assert.All(p.Connections, c => Assert.Null(c.CableCoreId));
        Assert.Empty(p.Cables[0].CoreAssignments);
        Assert.Equal(electrical, Truth(p));
        Assert.Equal(objects, JsonSerializer.Serialize(p.Components));
        Assert.Empty(p.TerminalBlocks);
        Assert.Null(a.PhysicalTopology.TrunkLengthMm);
        Assert.All(a.PhysicalTopology.Branches, b => Assert.Null(b.LengthMm));
    }

    [Fact]
    public void CancelAndFailedApplyArePureAndConstructionMustBeExplicit()
    {
        var p = Fixture(2);
        var before = JsonSerializer.Serialize(p);
        var d = _service.PrepareNew(p, p.Connections.Select(c => c.ConnectionId).ToArray());
        Assert.Throws<InvalidOperationException>(() => _service.Apply(p, d));
        _service.ConfirmEnds(p, d, "common");
        Assert.Throws<InvalidOperationException>(() => _service.Apply(p, d));
        Assert.Equal(before, JsonSerializer.Serialize(p));
        d.ConstructionType = CableConstructionType.Purchased;
        Assert.Equal(CableConstructionType.Purchased, _service.Apply(p, d).CableConstructionType);
    }

    [Fact]
    public void ReopenRoundtripKeepsNonContiguousIndicesAndMembershipRemovalPreservesWire()
    {
        var p = Fixture(3);
        var d = Confirmed(p);
        d.Branches[1].Index = 5;
        d.TrunkLengthMm = 1000;
        d.Branches[0].LengthMm = 200;
        var a = _service.Apply(p, d);
        p = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(p))!;
        var reopened = _service.PrepareExisting(p, a.CableAssemblyId);
        Assert.Equal(5, reopened.Branches.Single(b => b.PortId == "branch2").Index);
        Assert.Equal(1000, reopened.TrunkLengthMm);
        var original = Truth(p);
        var removed = reopened.ConnectionIds.Last();
        reopened.ConnectionIds.Remove(removed);
        _service.ConfirmEnds(p, reopened, "common");
        _service.Apply(p, reopened);
        Assert.Equal(original, Truth(p));
        Assert.Null(p.Connections.Single(c => c.ConnectionId == removed).CableInstanceId);
        Assert.Single(p.Cables);
        Assert.Single(p.CableAssemblies);
        Assert.Contains(p.CableAssemblies[0].PhysicalTopology!.Branches, b => b.Index == 5);
    }

    [Fact]
    public void OverlapAndContradictoryAssignmentBlockBeforeMutation()
    {
        var p = Fixture(2);
        var d = Confirmed(p);
        _service.Apply(p, d);
        Assert.Throws<InvalidOperationException>(() => _service.PrepareNew(p, d.ConnectionIds));
        var edit = _service.PrepareExisting(p, d.AssemblyId);
        p.Connections[0].CableInstanceId = "other";
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.Apply(p, edit));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void InvalidGroupingDuplicateOwnershipAndPortLevelEndpointsFailClosed()
    {
        var p = Fixture(2);
        var d = _service.PrepareNew(p, p.Connections.Select(c => c.ConnectionId).ToArray());
        Assert.Throws<InvalidOperationException>(() => _service.ConfirmEnds(p, d, "branch1"));
        p.Components[1].Ports[0].Pins.Add(p.Components[0].Ports[0].Pins[0]);
        Assert.Throws<InvalidOperationException>(() => _service.PrepareNew(p, d.ConnectionIds));
        p = Fixture(2);
        p.Connections.Add(new ElectricalConnection { ConnectionId = "port-wire", FromEndpointId = "common", ToEndpointId = "branch1" });
        Assert.Throws<InvalidOperationException>(() => _service.PrepareNew(p, ["port-wire", "wire1-1"]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLengthFailsClosed(double length)
    {
        var p = Fixture(2);
        var d = Confirmed(p);
        d.TrunkLengthMm = length;
        Assert.Throws<InvalidOperationException>(() => _service.Apply(p, d));
        Assert.Empty(p.Cables);
    }

    [Fact]
    public void DuplicateBranchIndicesAndStaleConfirmationFailClosed()
    {
        var p = Fixture(3);
        var d = Confirmed(p);
        d.Branches[1].Index = d.Branches[0].Index;
        Assert.Throws<InvalidOperationException>(() => _service.Apply(p, d));
        d.Branches[1].Index = 2;
        d.ConnectionIds.RemoveAll(id => id.StartsWith("wire3-", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => _service.Apply(p, d));
    }

    private MultiEndCableDraft Confirmed(ElectricalProject p)
    {
        var d = _service.PrepareNew(p, p.Connections.Select(c => c.ConnectionId).ToArray());
        _service.ConfirmEnds(p, d, "common");
        d.ConstructionType = CableConstructionType.Custom;
        return d;
    }

    internal static ElectricalProject Fixture(int branches)
    {
        var p = new ElectricalProject { ProjectId = "multi-end" };
        for (var b = 0; b <= branches; b++)
        {
            var port = new ComponentPort { PortId = b == 0 ? "common" : $"branch{b}", Name = b == 0 ? "ETH" : "M12" };
            for (var n = 1; n <= 6; n++)
                port.Pins.Add(new ComponentPin { PinId = $"contact-{b}-{n}", PinNumber = n.ToString() });
            p.Components.Add(new ComponentInstance { ComponentInstanceId = $"device{b}", ComponentDefinitionId = "fixture", TypeKey = "connector", ReferenceDesignator = $"X{b}", Ports = [port] });
            if (b == 0) continue;
            for (var n = 1; n <= b; n++)
                p.Connections.Add(new ElectricalConnection { ConnectionId = $"wire{b}-{n}", FromEndpointId = $"contact-0-{n}", ToEndpointId = $"contact-{b}-{n}", NetId = $"net{n}" });
        }
        return p;
    }

    private static string Truth(ElectricalProject p) => JsonSerializer.Serialize(p.Connections.Select(c =>
        new { c.ConnectionId, c.FromEndpointId, c.ToEndpointId, c.NetId, c.CableCoreId, c.ConductorAreaMm2, c.Kind }));
}
