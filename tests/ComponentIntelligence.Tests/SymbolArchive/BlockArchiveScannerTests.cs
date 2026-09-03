using ComponentIntelligence.Cache;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class BlockArchiveScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-block-scan-{Guid.NewGuid():N}");
    public BlockArchiveScannerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Sha256FileAsync_StreamsStableLowercaseHash()
    {
        var path = Path.Combine(_root, "large.dwg");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 300000).Select(i => (byte)(i % 251)).ToArray());
        var hash = await HashService.Sha256FileAsync(path);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Equal(HashService.Sha256(await File.ReadAllBytesAsync(path)), hash);
    }

    [Fact]
    public async Task Scan_IsRecursiveCadOnlyDeterministicAndReadOnly()
    {
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        await File.WriteAllTextAsync(Path.Combine(_root, "b", "Z.DWG"), "z");
        await File.WriteAllTextAsync(Path.Combine(_root, "a", "a.dxf"), "a");
        await File.WriteAllTextAsync(Path.Combine(_root, "ignore.txt"), "x");
        var before = await HashMapAsync(_root);

        var rows = await new BlockArchiveScanner().ScanAsync(_root, new SymbolArchiveDocument());

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a/a.dxf", "b/Z.DWG" }, rows.Select(row => row.RelativePath));
        Assert.All(rows, row => Assert.True(row.Extension is ".dwg" or ".dxf"));
        var after = await HashMapAsync(_root);
        Assert.Equal(before.Count, after.Count);
        foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
    }

    [Fact]
    public async Task ExistingArchiveHash_IsDuplicateHintOnly()
    {
        var path = Path.Combine(_root, "same.dwg");
        await File.WriteAllTextAsync(path, "same");
        var sha = await HashService.Sha256FileAsync(path);
        var archive = new SymbolArchiveDocument
        {
            Bindings = [new ComponentSymbolBinding
            {
                ComponentId = "C1", Role = SymbolRole.Schematic,
                Revisions = [new SymbolRevisionRecord
                {
                    Revision = "rev-001", SourceType = SymbolSourceType.Manufacturer,
                    AssetPath = "Documents/M/M/autocad/schematic/rev-001/symbol.dwg", AssetHashSha256 = sha,
                    Status = SymbolRevisionStatus.Approved
                }]
            }]
        };
        var row = Assert.Single(await new BlockArchiveScanner().ScanAsync(_root, archive));
        Assert.Equal("C1:Schematic:rev-001", row.ExactDuplicateRevision);
        Assert.Equal(DeepInspectionStatus.NotRequested, row.DeepInspectionStatus);
        Assert.Null(row.DeepMetadata);
    }

    [Fact]
    public async Task ReparsePointLoop_IsSkippedWhenPlatformAllowsCreation()
    {
        var target = Path.Combine(_root, "real");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "asset.dwg"), "asset");
        try { Directory.CreateSymbolicLink(Path.Combine(target, "loop"), _root); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        var rows = await new BlockArchiveScanner().ScanAsync(_root, new SymbolArchiveDocument());
        Assert.Single(rows);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static async Task<IReadOnlyDictionary<string, string>> HashMapAsync(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            result[Path.GetRelativePath(root, path).Replace('\\', '/')] = await HashService.Sha256FileAsync(path);
        }
        return result;
    }
}
