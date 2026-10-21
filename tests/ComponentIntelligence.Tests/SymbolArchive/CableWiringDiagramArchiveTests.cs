using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using ComponentIntelligence.Tests.Electrical;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class CableWiringDiagramArchiveTests
{
    [Fact]
    public void NewInventoryAndDiagramRequireV7AndRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "ci-wiring-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new SymbolArchiveRepository(root);
            var template = CableManufacturingEditorTests.Template(3) with { AssetSha256 = new('0', 64) };
            var tables = new CableManufacturingEditorService(); var draft = tables.Prepare(template);
            var editor = new CableWiringEditorService(); editor.EnterVisualMode(draft);
            editor.Connect(draft, "p1.1", "p3.2"); editor.MovePort(draft, "P3", new(40, 180));
            var saved = tables.Apply(template, draft);
            repository.Save(new() { CableTemplates = [new() { Template = saved, Status = SymbolRevisionStatus.Candidate, WiringAssetPending = true, AssetPath = "", MillimetresPerUnit = 1 }] });
            var before = File.ReadAllBytes(repository.ArchivePath);
            var loaded = repository.Load();
            Assert.Equal("ci-symbol-archive.v7", loaded.SchemaVersion);
            Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(loaded.CableTemplates.Single().Template));
            Assert.Equal(before, File.ReadAllBytes(repository.ArchivePath));
            var instance = ArchivedCableInstanceFactory.Create(loaded.CableTemplates.Single().Template, CableConstructionType.Unknown);
            var reopened = tables.Prepare(JsonSerializer.Deserialize<CableInstance>(JsonSerializer.Serialize(instance))!);
            Assert.Equal(saved.Mapping, reopened.ExplicitMapping);
            Assert.Equal(saved.Manufacturing!.WiringDiagram!.PortBounds["P3"], reopened.WiringDiagram!.PortBounds["P3"]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyV6DoesNotEnterVisualModeOrRewriteOnLoad()
    {
        var template = CableManufacturingEditorTests.Template() with { AssetSha256 = new('0', 64) };
        var service = new CableManufacturingEditorService(); var draft = service.Prepare(template);
        draft.Rows[0].ToSourcePinId = "p2.1";
        var entry = new CableArchiveEntry { Template = service.Apply(template, draft), Status = SymbolRevisionStatus.Candidate, WiringAssetPending = true, AssetPath = "", MillimetresPerUnit = 1 };
        var repository = new SymbolArchiveRepository(Path.GetTempPath());
        var normalized = repository.ValidateAndNormalize(new() { SchemaVersion = "ci-symbol-archive.v6", CableTemplates = [entry] });
        Assert.Equal("ci-symbol-archive.v6", normalized.SchemaVersion);
        Assert.False(service.Prepare(normalized.CableTemplates.Single().Template).InventoryOnly);
        Assert.Equal(entry.Template.Mapping, normalized.CableTemplates.Single().Template.Mapping);
    }

    [Fact]
    public void DiagramRoutesMustNameRealMappedPinsAndRetainTheirEndpoints()
    {
        var template = CableManufacturingEditorTests.Template();
        var service = new CableManufacturingEditorService(); var draft = service.Prepare(template);
        var editor = new CableWiringEditorService(); editor.EnterVisualMode(draft);
        editor.Connect(draft, "p1.1", "p2.1");
        draft.WiringDiagram!.Routes[CableWiringEditorService.EdgeKey("p1.1", "p2.1")] = [new(1, 2), new(1, 4)];
        Assert.Throws<InvalidOperationException>(() => service.Apply(template, draft));
    }
}
