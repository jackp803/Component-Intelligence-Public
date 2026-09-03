using ClosedXML.Excel;
using ComponentIntelligence.Cache;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Repository;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class Cp3aSymbolArchiveFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-cp3a-flow-{Guid.NewGuid():N}");

    public Cp3aSymbolArchiveFlowTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FullSourceFlowPreservesWorkbookAndSourcesWhileRevisionAuthorityEvolves()
    {
        var workbookPath = Path.Combine(_root, "Component_Intelligence_Database.xlsx");
        CreateWorkbook(workbookPath);
        var workbookHashBefore = await HashService.Sha256FileAsync(workbookPath);

        var sourceRoot = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceRoot);
        var rev1Source = Source(sourceRoot, "MODEL-rev1.dwg", "asset-one");
        var duplicateSource = Source(sourceRoot, "nested/MODEL-duplicate.dxf", "asset-one");
        var rev2Source = Source(sourceRoot, "MODEL-rev2.dwg", "asset-two");
        var panelSource = Source(sourceRoot, "MODEL-panel.dxf", "panel-asset");
        var sourceHashesBefore = await HashMap(sourceRoot);

        var store = new WorkbookComponentKnowledgeStore(workbookPath);
        var components = await store.ListAsync();
        var component = Assert.Single(components);
        Assert.Equal("C1", component.Identity.ComponentId);
        Assert.Equal("P1", Assert.Single(component.Ports).PortId);
        Assert.Equal("PIN-1", Assert.Single(component.Pins).PinId);

        var repository = new SymbolArchiveRepository(workbookPath);
        var scanner = new BlockArchiveScanner();
        var matcher = new SymbolCandidateMatcher();
        var initial = await scanner.ScanAsync(sourceRoot, repository.Load());
        Assert.Equal(4, initial.Count);
        Assert.All(initial, candidate => Assert.Equal(64, candidate.Sha256.Length));
        Assert.All(initial, candidate => Assert.Null(candidate.ExactDuplicateRevision));
        var rev1Candidate = initial.Single(candidate => candidate.SourcePath == rev1Source);
        var suggestions = matcher.Rank(rev1Candidate, components);
        Assert.Equal("C1", Assert.Single(suggestions).ComponentId);

        // Matching is suggestion-only. Authority starts only with explicit Component + Role + SourceType + confirmation.
        var approval = new SymbolArchiveApprovalService(repository, components);
        var rev1 = await approval.ApproveAsync(Request(rev1Source, SymbolRole.Schematic));
        Assert.Equal(SymbolApprovalDisposition.CreatedRevision, rev1.Disposition);
        Assert.Equal("rev-001", rev1.Revision);
        Assert.True(File.Exists(repository.ArchivePath));
        Assert.Equal(workbookHashBefore, await HashService.Sha256FileAsync(workbookPath));

        var duplicateScan = await scanner.ScanAsync(sourceRoot, repository.Load());
        Assert.Equal("C1:Schematic:rev-001", duplicateScan.Single(candidate => candidate.SourcePath == duplicateSource).ExactDuplicateRevision);
        var duplicate = await approval.ApproveAsync(Request(duplicateSource, SymbolRole.Schematic));
        Assert.Equal(SymbolApprovalDisposition.ExactDuplicate, duplicate.Disposition);
        Assert.Equal("rev-001", duplicate.Revision);
        Assert.False(Directory.Exists(Path.Combine(_root, "Documents", "MFR", "MODEL", "autocad", "schematic", "rev-002")));

        var rev1Resolution = await new SymbolResolver(repository, components).ResolveAsync("C1", SymbolRole.Schematic);
        var rev1Manifest = SymbolExportManifest.FromResolutions([rev1Resolution]);
        Assert.Equal("rev-001", Assert.Single(rev1Manifest.Symbols).Revision);
        Assert.Equal(rev1.Sha256, Assert.Single(rev1Manifest.Symbols).Sha256);

        var rev2 = await approval.ApproveAsync(Request(rev2Source, SymbolRole.Schematic));
        Assert.Equal("rev-002", rev2.Revision);
        var schematicBinding = repository.Load().Bindings.Single(binding => binding.ComponentId == "C1" && binding.Role == SymbolRole.Schematic);
        Assert.Equal(SymbolRevisionStatus.Superseded, schematicBinding.Revisions.Single(revision => revision.Revision == "rev-001").Status);
        Assert.Equal(SymbolRevisionStatus.Approved, schematicBinding.Revisions.Single(revision => revision.Revision == "rev-002").Status);
        Assert.True(File.Exists(repository.ResolveArchivePath(schematicBinding.Revisions.Single(revision => revision.Revision == "rev-001").AssetPath)));

        var panel = await approval.ApproveAsync(Request(panelSource, SymbolRole.PanelFootprint));
        Assert.Equal("rev-001", panel.Revision);
        Assert.Equal(2, repository.Load().Bindings.Count(binding => binding.ComponentId == "C1"));

        var resolver = new SymbolResolver(repository, components);
        var current = await resolver.ResolveAsync("C1", SymbolRole.Schematic);
        Assert.Equal("rev-002", current.Revision);
        Assert.Equal(rev2.Sha256, current.Sha256);
        Assert.Equal(SymbolSourceType.ApprovedCustom, current.SourceType);

        var generic = await resolver.ResolveAsync("C1", SymbolRole.ConnectorDetail);
        Assert.Equal(SymbolSourceType.GeneratedGeneric, generic.SourceType);
        Assert.Equal("generated-generic.v1", generic.Revision);
        Assert.Equal(["PIN-1"], generic.GeneratedGeneric!.Endpoints.Select(endpoint => endpoint.EngineeringEndpointId).ToArray());
        Assert.DoesNotContain(generic.GeneratedGeneric.Endpoints, endpoint => endpoint.EngineeringEndpointId == "1");

        var currentManifest = SymbolExportManifest.FromResolutions([current, generic]);
        var pinned = currentManifest.Symbols.Single(entry => entry.SymbolRole == SymbolRole.Schematic);
        Assert.Equal("rev-002", pinned.Revision);
        Assert.Equal(rev2.Sha256, pinned.Sha256);
        Assert.Equal("rev-001", Assert.Single(rev1Manifest.Symbols).Revision);
        Assert.Equal(rev1.Sha256, Assert.Single(rev1Manifest.Symbols).Sha256);

        Assert.Equal(workbookHashBefore, await HashService.Sha256FileAsync(workbookPath));
        var sourceHashesAfter = await HashMap(sourceRoot);
        Assert.Equal(sourceHashesBefore.Count, sourceHashesAfter.Count);
        foreach (var pair in sourceHashesBefore)
        {
            Assert.True(sourceHashesAfter.TryGetValue(pair.Key, out var afterHash));
            Assert.Equal(pair.Value, afterHash);
        }

        // Corrupt authority is never repaired or hidden by GeneratedGeneric fallback.
        var archiveJson = await File.ReadAllTextAsync(repository.ArchivePath);
        Assert.Contains("Superseded", archiveJson, StringComparison.Ordinal);
        await File.WriteAllTextAsync(repository.ArchivePath, archiveJson.Replace("Superseded", "Approved", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => new SymbolResolver(repository, components).ResolveAsync("C1", SymbolRole.Schematic));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static ApproveSymbolRequest Request(string sourcePath, SymbolRole role) => new()
    {
        SourcePath = sourcePath,
        ComponentId = "C1",
        Role = role,
        SourceType = SymbolSourceType.ApprovedCustom,
        UserConfirmed = true,
        PortBindings = role == SymbolRole.Schematic
            ? [new SymbolPortBinding { EngineeringEndpointId = "PIN-1", ConnectionPointId = "TERM01" }]
            : []
    };

    private static void CreateWorkbook(string path)
    {
        using var workbook = new XLWorkbook();
        var components = workbook.AddWorksheet("Components");
        components.Cell(1, 1).Value = "ComponentID";
        components.Cell(1, 2).Value = "Manufacturer";
        components.Cell(1, 3).Value = "Model";
        components.Cell(2, 1).Value = "C1";
        components.Cell(2, 2).Value = "MFR";
        components.Cell(2, 3).Value = "MODEL";

        var ports = workbook.AddWorksheet("Ports");
        ports.Cell(1, 1).Value = "ComponentID";
        ports.Cell(1, 2).Value = "PortID";
        ports.Cell(1, 3).Value = "TopologyEndpointMode";
        ports.Cell(2, 1).Value = "C1";
        ports.Cell(2, 2).Value = "P1";
        ports.Cell(2, 3).Value = "Pins";

        var pins = workbook.AddWorksheet("Pins");
        pins.Cell(1, 1).Value = "PortID";
        pins.Cell(1, 2).Value = "PinID";
        pins.Cell(1, 3).Value = "PinNumber";
        pins.Cell(2, 1).Value = "P1";
        pins.Cell(2, 2).Value = "PIN-1";
        pins.Cell(2, 3).Value = "1";
        workbook.SaveAs(path);
    }

    private static string Source(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return Path.GetFullPath(path);
    }

    private static async Task<Dictionary<string, string>> HashMap(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            result[Path.GetRelativePath(root, path).Replace('\\', '/')] = await HashService.Sha256FileAsync(path);
        return result;
    }
}
