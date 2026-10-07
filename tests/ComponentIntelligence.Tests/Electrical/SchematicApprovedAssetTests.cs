using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicApprovedAssetTests
{
    private readonly SchematicAuthoringService _service = new();
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void AppliesExactSourceBindingAndPinsRevisionWithoutChangingEngineering()
    {
        var project = Project(); var before = JsonSerializer.Serialize(project);
        var next = _service.SetApprovedSymbolGeometry(project, "S", "CAT", Asset(), Geometry());
        var symbol = Assert.Single(next.Schematic!.Symbols);
        Assert.Equal("rev-001", symbol.AssetRevision); Assert.Equal(Hash, symbol.AssetSha256);
        var anchor = Assert.Single(symbol.Anchors);
        Assert.Equal("runtime-pin", anchor.EndpointId); Assert.Equal("catalog-pin", anchor.SourcePinId);
        Assert.Equal(new SchematicPoint(12, 7), anchor.Position); Assert.True(anchor.Confirmed);
        Assert.Equal("Bottom", anchor.Direction);
        Assert.Equal(JsonSerializer.Serialize(project.Components), JsonSerializer.Serialize(next.Components));
        Assert.Empty(next.Connections); Assert.Equal(before, JsonSerializer.Serialize(project));
        var reload = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        SchematicAuthoringService.Validate(reload);
        Assert.Equal(symbol.AssetRevision, reload.Schematic!.Symbols[0].AssetRevision);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("component")]
    [InlineData("source")]
    [InlineData("point")]
    [InlineData("duplicate")]
    [InlineData("generated")]
    [InlineData("locked")]
    public void RejectsUnprovenMappingWithoutMutation(string fault)
    {
        var project = Project(); var asset = Asset(); var geometry = Geometry(); var component = "CAT";
        if (fault == "hash") geometry = geometry with { SourceSha256 = new string('B', 64) };
        if (fault == "component") component = "OTHER";
        if (fault == "source") asset = asset with { PortBindings = [new() { EngineeringEndpointId = "1", ConnectionPointId = "X1TERM01" }] };
        if (fault == "point") geometry = geometry with { ConnectionPoints = [] };
        if (fault == "duplicate") geometry = geometry with { ConnectionPoints = [geometry.ConnectionPoints[0], geometry.ConnectionPoints[0]] };
        if (fault == "generated") asset = asset with { SourceType = "GeneratedGeneric" };
        if (fault == "locked") project = _service.SetLocked(project, "S", true);
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => _service.SetApprovedSymbolGeometry(project, "S", component, asset, geometry));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void AnotherApprovedRepresentationSharesPhysicalIdentityAndPersistsExactVariant()
    {
        var project = Project();
        var before = JsonSerializer.Serialize(project);
        var next = _service.PlaceApprovedRepresentation(project, "C", project.Schematic!.Pages[0].PageId,
            new(100, 100), Asset() with { ArchiveRepresentationId = "coil" }, Geometry());
        Assert.Single(next.Components);
        Assert.Equal(2, next.Schematic!.Symbols.Count);
        var placed = next.Schematic.Symbols.Single(s => s.SymbolId != "S");
        Assert.Equal("C", placed.ComponentInstanceId);
        Assert.Equal("coil", placed.ArchiveRepresentationId);
        Assert.All(placed.Anchors, a => Assert.True(a.Confirmed));
        Assert.Empty(next.Connections);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var reload = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        Assert.Equal("coil", reload.Schematic!.Symbols.Single(s => s.SymbolId == placed.SymbolId).ArchiveRepresentationId);
    }

    [Fact]
    public void ManualAnchorOverrideRetiresApprovedBindingClaimButReferenceChangeDoesNot()
    {
        var project = _service.SetApprovedSymbolGeometry(Project(), "S", "CAT", Asset(), Geometry());
        var symbol = project.Schematic!.Symbols[0];
        var renamed = _service.SetSymbolDetails(project, "S", "TEST1", symbol.Anchors);
        Assert.Equal("rev-001", renamed.Schematic!.Symbols[0].AssetRevision);
        var edited = _service.SetSymbolDetails(renamed, "S", "TEST1", [symbol.Anchors[0] with { Position = new(15, 8) }]);
        Assert.Null(edited.Schematic!.Symbols[0].AssetRevision);
        Assert.Equal(Hash, edited.Schematic.Symbols[0].AssetSha256);
        Assert.Equal("rev-001", project.Schematic.Symbols[0].AssetRevision);
    }

    private static DrawingAssetResolution Asset() => new()
    {
        SourceType = "ApprovedCustom", Revision = "rev-001", AssetPath = "test/symbol.dxf", AssetHashSha256 = Hash,
        PortBindings = [new() { EngineeringEndpointId = "catalog-pin", ConnectionPointId = "X1TERM01" }]
    };
    private static SchematicCadAsset Geometry() => new()
    {
        SourceSha256 = Hash, MillimetresPerUnit = 1, Width = 30, Height = 20,
        ConnectionPoints = [new("X1TERM01", "1", new(12, 7), Direction: "Bottom")],
        Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(30, 20) }]
    };
    private ElectricalProject Project()
    {
        var p = _service.AddPage(new() { ProjectId = "test", Components = [new()
        {
            ComponentInstanceId = "C", ComponentDefinitionId = "CAT", TypeKey = "TEST",
            Ports = [new() { PortId = "runtime-port", SourcePortId = "catalog-port", Name = "P",
                Pins = [new() { PinId = "runtime-pin", SourcePinId = "catalog-pin", PinNumber = "1" }] }]
        }] }, "One");
        return _service.PlaceSymbol(p, SchematicAuthoringService.CreateSymbol(p.Components[0], p.Schematic!.Pages[0].PageId, new(20, 20)) with { SymbolId = "S" });
    }
}
