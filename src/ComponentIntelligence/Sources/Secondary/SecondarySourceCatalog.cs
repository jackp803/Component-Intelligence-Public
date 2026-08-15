using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Network;

namespace ComponentIntelligence.Sources.Secondary;

public static class SecondarySourceCatalog
{
    public static IReadOnlyList<IComponentSource> Create(
        ComponentHttpClient http,
        SpecificationParser parser,
        DocumentPipeline documents,
        IRenderedPageFetcher? rendered = null) =>
    [
        new SecondaryCatalogSource(
            new SecondaryCatalogSourceDefinition(
                "DigiKey Authorized Distributor",
                ComponentSourceType.AuthorizedDistributor,
                ["digikey.com", "digikey.tw", "digikey.ca"],
                model =>
                [
                    new Uri($"https://www.digikey.com/en/products/result?keywords={Escape(model)}"),
                    new Uri($"https://www.digikey.tw/en/products/result?keywords={Escape(model)}")
                ]),
            http, parser, documents, rendered),

        new SecondaryCatalogSource(
            new SecondaryCatalogSourceDefinition(
                "RS Authorized Distributor",
                ComponentSourceType.AuthorizedDistributor,
                ["rs-online.com"],
                model =>
                [
                    new Uri($"https://twcn.rs-online.com/web/c/?searchTerm={Escape(model)}")
                ]),
            http, parser, documents, rendered),

        new SecondaryCatalogSource(
            new SecondaryCatalogSourceDefinition(
                "Mouser Authorized Distributor",
                ComponentSourceType.AuthorizedDistributor,
                ["mouser.com", "mouser.tw"],
                model =>
                [
                    new Uri($"https://www.mouser.com/c/?q={Escape(model)}"),
                    new Uri($"https://www.mouser.tw/c/?q={Escape(model)}")
                ]),
            http, parser, documents, rendered),

        // Aggregators are useful corroboration/document-discovery sources but are deliberately ranked
        // below manufacturer and authorized-distributor evidence. They never resolve official identity.
        new SecondaryCatalogSource(
            new SecondaryCatalogSourceDefinition(
                "Octopart Component Aggregator",
                ComponentSourceType.TrustedThirdParty,
                ["octopart.com"],
                model =>
                [
                    new Uri($"https://octopart.com/search?q={Escape(model)}")
                ]),
            http, parser, documents, rendered),

        new SecondaryCatalogSource(
            new SecondaryCatalogSourceDefinition(
                "FindChips Component Aggregator",
                ComponentSourceType.TrustedThirdParty,
                ["findchips.com"],
                model =>
                [
                    new Uri($"https://www.findchips.com/search/{Escape(model)}"),
                    new Uri($"https://www.findchips.com/detail/{Escape(model)}")
                ]),
            http, parser, documents, rendered),

        // Last-resort public web discovery. Search-engine results are never identity evidence; they are
        // only used to locate openly reachable PDF/manual candidates after stronger sources are sparse.
        new WebPdfDiscoverySource(http, documents, rendered)
    ];

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());
}
