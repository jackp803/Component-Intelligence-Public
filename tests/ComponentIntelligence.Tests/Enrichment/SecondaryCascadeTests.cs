using ComponentIntelligence.Contracts;
using ComponentIntelligence.Enrichment;
using ComponentIntelligence.Sources;
using Xunit;

namespace ComponentIntelligence.Tests.Enrichment;

public sealed class SecondaryCascadeTests
{
    private static readonly ComponentIdentity Identity = new()
    {
        OfficialManufacturer = "IFM",
        OfficialModel = "TA2115",
        OfficialProductUrl = new Uri("https://manufacturer.example/TA2115")
    };

    [Fact]
    public async Task Enricher_StopsAfterFirstSecondarySourceAcquiresEngineeringPdf()
    {
        var primary = new FakeSource("primary", Raw([Spec("only", "value", "misc.only")]));
        var secondary1 = new FakeSecondarySource("authorized", Raw(SufficientSpecs(),
        [
            new ComponentDocument
            {
                Type = "datasheet-mirror",
                Url = new Uri("https://authorized.example/TA2115.pdf"),
                SourceType = ComponentSourceType.AuthorizedDistributor
            }
        ]));
        var secondary2 = new FakeSecondarySource("aggregator", Raw([Spec("unused", "value", "misc.unused")]));

        var profile = await new ComponentEnricher([primary, secondary1, secondary2]).EnrichAsync(Identity);

        Assert.Equal(1, secondary1.ExtractCalls);
        Assert.Equal(0, secondary2.ExtractCalls);
        Assert.Contains(profile.Documents, document => document.Url.Host == "authorized.example");
        Assert.Contains("PDF_ACQUIRED_ADDITIONAL_SOURCE_SEARCH_SKIPPED", profile.MissingData);
    }

    [Fact]
    public async Task Enricher_StopsAfterPdfEvenWhenTopologyPinFunctionsRemainIncomplete()
    {
        var primary = new FakeSource("primary", Raw([Spec("only", "value", "misc.only")]));
        var richButNotTopologyReady = new List<RawSpecification>
        {
            Spec("Operating voltage", "18...30 V DC", "power.operating_voltage"),
            Spec("Output type", "IO-Link", "io.output_type"),
            Spec("Connector family", "M12", "connector.family"),
            Spec("Pin count", "4", "connector.pin_count")
        };
        for (var index = 0; index < 40; index++)
            richButNotTopologyReady.Add(Spec($"General field {index}", $"Value {index}", $"general.field_{index}"));

        var secondary1 = new FakeSecondarySource("rich-no-pinout", Raw(richButNotTopologyReady,
        [
            new ComponentDocument
            {
                Type = "datasheet",
                Url = new Uri("https://rich.example/TA2115.pdf"),
                SourceType = ComponentSourceType.AuthorizedDistributor
            }
        ]));
        var secondary2 = new FakeSecondarySource("next-source", Raw(SufficientSpecs()));

        var profile = await new ComponentEnricher([primary, secondary1, secondary2]).EnrichAsync(Identity);

        Assert.Equal(1, secondary1.ExtractCalls);
        Assert.Equal(0, secondary2.ExtractCalls);
        Assert.Contains("TOPOLOGY_KNOWLEDGE_INCOMPLETE", profile.MissingData);
        Assert.Contains("PDF_ACQUIRED_ADDITIONAL_SOURCE_SEARCH_SKIPPED", profile.MissingData);
    }

    [Fact]
    public async Task Enricher_StopsRemainingPrimarySourcesAfterEngineeringPdf()
    {
        var primary1 = new FakeSource("official-product", Raw([Spec("Operating voltage", "18...30 V DC", "power.operating_voltage")],
        [
            new ComponentDocument
            {
                Type = "manufacturer-datasheet",
                Url = new Uri("https://manufacturer.example/TA2115.pdf"),
                SourceType = ComponentSourceType.ManufacturerDatasheet
            }
        ]));
        var primary2 = new FakeSource("official-download-center", Raw([Spec("unused", "value", "misc.unused")]));
        var secondary = new FakeSecondarySource("authorized", Raw(SufficientSpecs()));

        await new ComponentEnricher([primary1, primary2, secondary]).EnrichAsync(Identity);

        Assert.Equal(1, primary1.ExtractCalls);
        Assert.Equal(0, primary2.ExtractCalls);
        Assert.Equal(0, secondary.ExtractCalls);
    }

