using ComponentIntelligence.Cache;
using ComponentIntelligence.Enrichment;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Search;
using ComponentIntelligence.Sources;
using ComponentIntelligence.Sources.Ifm;
using ComponentIntelligence.Sources.Official;
using ComponentIntelligence.Sources.Secondary;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Runtime;

public static class ComponentRuntimeFactory
{
    public static ComponentIntelligencePipeline CreateOnline(string databasePath, string? cachePath = null)
    {
        var repository = new SqliteComponentIrRepository(databasePath);
        var factory = new DefaultHttpClientFactory();
        var http = new ComponentHttpClient(factory);
        var parser = new SpecificationParser();
        var cache = new CacheManager(cachePath ?? DefaultCachePath(), http);
        var documentPipeline = new DocumentPipeline(cache, new PdfTextExtractor(), parser);
        IRenderedPageFetcher rendered = new PlaywrightRenderedPageFetcher();
        var sources = new List<IComponentSource> { new IfmSource(http, parser, documentPipeline, rendered) };
        sources.AddRange(OfficialSourceCatalog.Create(http, parser, documentPipeline, rendered));
        sources.AddRange(SecondarySourceCatalog.Create(http, parser, documentPipeline, rendered));

        var notionOptions = NotionKnowledgeStoreOptions.FromEnvironment();
        IComponentKnowledgeStore? centralKnowledge = notionOptions.IsEnabled
            ? new NotionComponentKnowledgeStore(notionOptions)
            : null;

        return new ComponentIntelligencePipeline(
            repository,
            new ComponentResolver(repository, sources),
            new ComponentEnricher(sources),
            new ComponentNormalizer(),
            new VerificationEngine(),
            centralKnowledge);
    }

    public static ComponentSearchService CreateOnlineSearchService(string databasePath, string? cachePath = null) =>
        new(CreateOnline(databasePath, cachePath));

    /// <summary>
    /// Creates the explicit Local ↔ Notion manual sync workflow. The returned service is safe to use
    /// even when no Notion token exists: local edits are still saved and marked Pending/LocalOnly.
    /// </summary>
    public static ComponentKnowledgeSyncService CreateKnowledgeSyncService(string databasePath)
    {
        var options = NotionKnowledgeStoreOptions.FromEnvironment();
        IComponentKnowledgeStore central = new NotionComponentKnowledgeStore(options);
        return new ComponentKnowledgeSyncService(databasePath, central);
    }

    public static ComponentIntelligencePipeline CreateOfflineDemo(string databasePath)
    {
        var repository = new SqliteComponentIrRepository(databasePath);
        IComponentSource[] sources = [new IfmO5D100SeedSource()];
        return new ComponentIntelligencePipeline(repository, new ComponentResolver(repository, sources), new ComponentEnricher(sources), new ComponentNormalizer(), new VerificationEngine());
    }

    public static string DefaultCachePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "ComponentIntelligence", "cache");
    }
}
