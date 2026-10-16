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
    /// <summary>
    /// Legacy online pipeline retained for CLI/regression compatibility only.
    /// The Windows desktop UI must use CreateCentralWorkbookLookupService instead.
    /// </summary>
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
            ? new EngineeringValidatedKnowledgeStore(new NotionComponentKnowledgeStore(notionOptions))
            : null;

        return new ComponentIntelligencePipeline(
            repository,
            new ComponentResolver(repository, sources),
            new ComponentEnricher(sources),
            new ComponentNormalizer(),
            new VerificationEngine(),
            centralKnowledge);
    }

    /// <summary>
    /// Production Windows-desktop path. The selected Component_Intelligence_Database.xlsx is the
    /// central engineering authority and must contain Components, Ports, and Pins sheets. Google Drive
    /// for Desktop may synchronize the workbook and Documents folder locally. SQLite is hydrated only
    /// as a runtime/query cache; no web resolver, PDF downloader, parser, or central write is constructed.
    /// </summary>
    public static CentralLibraryComponentLookupService CreateCentralWorkbookLookupService(
        string databasePath,
        string workbookPath)
    {
        var repository = new SqliteComponentIrRepository(databasePath);
        IComponentKnowledgeStore central = new WorkbookComponentKnowledgeStore(workbookPath);
        return new CentralLibraryComponentLookupService(repository, central);
    }

    /// <summary>
    /// Legacy Notion adapter retained for migration/regression compatibility. New Windows UI code must
    /// not call this method.
    /// </summary>
    [Obsolete("Desktop central knowledge is workbook-based. Use CreateCentralWorkbookLookupService.")]
    public static NotionOnlyComponentLookupService CreateNotionOnlyLookupService(string databasePath)
    {
        var repository = new SqliteComponentIrRepository(databasePath);
        var notionOptions = NotionKnowledgeStoreOptions.FromEnvironment();
        IComponentKnowledgeStore notion = new EngineeringValidatedKnowledgeStore(
            new NotionComponentKnowledgeStore(notionOptions));
        return new NotionOnlyComponentLookupService(repository, notion);
    }

    [Obsolete("Desktop search uses the central workbook. Use CreateCentralWorkbookLookupService.")]
    public static ComponentSearchService CreateOnlineSearchService(string databasePath, string? cachePath = null) =>
        new(CreateOnline(databasePath, cachePath));

    /// <summary>
    /// Legacy explicit Local ↔ Notion manual sync retained for migration/regression compatibility.
    /// It is no longer the production desktop central-archive path.
    /// </summary>
    [Obsolete("Central archive writes are owned by the GPT archive workflow, not the desktop application.")]
    public static ComponentKnowledgeSyncService CreateKnowledgeSyncService(string databasePath)
    {
        var options = NotionKnowledgeStoreOptions.FromEnvironment();
        IComponentKnowledgeStore central = new EngineeringValidatedKnowledgeStore(new NotionComponentKnowledgeStore(options));
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
