using ComponentIntelligence.Contracts;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Search;

/// <summary>
/// Production desktop lookup path for the zero-cost central engineering library.
/// The central store is read-only from the desktop. A successful lookup hydrates Component IR into
/// local SQLite for Topology/Layout. No web search, PDF download, parser, browser automation, or
/// automatic central-library write occurs in this workflow.
/// </summary>
public sealed class CentralLibraryComponentLookupService
{
    private readonly IComponentRepository _localCache;
    private readonly IComponentKnowledgeStore _central;

    public CentralLibraryComponentLookupService(IComponentRepository localCache, IComponentKnowledgeStore central)
    {
        _localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
        _central = central ?? throw new ArgumentNullException(nameof(central));
    }

    public async Task<PipelineResult> LookupAsync(BomRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        var manufacturer = ManufacturerNormalizer.NormalizeKey(row.Manufacturer ?? row.RawManufacturer);
        var normalizedModel = ModelNormalizer.Normalize(row.ModelOrPartNumber ?? row.RawModelOrPartNumber);
        if (IdentityPlaceholderDetector.TryGetReason(manufacturer, normalizedModel?.Canonical, out var identityReason))
            return new PipelineResult(
                ResolutionStatus.WaitingForInput,
                null,
                null,
                null,
                false,
                [.. row.ValidationFlags, identityReason, "CENTRAL_LIBRARY_ONLY_MODE"]);

        if (!_central.IsEnabled)
            return new PipelineResult(
                ResolutionStatus.Failed,
                null,
                null,
                null,
                false,
                ["CENTRAL_LIBRARY_REQUIRED", "CENTRAL_WORKBOOK_DISABLED_OR_MISSING", "ONLINE_SEARCH_DISABLED"]);

        var lookup = await _central.FindByIdentityAsync(manufacturer!, normalizedModel!.Canonical, cancellationToken);
        var diagnostics = lookup.Diagnostics.ToList();
        diagnostics.Add("CENTRAL_LIBRARY_ONLY_MODE");
        diagnostics.Add("ONLINE_SEARCH_DISABLED");
        diagnostics.Add("PDF_AUTO_ENRICHMENT_DISABLED");
        diagnostics.Add("CENTRAL_LIBRARY_DESKTOP_READ_ONLY");

        if (lookup.Component is null)
        {
            var readFailed = diagnostics.Any(item =>
                item.StartsWith("CENTRAL_WORKBOOK_READ_FAILED", StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith("CENTRAL_WORKBOOK_SCHEMA_INVALID", StringComparison.OrdinalIgnoreCase));
            diagnostics.Add(readFailed ? "CENTRAL_LIBRARY_LOOKUP_FAILED" : "CENTRAL_LIBRARY_COMPONENT_NOT_FOUND");
            if (!readFailed)
            {
                diagnostics.Add("MANUAL_OFFICIAL_PDF_REQUIRED");
                diagnostics.Add("GPT_ARCHIVE_UPDATE_REQUIRED");
            }

            return new PipelineResult(
                readFailed ? ResolutionStatus.Failed : ResolutionStatus.NotFound,
                null,
                null,
                null,
                false,
                diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var acceptedPins = PinEngineeringValidationPolicy.AcceptedPins(lookup.Component.Pins);
        var rejectedPins = lookup.Component.Pins.Count - acceptedPins.Count;
        var component = rejectedPins > 0
            ? lookup.Component with { Pins = acceptedPins }
            : lookup.Component;

        var topology = TopologyKnowledgePolicy.Evaluate(component);
        component = component with
        {
            Readiness = component.Readiness with
            {
                Topology = topology.Status,
                Wiring = topology.Status == ReadinessStatus.NotReady && component.Readiness.Wiring == ReadinessStatus.Ready
                    ? ReadinessStatus.Partial
                    : component.Readiness.Wiring
            }
        };

        await _localCache.SaveAsync(component, cancellationToken);
        diagnostics.Add("CENTRAL_LIBRARY_HYDRATED_LOCAL_SQLITE");
        if (rejectedPins > 0)
            diagnostics.Add($"CENTRAL_LIBRARY_PIN_ENGINEERING_GATE_REJECTED:{rejectedPins}");
        diagnostics.AddRange(topology.Issues);

        var gaps = KnowledgeCompletenessPolicy.Assess(component);
        var required = gaps.Count(gap => gap.Priority == KnowledgeGapPriority.Required);
        var recommended = gaps.Count - required;
        diagnostics.Add($"KNOWLEDGE_GAPS_REQUIRED:{required}");
        diagnostics.Add($"KNOWLEDGE_GAPS_RECOMMENDED:{recommended}");
        if (required > 0)
        {
            diagnostics.Add("MANUAL_OFFICIAL_PDF_REQUIRED");
            diagnostics.Add("GPT_ARCHIVE_UPDATE_REQUIRED");
        }
        else
        {
            diagnostics.Add("CENTRAL_LIBRARY_REQUIRED_ENGINEERING_FIELDS_COMPLETE");
        }

        return new PipelineResult(
            ResolutionStatus.Resolved,
            component,
            null,
            null,
            false,
            diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<ComponentSearchResult> SearchAsync(
        string? manufacturer,
        string? model,
        CancellationToken cancellationToken = default)
    {
        var row = new BomRow
        {
            RowId = "CENTRAL-LIBRARY-LOOKUP",
            RawManufacturer = manufacturer,
            RawModelOrPartNumber = model,
            Manufacturer = manufacturer?.Trim(),
            ModelOrPartNumber = model?.Trim(),
            UsedQuantity = 1,
            TotalQuantity = 1,
            SpareQuantity = 0,
            Notes = "Central-library component lookup"
        };

        var result = await LookupAsync(row, cancellationToken);
        return new ComponentSearchResult(row, result);
    }
}
