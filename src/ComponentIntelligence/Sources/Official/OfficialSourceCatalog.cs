using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;

namespace ComponentIntelligence.Sources.Official;

public static class OfficialSourceCatalog
{
    public static IReadOnlyList<IComponentSource> Create(
        ComponentHttpClient http,
        SpecificationParser parser,
        DocumentPipeline documents,
        IRenderedPageFetcher? rendered = null) =>
    [
        CreateSource(new(
            "OMRON Official Product Search",
            "OMRON",
            ["OMRON", "OMRON CORPORATION"],
            ["ia.omron.com"],
            model => [new Uri($"https://www.ia.omron.com/search/product/?ie=UTF-8&keyword={Escape(model)}")]), http, parser, documents, rendered),

        CreateSource(new(
            "WAGO Official Catalog",
            "WAGO",
            ["WAGO", "WAGO KONTAKTTECHNIK"],
            ["wago.com"],
            model =>
            [
                new Uri($"https://www.wago.com/global/p/{EscapePath(model)}"),
                new Uri($"https://www.wago.com/global/search?q={Escape(model)}"),
                new Uri($"https://www.wago.com/global/search?text={Escape(model)}")
            ]), http, parser, documents, rendered),

        CreateSource(new(
            "Schneider Electric Official Catalog",
            "SCHNEIDER ELECTRIC",
            ["SCHNEIDER", "SCHNEIDER ELECTRIC"],
            ["se.com"],
            model => [new Uri($"https://www.se.com/us/en/product/{EscapePath(model)}/")]), http, parser, documents, rendered),

        CreateSource(new(
            "MEAN WELL Official Product Search",
            "MEAN WELL",
            ["MEAN WELL", "MEANWELL", "MEAN WELL ENTERPRISES"],
            ["meanwell.com"],
            model => [new Uri($"https://www.meanwell.com/productSearch.aspx?pkeywords={Escape(model)}")]), http, parser, documents, rendered),

        CreateSource(new(
            "Moxa Official Catalog",
            "MOXA",
            ["MOXA", "MOXA INC", "MOXA INC."],
            ["moxa.com"],
            MoxaUris), http, parser, documents, rendered),

        CreateSource(new(
            "Fuji Electric Official Catalog",
            "FUJI ELECTRIC",
            ["FUJI", "FUJI ELECTRIC"],
            ["fujielectric.com"],
            FujiUris,
            model => model.StartsWith("FSZ", StringComparison.OrdinalIgnoreCase)), http, parser, documents, rendered)
    ];

    private static IComponentSource CreateSource(
        OfficialCatalogSourceDefinition definition,
        ComponentHttpClient http,
        SpecificationParser parser,
        DocumentPipeline documents,
        IRenderedPageFetcher? rendered) => new OfficialCatalogSource(definition, http, parser, documents, rendered);

    private static IReadOnlyList<Uri> MoxaUris(string model)
    {
        var lower = model.Trim().ToLowerInvariant();
        var baseModel = lower.EndsWith("-t", StringComparison.Ordinal) ? lower[..^2] : lower;
        var uris = new List<Uri>
        {
            new($"https://www.moxa.com/en/search?keyword={Escape(model)}"),
            new($"https://www.moxa.com/en/search?q={Escape(model)}")
        };
        if (baseModel.StartsWith("eds-", StringComparison.Ordinal) && baseModel.EndsWith("-el", StringComparison.Ordinal))
        {
            uris.Insert(0, new Uri($"https://www.moxa.com/en/products/industrial-network-infrastructure/ethernet-switches/unmanaged-switches/{baseModel}-series/{lower}"));
            uris.Insert(1, new Uri($"https://www.moxa.com/en/products/industrial-network-infrastructure/ethernet-switches/unmanaged-switches/{baseModel}-series"));
        }
        return uris;
    }

    private static IReadOnlyList<Uri> FujiUris(string model)
    {
        if (model.StartsWith("FSZ", StringComparison.OrdinalIgnoreCase))
            return [new Uri("https://www.fujielectric.com/products/sensors_measurements/instruments/product_detail/flow_ultra_fsz.html")];
        return Array.Empty<Uri>();
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());
    private static string EscapePath(string value) => Uri.EscapeDataString(value.Trim()).Replace("%2F", "_", StringComparison.OrdinalIgnoreCase);
}