    [Fact]
    public async Task Enricher_ContinuesWhenOnlyDownloadPageWasFoundButNoPdfWasAcquired()
    {
        var discovered = new ComponentDocument
        {
            Type = "datasheet-index",
            Url = new Uri("https://manufacturer.example/TA2115?tab=documents"),
            SourceType = ComponentSourceType.ManufacturerProductPage
        };
        var primary = new FakeSource("legacy", Raw([Spec("only", "value", "misc.only")]), [discovered]);
        var secondary = new FakeSecondarySource("authorized", Raw(SufficientSpecs()));

        await new ComponentEnricher([primary, secondary]).EnrichAsync(Identity);

        Assert.Equal(1, primary.DiscoverCalls);
        Assert.Equal(1, secondary.ExtractCalls);
    }

    [Fact]
    public async Task Enricher_DoesNotRediscoverDocumentsWhenExtractAlreadyReturnsThem()
    {
        var document = new ComponentDocument
        {
            Type = "datasheet",
            Url = new Uri("https://manufacturer.example/TA2115.pdf"),
            SourceType = ComponentSourceType.ManufacturerDatasheet
        };
        var primary = new FakeSource("primary", Raw(SufficientSpecs(), [document]));

        var profile = await new ComponentEnricher([primary]).EnrichAsync(Identity);

        Assert.Equal(1, primary.ExtractCalls);
        Assert.Equal(0, primary.DiscoverCalls);
        Assert.Single(profile.Documents);
    }

    [Fact]
    public async Task Enricher_FallsBackToExplicitDiscoveryForLegacySourceWithoutExtractDocuments()
    {
        var discovered = new ComponentDocument
        {
            Type = "datasheet-index",
            Url = new Uri("https://manufacturer.example/TA2115?tab=documents"),
            SourceType = ComponentSourceType.ManufacturerProductPage
        };
        var primary = new FakeSource("legacy", Raw(SufficientSpecs()), [discovered]);

        var profile = await new ComponentEnricher([primary]).EnrichAsync(Identity);

        Assert.Equal(1, primary.ExtractCalls);
        Assert.Equal(1, primary.DiscoverCalls);
        Assert.Single(profile.Documents);
    }

    private static RawComponentData Raw(
        IReadOnlyList<RawSpecification> specs,
        IReadOnlyList<ComponentDocument>? documents = null) => new()
        {
            Specifications = specs,
            Documents = documents ?? Array.Empty<ComponentDocument>()
        };

    private static IReadOnlyList<RawSpecification> SufficientSpecs()
    {
        var specs = new List<RawSpecification>
        {
            Spec("Operating voltage", "18...30 V DC", "power.operating_voltage"),
            Spec("Output type", "IO-Link", "io.output_type"),
            Spec("Connector family", "M12", "connector.family"),
            Spec("Pin count", "4", "connector.pin_count"),
            Pin("1", "L+ 24V"),
            Pin("2", "OUT2 digital output"),
            Pin("3", "L- 0V"),
            Pin("4", "C/Q IO-Link")
        };
        for (var index = 0; index < 32; index++)
            specs.Add(Spec($"Engineering field {index}", $"Value {index}", $"engineering.field_{index}"));
        return specs;
    }

    private static RawSpecification Pin(string number, string value) => new()
    {
        Section = "Pin assignment / wiring",
        RawName = number,
        RawValue = value,
        ProposedKey = $"connector.pin_{number}.function",
        Status = VerificationStatus.SingleSource
    };

    private static RawSpecification Spec(string name, string value, string key) => new()
    {
        RawName = name,
        RawValue = value,
        ProposedKey = key,
        Status = VerificationStatus.SingleSource
    };

    private class FakeSource : IComponentSource, IComponentSourceMetadata
    {
        private readonly RawComponentData _raw;
        private readonly IReadOnlyList<ComponentDocument> _discovered;

        public FakeSource(string name, RawComponentData raw, IReadOnlyList<ComponentDocument>? discovered = null)
        {
            SourceName = name;
            _raw = raw;
            _discovered = discovered ?? Array.Empty<ComponentDocument>();
        }

        public string SourceName { get; }
        public IReadOnlyCollection<string> SupportedManufacturers => ["IFM"];
        public int ExtractCalls { get; private set; }
        public int DiscoverCalls { get; private set; }

        public bool CanHandle(string manufacturer, string model) => true;

        public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComponentCandidate>>(Array.Empty<ComponentCandidate>());

        public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductPage?>(new ProductPage { Url = identity.OfficialProductUrl!, RawContent = null });

        public Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            return Task.FromResult(_discovered);
        }

        public Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
        {
            ExtractCalls++;
            return Task.FromResult(_raw);
        }
    }

    private sealed class FakeSecondarySource : FakeSource, ISecondaryEnrichmentSource
    {
        public FakeSecondarySource(string name, RawComponentData raw) : base(name, raw) { }
    }
}
