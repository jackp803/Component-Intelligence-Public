using ComponentIntelligence.Contracts;
using ComponentIntelligence.Enrichment;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Sources;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Knowledge;

public sealed class SecondaryEnrichmentTests
{
    [Fact]
    public async Task Secondary_source_enriches_but_never_resolves_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new SqliteComponentIrRepository(Path.Combine(directory, "secondary.db"));
            var primary = new SparsePrimarySource();
            var secondary = new RecordingSecondarySource();
            IComponentSource[] sources = [primary, secondary];
            var pipeline = new ComponentIntelligencePipeline(
                repository,
                new ComponentResolver(repository, sources),
                new ComponentEnricher(sources),
                new ComponentNormalizer(),
                new VerificationEngine());

            var result = await pipeline.ProcessAsync(new BomRow
            {
                RowId = "1",
                Manufacturer = "IFM",
                RawManufacturer = "IFM",
                ModelOrPartNumber = "TA2115",
                RawModelOrPartNumber = "TA2115",
                UsedQuantity = 1,
                TotalQuantity = 1,
                SpareQuantity = 0,
                ImportStatus = BomImportStatus.Imported
            });

            Assert.Equal(ResolutionStatus.Resolved, result.ResolutionStatus);
            Assert.Equal(0, secondary.SearchCalls);
            Assert.True(secondary.ExtractCalls > 0);
            Assert.NotNull(result.Raw);
            var distributorSpec = Assert.Single(result.Raw!.Specifications, spec => spec.RawName == "Measuring range");
            Assert.Contains(distributorSpec.Evidence, evidence => evidence.SourceType == ComponentSourceType.AuthorizedDistributor);
            Assert.Contains(result.Component!.Specifications, spec => spec.Name == "Measuring range" && spec.Value == "-50...150 °C");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class SparsePrimarySource : IComponentSource
    {
        public bool CanHandle(string manufacturer, string model) =>
            manufacturer.Contains("IFM", StringComparison.OrdinalIgnoreCase) && model.Equals("TA2115", StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
        {
            var url = new Uri("https://www.ifm.com/us/en/product/TA2115");
            return Task.FromResult<IReadOnlyList<ComponentCandidate>>
            ([
                new ComponentCandidate
                {
                    Manufacturer = "IFM",
                    OfficialModel = "TA2115",
                    Mpn = "TA2115",
                    SourceType = ComponentSourceType.ManufacturerProductPage,
                    ProductUrl = url,
                    Evidence =
                    [
                        new Evidence
                        {
                            SourceType = ComponentSourceType.ManufacturerProductPage,
                            SourceUrl = url,
                            ExtractionMethod = ExtractionMethod.Html,
                            RawValue = "TA2115",
                            RetrievedAt = DateTimeOffset.UtcNow,
                            VerificationStatus = VerificationStatus.SingleSource
                        }
                    ]
                }
            ]);
        }

        public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductPage?>(new ProductPage
            {
                Url = new Uri("https://www.ifm.com/us/en/product/TA2115"),
                RawContent = "<html><body>TA2115</body></html>"
            });

        public Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComponentDocument>>(Array.Empty<ComponentDocument>());

        public Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
        {
            var source = new Uri("https://www.ifm.com/us/en/product/TA2115");
            return Task.FromResult(new RawComponentData
            {
                Specifications =
                [
                    new RawSpecification
                    {
                        RawName = "Product name",
                        RawValue = "TA2115 Temperature transmitter",
                        ProposedKey = "identity.product_name",
                        Evidence =
                        [
                            new Evidence
                            {
                                SourceType = ComponentSourceType.ManufacturerProductPage,
                                SourceUrl = source,
                                ExtractionMethod = ExtractionMethod.Html,
                                RawValue = "TA2115 Temperature transmitter",
                                RetrievedAt = DateTimeOffset.UtcNow,
                                VerificationStatus = VerificationStatus.SingleSource
                            }
                        ]
                    }
                ]
            });
        }
    }

    private sealed class RecordingSecondarySource : IComponentSource, ISecondaryEnrichmentSource
    {
        public int SearchCalls { get; private set; }
        public int ExtractCalls { get; private set; }

        public bool CanHandle(string manufacturer, string model) => true;

        public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            throw new InvalidOperationException("Secondary source must not participate in identity resolution.");
        }

        public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductPage?>(new ProductPage
            {
                Url = new Uri("https://www.digikey.com/en/products/detail/ifm-efector-inc/TA2115/12145774"),
                RawContent = "<html><body>TA2115</body></html>"
            });

        public Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComponentDocument>>(Array.Empty<ComponentDocument>());

        public Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
        {
            ExtractCalls++;
            var source = new Uri("https://www.digikey.com/en/products/detail/ifm-efector-inc/TA2115/12145774");
            return Task.FromResult(new RawComponentData
            {
                Specifications =
                [
                    new RawSpecification
                    {
                        RawName = "Measuring range",
                        RawValue = "-50...150 °C",
                        ProposedKey = "sensing.measuring_range",
                        Evidence =
                        [
                            new Evidence
                            {
                                SourceType = ComponentSourceType.AuthorizedDistributor,
                                SourceUrl = source,
                                ExtractionMethod = ExtractionMethod.TableParser,
                                RawValue = "-50...150 °C",
                                RetrievedAt = DateTimeOffset.UtcNow,
                                VerificationStatus = VerificationStatus.SingleSource
                            }
                        ]
                    }
                ]
            });
        }
    }
}
