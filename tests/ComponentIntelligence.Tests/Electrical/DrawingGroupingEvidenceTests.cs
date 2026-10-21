using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using System.Text.Json;
using ProjectPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingGroupingEvidenceTests
{
    [Fact]
    public void IndependentPortsOnOneControllerAreNotSilentlyOneBus()
    {
        var p = Project("RS485");
        p.Components[0].Ports.Add(new() { PortId = "A:OTHER", Name = "OTHER", Protocol = "RS485" });
        p.Components.Add(Device("C", "RS485"));
        p.Connections.Add(new() { ConnectionId = "other-link", FromEndpointId = "A:OTHER", ToEndpointId = "C:P" });
        var input = Builder([]).Build(p);
        Assert.Equal(2, input.Networks.Count);
        Assert.All(input.Networks, n => Assert.Single(n.ConnectionIds));
        Assert.Null(input.Representations.Single(r => r.OwnerId == "A").NetworkId);
        Assert.Empty(p.Buses);
    }

    [Fact]
    public void TwoMastersDoNotCauseArbitraryModuleOwnership()
    {
        var p = Project("IO-Link");
        p.Components.Add(Device("C", "IO-Link"));
        p.Connections.Add(new() { ConnectionId = "second-master", FromEndpointId = "C:P", ToEndpointId = "B:P" });
        var input = Builder([Catalog("A", "IO-Link Master"), Catalog("C", "IO-Link Master")]).Build(p);
        Assert.Null(input.Representations.Single(r => r.OwnerId == "B").PhysicalModuleId);
        Assert.Contains(input.Issues, i => i.Code == "DRAWING_MODULE_OWNERSHIP_AMBIGUOUS");
    }

    [Fact]
    public void CatalogClassificationAndExactProtocolConnectionPreserveModuleOwnership()
    {
        var p = Project("IO-Link");
        p.Cables.Add(new() { CableInstanceId = "cable", CableDefinitionId = "catalog-cable", CableConstructionType = CableConstructionType.Purchased });
        p.Connections[0].CableInstanceId = "cable";
        var before = JsonSerializer.Serialize(p);
        var input = Builder([Catalog("A", "IO-Link Master"), Catalog("B", "Sensor")]).Build(p);
        var module = Assert.Single(input.ControllerModules);
        Assert.Equal("A", module.ControllerId);
        Assert.Equal("A", module.PhysicalModuleId);
        Assert.Contains("REP:B:Schematic", module.RepresentationIds);
        Assert.All(input.Representations, r => Assert.Equal("A", r.ControllerId));
        Assert.Equal("A", Assert.Single(input.Cables).SourcePhysicalModuleId);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        var roundTrip = DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(input));
        Assert.Equal("A", Assert.Single(roundTrip.ControllerModules).PhysicalModuleId);
        Assert.Equal("link", Assert.Single(roundTrip.Connections).ConnectionId);
    }

    [Fact]
    public void ExplicitPowerAndNetworkMembershipSurviveWithoutCreatingUnknownDomainIds()
    {
        var p = Project("Ethernet");
        p.Components[0].Ports[0].PowerDomainId = "exact-domain";
        p.Buses.Add(new() { BusId = "exact-bus", Protocol = "Ethernet", NetIds = ["net"] });
        p.Connections[0].NetId = "net";
        var input = Builder([]).Build(p);
        Assert.Equal(new[] { "REP:A:Schematic" }, Assert.Single(input.PowerDomains).RepresentationIds);
        Assert.Equal(new[] { "REP:A:Schematic", "REP:B:Schematic" }, Assert.Single(input.Networks).RepresentationIds);
        Assert.All(input.Representations, r => Assert.Equal("exact-bus", r.NetworkId));
        Assert.Equal("exact-domain", Assert.Single(input.PowerDomains).PowerDomainId);
        Assert.Empty(input.ControllerModules);
    }

    [Fact]
    public void ProtocolOnlyGroupingNeverCreatesElectricalBusOrBond()
    {
        var p = Project("Ethernet"); var before = JsonSerializer.Serialize(p);
        var input = Builder([]).Build(p);
        Assert.StartsWith("LAYOUT-NETWORK:", Assert.Single(input.Networks).NetworkId);
        Assert.Equal("Ethernet", Assert.Single(input.Networks).NetworkKind);
        Assert.Null(Assert.Single(input.Connections).NetId);
        Assert.Empty(input.PowerDomains);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void NamesAndConnectorFamilyDoNotCreateRolesOrOwnership()
    {
        var p = Project(null);
        p.Components[0].DisplayName = "IO-Link Master DC-DC Converter";
        p.Components[0].TypeKey = "PLC";
        p.Components[0].Ports[0].Connector = new() { ConnectorId = "m12", Family = "M12" };
        var input = Builder([]).Build(p);
        Assert.Empty(input.ControllerModules); Assert.Empty(input.Networks); Assert.Empty(input.PowerDomains);
        Assert.All(input.Representations, r => { Assert.Null(r.ControllerId); Assert.Null(r.FunctionKind); });
    }

    private static DrawingPlanningInputBuilder Builder(ComponentIR[] catalog) => new(new RepresentationPolicy(new NoAssets()), catalog);
    private static ComponentIR Catalog(string id, string category) => new()
    {
        Identity = new() { ComponentId = id, Manufacturer = "Test", Model = "Test" }, Classification = new() { Category = category }
    };
    private static ElectricalProject Project(string? protocol) => new()
    {
        ProjectId = "synthetic", Components = [Device("A", protocol), Device("B", protocol)],
        Connections = [new() { ConnectionId = "link", FromEndpointId = "A:P", ToEndpointId = "B:P" }]
    };
    private static ComponentInstance Device(string id, string? protocol) => new()
    {
        ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = "Unknown",
        Ports = [new ProjectPort { PortId = id + ":P", Name = "P", Protocol = protocol }]
    };
    private sealed class NoAssets : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => null;
    }
}
