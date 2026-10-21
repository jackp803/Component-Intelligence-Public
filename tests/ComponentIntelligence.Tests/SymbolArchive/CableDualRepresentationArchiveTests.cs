using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class CableDualRepresentationArchiveTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task LegacyArchiveLoadsWithoutManufacturingAndDoesNotWrite()
    {
        using var files = new ArchiveFiles();
        var entry = files.Entry(includeManufacturing: false);
        files.Repository.Save(new() { CableTemplates = [entry] });
        var original = File.ReadAllBytes(files.Repository.ArchivePath);
        var resolved = await new CableArchiveResolver(files.Repository).ResolveAsync("cable", "r1");
        Assert.Equal(files.WiringPath, resolved.AbsolutePath);
        Assert.Equal(original, File.ReadAllBytes(files.Repository.ArchivePath));
        Assert.Equal("ci-symbol-archive.v3", files.Repository.Load().SchemaVersion);
    }

    [Fact]
    public async Task DualAssetsRoundTripWithPinnedHashes()
    {
        using var files = new ArchiveFiles();
        files.Repository.Save(new() { CableTemplates = [files.Entry()] });
        var loaded = files.Repository.Load();
        var saved = JsonSerializer.SerializeToNode(Assert.Single(loaded.CableTemplates), Json)!;
        Assert.Equal(files.DetailHash.ToLowerInvariant(), saved["manufacturingAsset"]?["sourceSha256"]?.GetValue<string>());
        Assert.Equal("detail.dxf", saved["manufacturingAsset"]?["assetPath"]?.GetValue<string>());
        Assert.Equal("ci-symbol-archive.v5", loaded.SchemaVersion);
        var resolved = JsonSerializer.SerializeToNode(await new CableArchiveResolver(files.Repository).ResolveAsync("cable", "r1"), Json)!;
        Assert.Equal(files.DetailPath, resolved["manufacturingPath"]?.GetValue<string>());
        Assert.Equal("manufacturing", File.ReadAllText(files.DetailPath));
    }

    [Theory]
    [InlineData("../detail.dxf")]
    [InlineData("C:/outside/detail.dxf")]
    public void TraversalAssetsRejected(string path)
    {
        using var files = new ArchiveFiles();
        Assert.Throws<InvalidDataException>(() => files.Repository.Save(new() { CableTemplates = [files.Entry(path)] }));
        Assert.False(File.Exists(files.Repository.ArchivePath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidManufacturingUnitsRejected(double units)
    {
        using var files = new ArchiveFiles();
        Assert.Throws<InvalidDataException>(() => files.Repository.Save(new() { CableTemplates = [files.Entry(units: units)] }));
    }

    [Fact]
    public async Task ChangedManufacturingAssetFailsResolution()
    {
        using var files = new ArchiveFiles();
        files.Repository.Save(new() { CableTemplates = [files.Entry()] });
        var before = File.ReadAllBytes(files.Repository.ArchivePath);
        File.WriteAllText(files.DetailPath, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => new CableArchiveResolver(files.Repository).ResolveAsync("cable", "r1"));
        Assert.Equal(before, File.ReadAllBytes(files.Repository.ArchivePath));
    }

    [Fact]
    public async Task MissingManufacturingAssetFailsResolution()
    {
        using var files = new ArchiveFiles();
        files.Repository.Save(new() { CableTemplates = [files.Entry()] });
        File.Delete(files.DetailPath);
        await Assert.ThrowsAsync<FileNotFoundException>(() => new CableArchiveResolver(files.Repository).ResolveAsync("cable", "r1"));
    }

    private sealed class ArchiveFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "dual-cable-" + Guid.NewGuid().ToString("N"));
        public SymbolArchiveRepository Repository { get; }
        public string WiringPath => Path.Combine(_root, "wiring.dxf");
        public string DetailPath => Path.Combine(_root, "detail.dxf");
        public string DetailHash => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DetailPath)));

        public ArchiveFiles()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(WiringPath, "wiring");
            File.WriteAllText(DetailPath, "manufacturing");
            Repository = new(_root);
        }

        public CableArchiveEntry Entry(string path = "detail.dxf", double units = 1, bool includeManufacturing = true)
        {
            var entry = new CableArchiveEntry
            {
                AssetPath = "wiring.dxf", MillimetresPerUnit = 1,
                Template = new()
                {
                    TemplateId = "cable", TemplateRevision = "r1",
                    AssetSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(WiringPath))),
                    Ports = [new() { PortId = "P1", Name = "P1", Pins = [new() { PinId = "p1.1", PinNumber = "1" }] }]
                }
            };
            if (!includeManufacturing) return entry;
            var node = JsonSerializer.SerializeToNode(entry, Json)!;
            node["manufacturingAsset"] = new JsonObject
            {
                ["assetPath"] = path, ["sourceSha256"] = DetailHash, ["millimetresPerUnit"] = units
            };
            return node.Deserialize<CableArchiveEntry>(Json)!;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
