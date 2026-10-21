using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCableDetailTests
{
    [Fact]
    public void DetailPageReferencesExistingCableWithoutDuplicatingEngineeringTruth()
    {
        var p = Project(2);
        p.Components[1].Ports[0].Pins.Add(p.Components[2].Ports[0].Pins[0]);
        p.Components[2].Ports[0].Pins.Clear();
        var before = JsonSerializer.Serialize(p);
        var service = new SchematicCableDetailService();
        var next = service.AddPage(p, "wire-1");
        var page = Assert.Single(next.Schematic!.Pages);
        Assert.Equal("cable", page.CableDetail!.CableInstanceId);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        Assert.Equal(JsonSerializer.Serialize(p.Connections), JsonSerializer.Serialize(next.Connections));
        Assert.Single(next.Cables);
        var detail = service.Build(next, page);
        Assert.Contains(detail.Primitives, x => x.Text?.Contains("CBL-01") == true);
        Assert.Contains(detail.Primitives, x => x.Text?.Contains("1200 mm") == true);
        Assert.Equal(2, detail.ConnectionIds.Count);
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        reloaded.Cables[0].ProvidedLengthMm = 1350;
        Assert.Contains(service.Build(reloaded, reloaded.Schematic!.Pages[0]).Primitives, x => x.Text?.Contains("1350 mm") == true);
        Assert.Single(service.AddPage(reloaded, "wire-2").Schematic!.Pages);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void PhysicalBranchesDoNotRewriteDaisyChainElectricalGraph(int branches)
    {
        var p = Project(branches);
        p.CableAssemblies.Add(new() { CableAssemblyId = "assembly", ReferenceDesignator = "Y-01",
            CableConstructionType = CableConstructionType.Custom,
            PhysicalTopology = new() { CableInstanceId = "cable", CommonPortId = "port-0",
                Branches = Enumerable.Range(1, branches).Select(i => new MultiEndCableBranch { PortId = $"port-{i}", Index = i == 2 ? 3 : i == 3 ? 5 : 1 }).ToList(),
                ConnectionIds = p.Connections.Select(c => c.ConnectionId).ToList() } });
        var before = JsonSerializer.Serialize(p.Connections);
        var service = new SchematicCableDetailService(); var next = service.AddPage(p, "wire-1");
        var detail = service.Build(next, next.Schematic!.Pages[0]);
        Assert.Equal(before, JsonSerializer.Serialize(next.Connections));
        Assert.Equal(branches + 1, next.Components.Count);
        Assert.Empty(next.Schematic.Symbols); Assert.Empty(next.Schematic.Wires);
        Assert.Contains(detail.Primitives, x => x.Text?.Contains("Branch 3") == true);
        Assert.Equal(branches, detail.ConnectionIds.Count);
    }

    [Fact]
    public void OrdinaryWireDoesNotBecomeCableAndMissingOwnershipFailsClosed()
    {
        var p = Project(1); p.Connections[0].CableInstanceId = null;
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => new SchematicCableDetailService().AddPage(p, "wire-1"));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void DxfDetailIsTheSamePresentationAndDoesNotEmitElectricalWireEntities()
    {
        var service = new SchematicCableDetailService(); var p = service.AddPage(Project(1), "wire-1");
        var display = service.Build(p, p.Schematic!.Pages[0]);
        var sheet = Assert.Single(new SchematicDxfExporter().Create(p));
        using var stream = new MemoryStream(sheet.Dxf);
        var dxf = netDxf.DxfDocument.Load(stream);
        Assert.Equal(display.Primitives.Count, dxf.Entities.All.Count(e => e.Layer.Name == "SCHEMATIC_CABLE_DETAIL"));
        Assert.DoesNotContain(dxf.Entities.All, e => e.Layer.Name == "SCHEMATIC_WIRE");
        Assert.All(display.Primitives.Where(p => p.Kind == "TEXT"), p => Assert.Contains(dxf.Entities.Texts, t => t.Value == p.Text));
    }

    [Fact]
    public void MissingAndMalformedAuthorityCannotBeSilentlyRendered()
    {
        var service = new SchematicCableDetailService(); var p = service.AddPage(Project(1), "wire-1");
        p.Cables.Clear();
        Assert.Throws<InvalidOperationException>(() => service.Build(p, p.Schematic!.Pages[0]));
        Assert.Throws<InvalidOperationException>(() => new SchematicDxfExporter().Create(p));
        var large = Project(1);
        large.Components[0].ReferenceDesignator = new string('X', 2000);
        var before = JsonSerializer.Serialize(large);
        Assert.Throws<InvalidOperationException>(() => service.AddPage(large, "wire-1"));
        Assert.Equal(before, JsonSerializer.Serialize(large));
    }

    private static ElectricalProject Project(int count) => new()
    {
        ProjectId = "test",
        Components = Enumerable.Range(0, count + 1).Select(i => new ComponentInstance {
            ComponentInstanceId = $"cmp-{i}", ComponentDefinitionId = $"model-{i}", TypeKey = "TEST", ReferenceDesignator = $"X{i + 1}",
            Ports = [new() { PortId = $"port-{i}", Name = i == 0 ? "ETH" : "M12", Pins = [new() { PinId = $"pin-{i}", PinNumber = "1", PinName = "Signal" }] }]
        }).ToList(),
        Cables = [new() { CableInstanceId = "cable", CableDefinitionId = "custom", ReferenceDesignator = "CBL-01", CableConstructionType = CableConstructionType.Custom,
            ProvidedLengthMm = 1200, LengthSource = CableLengthSource.User }],
        Connections = Enumerable.Range(1, count).Select(i => new ElectricalConnection {
            ConnectionId = $"wire-{i}", FromEndpointId = $"pin-{i - 1}", ToEndpointId = $"pin-{i}", CableInstanceId = "cable", Kind = ConnectionKind.Cable
        }).ToList()
    };
}
