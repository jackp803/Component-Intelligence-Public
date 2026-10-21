using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicWireSizeTests
{
    [Fact]
    public void ExplicitAwgSetsAreaAndPersistsWithoutInferringFromVoltage()
    {
        var service = new SchematicAuthoringService();
        var p = service.AddPage(new ElectricalProject { ProjectId = "TEST" }, "Test");
        p = service.DrawWire(p, p.Schematic!.Pages[0].PageId, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(10, 10), new(20, 10)]);
        var id = p.Schematic!.Wires[0].WireId;
        Assert.Null(SchematicWirePresentation.Resolve(p, p.Schematic.Wires[0]).Awg);
        var next = service.SetWireAwg(p, id, 18);
        var restored = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        var style = SchematicWirePresentation.Resolve(restored, restored.Schematic!.Wires[0]);
        Assert.Equal(18, style.Awg); Assert.InRange(style.AreaMm2!.Value, .82, .83);
        Assert.Null(p.Schematic.Wires[0].Awg);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetWireAwg(p, id, 41));
        Assert.Throws<InvalidOperationException>(() => service.SetWireAwg(service.SetLocked(p, id, true), id, 18));
    }

    [Fact]
    public void ArchivedCoreOnlyResolvesByExactCableAndCoreIdentity()
    {
        var p = new ElectricalProject { ProjectId = "TEST" };
        p.Cables.Add(new() { CableInstanceId = "C", CableDefinitionId = "D" });
        p.Connections.Add(new() { ConnectionId = "E", FromEndpointId = "A", ToEndpointId = "B", CableInstanceId = "C", CableCoreId = "CORE" });
        var wire = new SchematicWire { WireId = "W", PageId = "P", ConnectionId = "E" };
        CableDefinition[] definitions = [new() { CableDefinitionId = "D", Cores = [new() { CoreId = "CORE", CoreNumber = "1", Awg = 22, ColorCode = "BN" }] }];
        var resolved = SchematicWirePresentation.Resolve(p, wire, definitions);
        Assert.Equal(22, resolved.Awg); Assert.Equal("BN", resolved.PhysicalColor);
        p.Connections[0].CableCoreId = "MISSING";
        Assert.Null(SchematicWirePresentation.Resolve(p, wire, definitions).Awg);
    }

    [Fact]
    public void PreviewWeightIsBoundedAndLargerConductorIsThicker()
    {
        Assert.InRange(SchematicWirePresentation.StrokeWidthMm(0), .2, 1);
        Assert.True(SchematicWirePresentation.StrokeWidthMm(12) > SchematicWirePresentation.StrokeWidthMm(24));
    }
}
