using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using netDxf;
using netDxf.Entities;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class CableArchiveCreationTests
{
    [Fact]
    public async Task ExplicitCandidateUpdateKeepsOneRevisionAndStaleUpdatesAreAtomic()
    {
        using var files = new Files();
        var service = new CableArchiveCreationService(files.Repository);
        await service.CreateAsync(files.Draft());
        var basis = files.Draft();
        var draft = basis with { UpdateExistingCandidate = true, ExpectedArchiveSha256 = files.Repository.GetContentHash(),
            Entry = basis.Entry with { Template = basis.Entry.Template with { DisplayName = "updated candidate" } } };
        var entry = await service.CreateAsync(draft);
        Assert.Equal("updated candidate", Assert.Single(files.Repository.Load().CableTemplates).Template.DisplayName);
        Assert.Equal("r1", entry.Template.TemplateRevision);
        var before = File.ReadAllBytes(files.Repository.ArchivePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync(draft));
        Assert.Equal(before, File.ReadAllBytes(files.Repository.ArchivePath));
    }

    [Fact]
    public async Task ExplicitUpdateCannotReplaceApprovedRevision()
    {
        using var files = new Files(); var service = new CableArchiveCreationService(files.Repository);
        var entry = await service.CreateAsync(files.Draft());
        files.Repository.Save(files.Repository.Load() with { CableTemplates = [entry with { Status = SymbolRevisionStatus.Approved }] });
        var before = File.ReadAllBytes(files.Repository.ArchivePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync(files.Draft() with {
            UpdateExistingCandidate = true, ExpectedArchiveSha256 = files.Repository.GetContentHash() }));
        Assert.Equal(before, File.ReadAllBytes(files.Repository.ArchivePath));
    }

    [Fact]
    public async Task MissingWiringCadCanSaveCandidateButCannotBePlaced()
    {
        using var files = new Files();
        var draft = files.Draft();
        draft = draft with { WiringSourcePath = null, WiringGeometry = null,
            Entry = draft.Entry with { WiringAssetPending = true, AssetPath = "",
                Template = draft.Entry.Template with { AssetSha256 = new('0', 64) } } };
        var entry = await new CableArchiveCreationService(files.Repository).CreateAsync(draft);
        Assert.True(entry.WiringAssetPending);
        Assert.Null(entry.WiringGeometry);
        Assert.Equal("", entry.AssetPath);
        Assert.NotNull(entry.ManufacturingAsset);
        await Assert.ThrowsAsync<InvalidDataException>(() => new CableArchiveResolver(files.Repository).ResolveAsync("cable", "r1"));
    }

    [Fact]
    public void MissingWiringCannotBeApprovedOrCarryFakeBindings()
    {
        using var files = new Files();
        var entry = files.Draft().Entry with { WiringAssetPending = true, AssetPath = "",
            Status = SymbolRevisionStatus.Approved };
        Assert.Throws<InvalidDataException>(() => files.Repository.Save(new() { CableTemplates = [entry] }));
    }

    [Fact]
    public async Task TwoFilesCreateOneTemplateWithoutTouchingSources()
    {
        using var files = new Files();
        var wiring = File.ReadAllBytes(files.Wiring);
        var sketch = File.ReadAllBytes(files.Sketch);
        var service = new CableArchiveCreationService(files.Repository);
        var entry = await service.CreateAsync(files.Draft());
        Assert.Single(files.Repository.Load().CableTemplates);
        var resolved = await new CableArchiveResolver(files.Repository).ResolveAsync("cable", "r1");
        Assert.Equal(wiring, File.ReadAllBytes(resolved.AbsolutePath));
        Assert.Equal(sketch, File.ReadAllBytes(resolved.ManufacturingPath!));
        Assert.Equal(wiring, File.ReadAllBytes(files.Wiring));
        Assert.Equal(sketch, File.ReadAllBytes(files.Sketch));
        Assert.NotEqual(files.Wiring, resolved.AbsolutePath);
        Assert.NotNull(entry.ManufacturingAsset);
    }

    [Fact]
    public async Task CancelledOrFailedSaveLeavesArchiveUnchanged()
    {
        using var files = new Files();
        var service = new CableArchiveCreationService(files.Repository);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(files.Draft(), new CancellationToken(true)));
        Assert.False(File.Exists(files.Repository.ArchivePath));
        File.AppendAllText(files.Sketch, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync(files.Draft()));
        Assert.False(File.Exists(files.Repository.ArchivePath));
        Assert.Empty(Directory.GetFiles(files.Archive, "*.dxf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExistingRevisionAndConcurrentArchiveChangesCannotBeOverwritten()
    {
        using var files = new Files();
        var service = new CableArchiveCreationService(files.Repository);
        var old = files.Draft();
        await service.CreateAsync(old);
        var original = File.ReadAllBytes(files.Repository.ArchivePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CreateAsync(old));
        Assert.Equal(original, File.ReadAllBytes(files.Repository.ArchivePath));
    }

    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cable-import-" + Guid.NewGuid().ToString("N"));
        public string Archive => Path.Combine(_root, "archive");
        public string Wiring => Path.Combine(_root, "wiring.dxf");
        public string Sketch => Path.Combine(_root, "sketch.dxf");
        public SymbolArchiveRepository Repository { get; }
        private readonly SchematicCadAsset _wire;
        private readonly SchematicCadAsset _sketch;

        public Files()
        {
            Directory.CreateDirectory(_root); Directory.CreateDirectory(Archive);
            foreach (var path in new[] { Wiring, Sketch })
            {
                var doc = new DxfDocument(); doc.Entities.Add(new Line(Vector2.Zero, new Vector2(path == Wiring ? 20 : 40, 10))); doc.Save(path);
            }
            _wire = new SchematicCadImporter().ReadDxf(Wiring, 1);
            _sketch = new SchematicCadImporter().ReadDxf(Sketch, 1);
            Repository = new(Archive);
        }

        public CableArchiveDraft Draft() => new()
        {
            WiringSourcePath = Wiring, WiringGeometry = _wire,
            ManufacturingSourcePath = Sketch, ManufacturingGeometry = _sketch,
            Entry = new()
            {
                AssetPath = "wiring.dxf", MillimetresPerUnit = 1,
                ManufacturingAsset = new() { AssetPath = "sketch.dxf", MillimetresPerUnit = 1, SourceSha256 = _sketch.SourceSha256 },
                Template = new() { TemplateId = "cable", TemplateRevision = "r1", AssetSha256 = _wire.SourceSha256,
                    Ports = [new() { PortId = "P1", Name = "P1", Pins = [new() { PinId = "p1.1", PinNumber = "1" }] }] }
            }
        };

        public void Dispose() => Directory.Delete(_root, true);
    }
}
