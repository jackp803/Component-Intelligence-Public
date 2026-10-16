using ComponentIntelligence.Contracts;
using ComponentIntelligence.Enrichment;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Search;
using ComponentIntelligence.Sources;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Mvp;

public sealed class CentralKnowledgePriorityTests
{
    [Fact]
    public async Task NormalLookup_PrefersNotionOverExistingLocalSqlite_AndRefreshesCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new SqliteComponentIrRepository(Path.Combine(directory, "priority.db"));
            var local = ReadyComponent("local-version", "Local SQLite copy");
            var central = ReadyComponent("notion-version", "Notion authoritative copy");
            await repository.SaveAsync(local);

            var pipeline = CreatePipeline(repository, new FakeKnowledgeStore(central));
            var result = await pipeline.ProcessAsync(Row());

            Assert.Equal(ResolutionStatus.Resolved, result.ResolutionStatus);
            Assert.NotNull(result.Component);
            Assert.Equal("notion-version", result.Component!.Identity.ComponentId);
            Assert.Equal("Notion authoritative copy", result.Component.Classification.Category);
            Assert.False(result.LocalRepositoryHit);
            Assert.Contains("NOTION_CENTRAL_HIT", result.Issues);
            Assert.Contains("NOTION_CENTRAL_HYDRATED_LOCAL_CACHE", result.Issues);

            var cached = await repository.FindByIdentityAsync("MOXA", "EDS-2005-EL");
            Assert.NotNull(cached);
            Assert.Equal("notion-version", cached!.Identity.ComponentId);
            Assert.Equal("Notion authoritative copy", cached.Classification.Category);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task NormalLookup_UsesLocalSqliteOnlyAfterNotionMiss()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new SqliteComponentIrRepository(Path.Combine(directory, "priority.db"));
            var local = ReadyComponent("local-version", "Local fallback copy");
            await repository.SaveAsync(local);

            var pipeline = CreatePipeline(repository, new FakeKnowledgeStore(null));
            var result = await pipeline.ProcessAsync(Row());

            Assert.Equal(ResolutionStatus.Resolved, result.ResolutionStatus);
            Assert.NotNull(result.Component);
            Assert.Equal("local-version", result.Component!.Identity.ComponentId);
            Assert.True(result.LocalRepositoryHit);
            Assert.Contains("NOTION_CENTRAL_MISS", result.Issues);
            Assert.Contains("NOTION_CENTRAL_FALLBACK_TO_LOCAL_SQLITE", result.Issues);
            Assert.Contains("LOCAL_SQLITE_HIT_AFTER_NOTION", result.Issues);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task NormalSearch_ReturnsIncompleteCentralKnowledgeWithoutImplicitEnrichment()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new SqliteComponentIrRepository(Path.Combine(directory, "priority.db"));
            var central = IncompleteComponent("notion-incomplete", "Existing central knowledge");
            var pipeline = CreatePipeline(repository, new FakeKnowledgeStore(central));
            var search = new ComponentSearchService(pipeline);

            var result = await search.SearchAsync("MOXA", "EDS-2005-EL");

            Assert.Equal(ResolutionStatus.Resolved, result.Result.ResolutionStatus);
            Assert.NotNull(result.Result.Component);
            Assert.Equal("notion-incomplete", result.Result.Component!.Identity.ComponentId);
            Assert.Contains("NOTION_CENTRAL_SEARCH_HIT_NO_ENRICHMENT", result.Result.Issues);
            Assert.Contains("EXISTING_KNOWLEDGE_RETURNED_WITHOUT_ENRICHMENT", result.Result.Issues);
            Assert.Contains("EXPLICIT_DEEP_SEARCH_REQUIRED_FOR_REFRESH", result.Result.Issues);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static ComponentIntelligencePipeline CreatePipeline(
        SqliteComponentIrRepository repository,
        IComponentKnowledgeStore central)
    {
        IComponentSource[] sources = Array.Empty<IComponentSource>();
        return new ComponentIntelligencePipeline(
            repository,
            new ComponentResolver(repository, sources),
            new ComponentEnricher(sources),
            new ComponentNormalizer(),
            new VerificationEngine(),
            central);
    }

    private static BomRow Row() => new()
    {
        RowId = "priority-test",
        Manufacturer = "MOXA",
        RawManufacturer = "MOXA",
        ModelOrPartNumber = "EDS-2005-EL",
        RawModelOrPartNumber = "EDS-2005-EL",
        UsedQuantity = 1,
        TotalQuantity = 1,
        SpareQuantity = 0,
        ImportStatus = BomImportStatus.Imported
    };

    private static ComponentIR ReadyComponent(string componentId, string category) => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = componentId,
            Manufacturer = "MOXA",
            Model = "EDS-2005-EL",
            Mpn = "EDS-2005-EL"
        },
        Classification = new ComponentClassification { Category = category },
        Ports =
        [
            new ComponentPort
            {
                PortId = "ETH1",
                PortType = "RJ45",
                ConnectorFamily = "RJ45",
                Protocol = "Ethernet"
            }
        ]
    };

    private static ComponentIR IncompleteComponent(string componentId, string category) => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = componentId,
            Manufacturer = "MOXA",
            Model = "EDS-2005-EL",
            Mpn = "EDS-2005-EL"
        },
        Classification = new ComponentClassification { Category = category }
    };

    private sealed class FakeKnowledgeStore(ComponentIR? component) : IComponentKnowledgeStore
    {
        public bool IsEnabled => true;

        public Task<ComponentKnowledgeLookup> FindByIdentityAsync(
            string manufacturer,
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(component is null
                ? new ComponentKnowledgeLookup(null, ["NOTION_CENTRAL_MISS"])
                : new ComponentKnowledgeLookup(component, ["NOTION_CENTRAL_HIT"]));

        public Task<ComponentKnowledgeWriteResult> UpsertAsync(
            ComponentIR value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComponentKnowledgeWriteResult(true, ["NOTION_CENTRAL_SYNC_OK"]));
    }
}
