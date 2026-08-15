using ComponentIntelligence.Contracts;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Sources.Ifm;

/// <summary>
/// Deterministic offline seed adapter for the v0.1 acceptance component IFM O5D100.
/// </summary>
public sealed class IfmO5D100SeedSource : IComponentSource, IComponentSourceMetadata
{
    public static readonly Uri ProductUri = new("https://www.ifm.com/us/en/product/O5D100");
    public static readonly Uri DocumentsUri = new("https://www.ifm.com/us/en/product/O5D100?tab=documents");
    public string SourceName => "IFM O5D100 Offline Seed";
    public IReadOnlyCollection<string> SupportedManufacturers => ["IFM"];
    public bool CanHandle(string manufacturer, string model) =>
        string.Equals(ManufacturerNormalizer.NormalizeKey(manufacturer), "IFM", StringComparison.Ordinal) &&
        string.Equals(ModelNormalizer.Normalize(model)?.Canonical, "O5D100", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manufacturer = ManufacturerNormalizer.NormalizeKey(query.NormalizedManufacturer ?? query.RawManufacturer);
        var model = ModelNormalizer.Normalize(query.NormalizedModel ?? query.RawModel)?.Canonical;
        if (manufacturer is null || model is null || !CanHandle(manufacturer, model))
            return Task.FromResult<IReadOnlyList<ComponentCandidate>>(Array.Empty<ComponentCandidate>());

        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage,
            SourceUrl = ProductUri,
            ExtractionMethod = ExtractionMethod.Html,
            RawValue = "IFM O5D100",
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        IReadOnlyList<ComponentCandidate> result =
        [
            new ComponentCandidate
            {
                Manufacturer = "IFM", OfficialModel = "O5D100", Mpn = "O5D100",
                SourceType = ComponentSourceType.ManufacturerProductPage,
                ProductUrl = ProductUri, RawSourceTitle = "IFM O5D100", Evidence = [evidence]
            }
        ];
        return Task.FromResult(result);
    }

    public Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CanHandle(identity.OfficialManufacturer, identity.OfficialModel)
            ? new ProductPage { Url = ProductUri, RawContent = null }
            : null);
    }

    public Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ComponentDocument> result = CanHandle(identity.OfficialManufacturer, identity.OfficialModel)
            ? [new ComponentDocument { Type = "datasheet-index", Url = DocumentsUri, SourceType = ComponentSourceType.ManufacturerProductPage }]
            : Array.Empty<ComponentDocument>();
        return Task.FromResult(result);
    }

    public Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanHandle(identity.OfficialManufacturer, identity.OfficialModel)) return Task.FromResult(new RawComponentData());
        var specs = new[]
        {
            Spec("Operating voltage", "10...30 V DC", "power.operating_voltage"),
            Spec("Output type", "PNP", "io.output_type"),
            Spec("Connector family", "M12", "connector.family"),
            Spec("Connector coding", "A", "connector.coding"),
            Spec("Connector pin count", "4", "connector.pin_count"),
            Spec("Category", "sensor", "classification.category"),
            Spec("Subcategory", "photoelectric_sensor", "classification.subcategory")
        };
        return Task.FromResult(new RawComponentData { Specifications = specs });
    }

    private static RawSpecification Spec(string name, string value, string key)
    {
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.User, SourceUrl = ProductUri, DocumentUrl = DocumentsUri,
            ExtractionMethod = ExtractionMethod.UserInput, RawValue = value,
            RetrievedAt = DateTimeOffset.UtcNow, VerificationStatus = VerificationStatus.SingleSource
        };
        return new RawSpecification { RawName = name, RawValue = value, ProposedKey = key, Status = VerificationStatus.SingleSource, Evidence = [evidence] };
    }
}
