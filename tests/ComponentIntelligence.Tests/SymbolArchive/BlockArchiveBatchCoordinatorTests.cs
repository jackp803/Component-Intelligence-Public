using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class BlockArchiveBatchCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-block-batch-{Guid.NewGuid():N}");

    public BlockArchiveBatchCoordinatorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ScanAndMatchingNeverPopulateUserAuthority()
    {
        var sourceRoot = SourceRoot(("MODEL.dwg", "one"));
        var coordinator = Coordinator([Component("C1", "MFR", "MODEL")]);

        var row = Assert.Single(await coordinator.ScanAsync(sourceRoot));

        Assert.NotEmpty(row.SuggestedComponents);
        Assert.Null(row.SelectedComponentId);
        Assert.Null(row.SelectedRole);
        Assert.Null(row.SelectedSourceType);
        Assert.False(row.UserConfirmed);
        Assert.DoesNotContain(SymbolSourceType.GeneratedGeneric, row.ImportSourceTypes);
        Assert.Equal("ReviewRequired", row.ReviewStatus);
    }

    [Fact]
    public async Task AmbiguousEqualScoreSuggestionsRemainReviewRequired()
    {
        var sourceRoot = SourceRoot(("MODEL.dwg", "one"));
        var coordinator = Coordinator([
            Component("C1", "MFR-A", "MODEL"),
            Component("C2", "MFR-B", "MODEL")
        ]);

        var row = Assert.Single(await coordinator.ScanAsync(sourceRoot));

        Assert.Equal(2, row.SuggestedComponents.Count);
        Assert.Equal(row.SuggestedComponents[0].Score, row.SuggestedComponents[1].Score);
        Assert.Null(row.SelectedComponentId);
        Assert.Equal("ReviewRequired", row.ReviewStatus);
    }

    [Fact]
    public async Task BatchArchivePreflightsAllRowsBeforeAnyWrite()
    {
        var sourceRoot = SourceRoot(("MODEL-a.dwg", "one"), ("MODEL-b.dwg", "two"));
        var coordinator = Coordinator([Component("C1", "MFR", "MODEL")]);
        var rows = (await coordinator.ScanAsync(sourceRoot)).ToArray();
        Configure(rows[0]);
        rows[1].SelectedComponentId = "C1";
        rows[1].SelectedRole = SymbolRole.Schematic;
        rows[1].SelectedSourceType = SymbolSourceType.ApprovedCustom;
        rows[1].UserConfirmed = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApproveSelectedAsync(rows));

        Assert.False(File.Exists(Path.Combine(_root, SymbolArchiveRepository.FileName)));
        Assert.False(Directory.Exists(Path.Combine(_root, "Documents")));
    }

    [Fact]
    public async Task InvalidEndpointMappingFailsPreflightBeforeAnyWrite()
    {
        var sourceRoot = SourceRoot(("MODEL.dwg", "one"));
        var coordinator = Coordinator([Component("C1", "MFR", "MODEL")]);
        var row = Assert.Single(await coordinator.ScanAsync(sourceRoot));
        Configure(row);
        row.PortBindings =
        [
            new SymbolPortBinding { EngineeringEndpointId = "INVENTED", ConnectionPointId = "TERM01" }
        ];

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApproveSelectedAsync([row]));

        Assert.False(File.Exists(Path.Combine(_root, SymbolArchiveRepository.FileName)));
        Assert.False(Directory.Exists(Path.Combine(_root, "Documents")));
    }

    [Fact]
    public async Task DeepInspectionFailurePreservesBasicCandidateAndDoesNotAssignAuthority()
    {
        var sourceRoot = SourceRoot(("MODEL.dwg", "one"));
        var coordinator = Coordinator(
            [Component("C1", "MFR", "MODEL")],
            new FakeInspector(new BlockDeepInspectionResult
            {
                Status = DeepInspectionStatus.Failed,
                Diagnostics = ["EXPECTED_TEST_FAILURE"]
            }));
        var row = Assert.Single(await coordinator.ScanAsync(sourceRoot));

        await coordinator.DeepInspectAsync(row);

        Assert.Equal(DeepInspectionStatus.Failed, row.Candidate.DeepInspectionStatus);
        Assert.Equal("MODEL.dwg", row.Candidate.FileName);
        Assert.Null(row.SelectedComponentId);
        Assert.Null(row.SelectedRole);
        Assert.Null(row.SelectedSourceType);
        Assert.False(row.UserConfirmed);
    }

    [Fact]
    public async Task SourceIntegrityFailureBlocksApproval()
    {
        var sourceRoot = SourceRoot(("MODEL.dwg", "one"));
        var coordinator = Coordinator(
            [Component("C1", "MFR", "MODEL")],
            new FakeInspector(new BlockDeepInspectionResult
            {
                Status = DeepInspectionStatus.Failed,
                SourceHashBefore = "before",
                SourceHashAfter = "after",
                Diagnostics = ["SOURCE_HASH_CHANGED_DURING_DEEP_INSPECTION"]
            }));
        var row = Assert.Single(await coordinator.ScanAsync(sourceRoot));
        await coordinator.DeepInspectAsync(row);
        Configure(row);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApproveSelectedAsync([row]));

        Assert.True(row.Candidate.SourceIntegrityFailed);
        Assert.Equal("BlockedIntegrity", row.ReviewStatus);
        Assert.False(File.Exists(Path.Combine(_root, SymbolArchiveRepository.FileName)));
    }

    [Fact]
    public async Task ExplicitValidSelectionArchivesAndRefreshesActualRevisionEvidence()
    {
        var sourceRoot = SourceRoot(("MODEL.dwg", "one"));
        var coordinator = Coordinator([Component("C1", "MFR", "MODEL")]);
        var row = Assert.Single(await coordinator.ScanAsync(sourceRoot));
        Configure(row);
        row.PortBindings =
        [
            new SymbolPortBinding { EngineeringEndpointId = "P1", ConnectionPointId = "TERM01" }
        ];

        var result = Assert.Single(await coordinator.ApproveSelectedAsync([row]));

        Assert.Equal(SymbolApprovalDisposition.CreatedRevision, result.Disposition);
        Assert.Equal("rev-001", row.ApprovedRevision);
        Assert.Equal(result.AssetPath, row.ApprovedAssetPath);
        Assert.Equal(result.Sha256, row.ApprovedSha256);
        Assert.Equal("Approved", row.ReviewStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private BlockArchiveBatchCoordinator Coordinator(
        IReadOnlyList<ComponentIR> components,
        IBlockDeepInspector? inspector = null) =>
        new(_root, components, inspector ?? new FakeInspector(new BlockDeepInspectionResult
        {
            Status = DeepInspectionStatus.Unavailable,
            Diagnostics = ["TEST_NO_AUTOCAD"]
        }));

    private string SourceRoot(params (string Name, string Content)[] files)
    {
        var path = Path.Combine(_root, "source");
        Directory.CreateDirectory(path);
        foreach (var file in files) File.WriteAllText(Path.Combine(path, file.Name), file.Content);
        return path;
    }

    private static ComponentIR Component(string id, string manufacturer, string model) => new()
    {
        Identity = new ComponentIrIdentity { ComponentId = id, Manufacturer = manufacturer, Model = model },
        Ports = [new ComponentPort { PortId = "P1", TopologyEndpointMode = "Connector" }],
        Pins = [new ComponentPin { PinId = "PIN-1", PortId = "P1", PinNumber = "1" }]
    };

    private static void Configure(BlockArchiveReviewRow row)
    {
        row.SelectedComponentId = "C1";
        row.SelectedRole = SymbolRole.Schematic;
        row.SelectedSourceType = SymbolSourceType.ApprovedCustom;
        row.UserConfirmed = true;
    }

    private sealed class FakeInspector(BlockDeepInspectionResult result) : IBlockDeepInspector
    {
        public Task<BlockDeepInspectionResult> InspectAsync(
            BlockArchiveCandidate candidate,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
