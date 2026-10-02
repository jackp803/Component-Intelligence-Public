using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicRepresentationSplitTests
{
    private readonly SchematicAuthoringService _service = new();

    [Fact]
    public void GenericHeavyDutySplitsAcrossPagesWithoutDuplicatingPhysicalIdentityOrPins()
    {
        var project = Project(16);
        var original = project.Schematic!.Symbols.Single();
        var before = JsonSerializer.Serialize(project);
        var result = _service.SplitGenericRepresentation(project, original.SymbolId, ["P1", "P2", "P2", "P1"]);
        Assert.Single(result.Components);
        Assert.DoesNotContain(result.Schematic!.Symbols, s => s.SymbolId == original.SymbolId);
        Assert.Equal(4, result.Schematic.Symbols.Count);
        Assert.Equal(["P1", "P2", "P2", "P1"], result.Schematic.Symbols.Select(s => s.PageId));
        Assert.Equal([1, 2, 3, 4], result.Schematic.Symbols.Select(s => s.SectionIndex));
        Assert.All(result.Schematic.Symbols, s =>
        {
            Assert.Equal(4, s.SectionCount);
            Assert.Equal(original.ComponentInstanceId, s.ComponentInstanceId);
            Assert.Empty(s.CollapsedPortIds);
            Assert.All(project.Components.Single().Ports, port => Assert.False(SchematicPortPresentation.IsRepresented(s, port)));
            Assert.True(s.Height < original.Height);
        });
        Assert.Equal(original.Anchors.Select(a => a.EndpointId).Order(StringComparer.Ordinal),
            result.Schematic.Symbols.SelectMany(s => s.Anchors).Select(a => a.EndpointId).Order(StringComparer.Ordinal));
        Assert.Equal(original.Anchors.Count, result.Schematic.Symbols.Sum(s => s.Anchors.Count));
        Assert.Equal(project.Components.Single().Ports.SelectMany(p => p.Pins).Select(p => p.SourcePinId).Order(StringComparer.Ordinal),
            result.Components.Single().Ports.SelectMany(p => p.Pins).Select(p => p.SourcePinId).Order(StringComparer.Ordinal));
        Assert.Empty(result.Connections);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var reloaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(result))!;
        SchematicAuthoringService.Validate(reloaded);
        Assert.Equal(4, reloaded.Schematic!.Symbols.Count);
    }

    [Fact]
    public void SplitRejectsWiredLockedOrCadGeometryWithoutMutation()
    {
        var project = Project(8);
        var symbol = project.Schematic!.Symbols.Single();
        var wired = _service.DrawWire(project, "P1", SchematicAttachment.Pin(symbol.SymbolId, symbol.Anchors[0].EndpointId),
            SchematicAttachment.Free(), [SchematicAuthoringService.AnchorPoint(symbol, symbol.Anchors[0].EndpointId), new(80, 45)]);
        var wiredJson = JsonSerializer.Serialize(wired);
        Assert.Throws<InvalidOperationException>(() => _service.SplitGenericRepresentation(wired, symbol.SymbolId, ["P1", "P2"]));
        Assert.Equal(wiredJson, JsonSerializer.Serialize(wired));
        var locked = _service.SetLocked(project, symbol.SymbolId, true);
        Assert.Throws<InvalidOperationException>(() => _service.SplitGenericRepresentation(locked, symbol.SymbolId, ["P1", "P2"]));
        var cad = project.Schematic.Symbols[0] with { Geometry = new SchematicCadAsset { SourceSha256 = new string('A', 64), Width = 50, Height = 100 } };
        project.Schematic.Symbols[0] = cad;
        Assert.Throws<InvalidOperationException>(() => _service.SplitGenericRepresentation(project, symbol.SymbolId, ["P1", "P2"]));
    }

    private static ElectricalProject Project(int rows)
    {
        var component = new ComponentInstance
        {
            ComponentInstanceId = "HD1", ComponentDefinitionId = "HD-64", TypeKey = "HEAVY_DUTY",
            ReferenceDesignator = "-X1", Ports =
            [
                new ComponentPort { PortId = "IN", Name = "Input", PhysicalLocation = new() { Side = "Left" },
                    Pins = Enumerable.Range(1, rows).Select(i => new ComponentPin { PinId = $"IN-{i}", SourcePinId = $"SOURCE-IN-{i}", PinNumber = i.ToString() }).ToList() },
                new ComponentPort { PortId = "OUT", Name = "Output", PhysicalLocation = new() { Side = "Right" },
                    Pins = Enumerable.Range(1, rows).Select(i => new ComponentPin { PinId = $"OUT-{i}", SourcePinId = $"SOURCE-OUT-{i}", PinNumber = i.ToString() }).ToList() }
            ]
        };
        return new ElectricalProject { ProjectId = "SPLIT", Components = [component], Schematic = new()
        {
            Pages = [new() { PageId = "P1", Title = "First" }, new() { PageId = "P2", Title = "Second" }],
            Symbols = [SchematicAuthoringService.CreateSymbol(component, "P1", new(40, 40))]
        } };
    }
}
