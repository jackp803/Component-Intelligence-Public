using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Sources;
using ComponentIntelligence.Sources.Ifm;
using Xunit;

namespace ComponentIntelligence.Tests.Resolution;

public sealed class SearchRoutingTests
{
    [Fact]
    public async Task Resolver_ReturnsUnsupportedManufacturerInsteadOfFalseNotFound()
    {
        var database = TempDb();
        try
        {
            var repository = new SqliteComponentIrRepository(database);
            var resolver = new ComponentResolver(repository, [new IfmO5D100SeedSource()]);
            var result = await resolver.ResolveAsync(new ComponentIdentityQuery { RawManufacturer = "WAGO", RawModel = "2200-1401" });
            Assert.Equal(ResolutionStatus.NotFound, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.StartsWith(ResolutionDiagnostics.UnsupportedManufacturer, StringComparison.Ordinal));
        }
        finally { Delete(database); }
    }

    [Fact]
    public async Task Resolver_TreatsTbdAsWaitingForInputWithoutSearching()
    {
        var database = TempDb();
        try
        {
            var source = new TrackingSource("IFM");
            var resolver = new ComponentResolver(new SqliteComponentIrRepository(database), [source]);
            var result = await resolver.ResolveAsync(new ComponentIdentityQuery { RawManufacturer = "TBD", RawModel = "TBD (DEMO DEVICE)" });
            Assert.Equal(ResolutionStatus.WaitingForInput, result.Status);
            Assert.Equal(0, source.SearchCalls);
            Assert.Contains(ResolutionDiagnostics.PlaceholderIdentity, result.Diagnostics);
        }
        finally { Delete(database); }
    }

    [Fact]
    public async Task Resolver_OnlyCallsCompatibleManufacturerSource()
    {
        var database = TempDb();
        try
        {
            var omron = new TrackingSource("OMRON", true);
            var wago = new TrackingSource("WAGO", true);
            var resolver = new ComponentResolver(new SqliteComponentIrRepository(database), [omron, wago]);
            var result = await resolver.ResolveAsync(new ComponentIdentityQuery { RawManufacturer = "OMRON", RawModel = "K7L-AT50DP" });
            Assert.Equal(ResolutionStatus.Resolved, result.Status);
            Assert.Equal(1, omron.SearchCalls);
            Assert.Equal(0, wago.SearchCalls);
        }
        finally { Delete(database); }
    }

    [Fact]
    public async Task Resolver_MergesMultipleExactUrlsForSameOfficialModel()
    {
        var database = TempDb();
        try
        {
            var source = new DuplicateExactSource();
            var resolver = new ComponentResolver(new SqliteComponentIrRepository(database), [source]);
            var result = await resolver.ResolveAsync(new ComponentIdentityQuery { RawManufacturer = "WAGO", RawModel = "2200-1401" });

            Assert.Equal(ResolutionStatus.Resolved, result.Status);
            Assert.Equal(MatchLevel.Exact, result.MatchLevel);
            Assert.NotNull(result.ResolvedIdentity);
            Assert.Equal("2200-1401", result.ResolvedIdentity!.OfficialModel);
            Assert.Equal("https://www.wago.com/global/rail-mount-terminal-blocks/example/p/2200-1401", result.ResolvedIdentity.OfficialProductUrl?.AbsoluteUri);
            Assert.Equal(2, result.Evidence.Count);
            Assert.Contains(result.Diagnostics, item => item == "MULTIPLE_EXACT_EVIDENCE_MERGED:2");
        }
        finally { Delete(database); }
    }

    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private static void Delete(string path) { if (File.Exists(path)) File.Delete(path); }

    private sealed class TrackingSource : IComponentSource, IComponentSourceMetadata
    {
        private readonly string _manufacturer;
        private readonly bool _returnCandidate;
        public int SearchCalls { get; private set; }
        public TrackingSource(string manufacturer, bool returnCandidate = false) { _manufacturer = manufacturer; _returnCandidate = returnCandidate; }
        public string SourceName => $"{_manufacturer} Test Source";
        public IReadOnlyCollection<string> SupportedManufacturers => [_manufacturer];
        public bool CanHandle(string manufacturer, string model) => string.Equals(ManufacturerNormalizer.NormalizeKey(manufacturer), _manufacturer, StringComparison.OrdinalIgnoreCase);
        public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            if (!_returnCandidate) return Task.FromResult<IReadOnlyList<ComponentCandidate>>(Array.Empty<ComponentCandidate>());
            var model = query.NormalizedModel ?? query.RawModel ?? "UNKNOWN";
            IReadOnlyList<ComponentCandidate> candidates = [new ComponentCandidate { Manufacturer = _manufacturer, OfficialModel = model, Mpn = model, SourceType = ComponentSourceType.ManufacturerProductPage }];
            return Task.FromResult(candidates);
        }
        public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult<ProductPage?>(null);
        public Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ComponentDocument>>(Array.Empty<ComponentDocument>());
        public Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult(new RawComponentData());
    }

    private sealed class DuplicateExactSource : IComponentSource, IComponentSourceMetadata
    {
        public string SourceName => "WAGO Duplicate Exact Test Source";
        public IReadOnlyCollection<string> SupportedManufacturers => ["WAGO"];
        public bool CanHandle(string manufacturer, string model) => string.Equals(ManufacturerNormalizer.NormalizeKey(manufacturer), "WAGO", StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
        {
            var model = query.NormalizedModel ?? query.RawModel ?? "2200-1401";
            var now = DateTimeOffset.UtcNow;
            IReadOnlyList<ComponentCandidate> candidates =
            [
                new ComponentCandidate
                {
                    Manufacturer = "WAGO",
                    OfficialModel = model,
                    Mpn = model,
                    SourceType = ComponentSourceType.ManufacturerProductPage,
                    ProductUrl = new Uri($"https://www.wago.com/global/search?q={Uri.EscapeDataString(model)}"),
                    Evidence = [new Evidence { SourceType = ComponentSourceType.ManufacturerProductPage, SourceUrl = new Uri("https://www.wago.com/global/search"), ExtractionMethod = ExtractionMethod.Html, RawValue = model, RetrievedAt = now, VerificationStatus = VerificationStatus.SingleSource }]
                },
                new ComponentCandidate
                {
                    Manufacturer = "WAGO",
                    OfficialModel = model,
                    Mpn = model,
                    SourceType = ComponentSourceType.ManufacturerProductPage,
                    ProductUrl = new Uri($"https://www.wago.com/global/rail-mount-terminal-blocks/example/p/{model}"),
                    Evidence = [new Evidence { SourceType = ComponentSourceType.ManufacturerProductPage, SourceUrl = new Uri($"https://www.wago.com/global/rail-mount-terminal-blocks/example/p/{model}"), ExtractionMethod = ExtractionMethod.Html, RawValue = model, RetrievedAt = now, VerificationStatus = VerificationStatus.SingleSource }]
                }
            ];
            return Task.FromResult(candidates);
        }

        public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult<ProductPage?>(null);
        public Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ComponentDocument>>(Array.Empty<ComponentDocument>());
        public Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult(new RawComponentData());
    }
}
