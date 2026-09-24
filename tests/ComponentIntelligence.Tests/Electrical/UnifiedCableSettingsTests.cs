using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using System.Text.Json;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class UnifiedCableSettingsTests
{
    [Theory]
    [InlineData(CableConstructionType.Purchased)]
    [InlineData(CableConstructionType.Custom)]
    public void ExplicitChoiceGroupsConductorsWithoutInventingCoreOrChangingEndpoints(CableConstructionType choice)
    {
        var project = Project();
        var original = project.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId, c.NetId, c.CableCoreId)).ToArray();
        var service = new UnifiedCableSettingsService();
        Assert.Equal(2, service.DescribeEnds(project, ["C1", "C2"]).Count);
        var cable = service.ApplyPointToPoint(project, ["C1", "C2"], choice, "CABLE", null, 250, false);
        Assert.Single(project.Cables);
        Assert.Empty(project.CableAssemblies);
        Assert.Empty(cable.CoreAssignments);
        Assert.Equal(choice, cable.CableConstructionType);
        Assert.Equal(CableLengthSource.User, cable.LengthSource);
        Assert.All(project.Connections, c => Assert.Equal(cable.CableInstanceId, c.CableInstanceId));
        Assert.Equal(original, project.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId, c.NetId, c.CableCoreId)).ToArray());
    }

    [Fact]
    public void UnknownAndMultiEndCannotSilentlyBecomePointToPoint()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => service.ApplyPointToPoint(project, ["C1", "C2"], CableConstructionType.Unknown, null, null, null, false));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        project.Components.Add(Component("Z"));
        project.Connections.Add(new() { ConnectionId = "C3", FromEndpointId = "B2", ToEndpointId = "Z1" });
        before = JsonSerializer.Serialize(project);
        Assert.Equal(3, service.DescribeEnds(project, ["C1", "C2", "C3"]).Count);
        Assert.Throws<InvalidOperationException>(() => service.ApplyPointToPoint(project, ["C1", "C2", "C3"], CableConstructionType.Custom, null, null, null, false));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ExistingCableRequiresWholeMembershipAndPreservesLengthProvenance()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        var cable = service.ApplyPointToPoint(project, ["C1", "C2"], CableConstructionType.Purchased, "OLD", null, null, false);
        cable.ProvidedLengthMm = 123;
        cable.LengthSource = CableLengthSource.Mechanical;
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => service.ApplyPointToPoint(project, ["C1"], CableConstructionType.Custom, null, null, null, false));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        service.ApplyPointToPoint(project, ["C1", "C2"], CableConstructionType.Custom, "NEW", null, null, false);
        Assert.Equal(123, cable.ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Mechanical, cable.LengthSource);
    }

    [Fact]
    public void SeparateCableConsolidationNeedsConfirmationAndDoesNotGuessCoreMapping()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        service.ApplyPointToPoint(project, ["C1"], CableConstructionType.Purchased, null, null, null, false);
        service.ApplyPointToPoint(project, ["C2"], CableConstructionType.Custom, null, null, null, false);
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => service.ApplyPointToPoint(project, ["C1", "C2"], CableConstructionType.Custom, null, null, null, false));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var cable = service.ApplyPointToPoint(project, ["C1", "C2"], CableConstructionType.Custom, null, null, null, true);
        Assert.Single(project.Cables);
        Assert.Empty(cable.CoreAssignments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLengthFailsBeforeAnyMutation(double length)
    {
        var project = Project();
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => new UnifiedCableSettingsService().ApplyPointToPoint(
            project, ["C1", "C2"], CableConstructionType.Custom, "NEW", null, length, false));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void OrdinaryWireConversionPreservesElectricalTruthAndUnrelatedCable()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        service.ApplyPointToPoint(project, ["C1"], CableConstructionType.Custom, null, null, null, false);
        var other = service.ApplyPointToPoint(project, ["C2"], CableConstructionType.Purchased, "KEEP", null, null, false);
        var otherBefore = JsonSerializer.Serialize(other);
        var connection = project.Connections[0];
        var truth = (connection.ConnectionId, connection.FromEndpointId, connection.ToEndpointId, connection.NetId);
        service.ApplyOrdinaryWire(project, ["C1"]);
        Assert.Null(connection.CableInstanceId);
        Assert.Equal(ConnectionKind.Wire, connection.Kind);
        Assert.Equal(truth, (connection.ConnectionId, connection.FromEndpointId, connection.ToEndpointId, connection.NetId));
        Assert.Same(other, Assert.Single(project.Cables));
        Assert.Equal(otherBefore, JsonSerializer.Serialize(other));
    }

    [Fact]
    public void CoreEvidenceCannotBeDiscardedByOrdinaryWireConversion()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        service.ApplyPointToPoint(project, ["C1", "C2"], CableConstructionType.Custom, null, null, null, false);
        project.Connections[0].CableCoreId = "explicit-core";
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => service.ApplyOrdinaryWire(project, ["C1", "C2"]));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void DirectMatingAndStaleSelectionCannotBecomeCable()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        project.Connections[0].Kind = ConnectionKind.DirectMating;
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => service.ApplyPointToPoint(project, ["C1"], CableConstructionType.Custom, null, null, null, false));
        Assert.Throws<InvalidOperationException>(() => service.ApplyOrdinaryWire(project, ["MISSING"]));
        Assert.Throws<InvalidOperationException>(() => service.ApplyOrdinaryWire(project, ["C2", "C2"]));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    internal static ComponentInstance Component(string id) => new() {
        ComponentInstanceId = id, ComponentDefinitionId = "notion:opaque-" + id, TypeKey = "Connector", ReferenceDesignator = id,
        Ports = [new() { PortId = id, Name = "Port", Pins = [new() { PinId = id + "1", PinNumber = "1" }, new() { PinId = id + "2", PinNumber = "2" }] }]
    };
    internal static ElectricalProject Project() => new() {
        ProjectId = "TEST", Components = [Component("A"), Component("B")],
        Connections = [new() { ConnectionId = "C1", FromEndpointId = "A1", ToEndpointId = "B1", NetId = "N1" },
                       new() { ConnectionId = "C2", FromEndpointId = "A2", ToEndpointId = "B2", NetId = "N2" }]
    };
}
