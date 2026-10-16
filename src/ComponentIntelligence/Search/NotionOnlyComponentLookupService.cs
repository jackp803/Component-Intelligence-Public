using ComponentIntelligence.Contracts;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Search;

/// <summary>
/// Desktop lookup workflow after the online-search removal.
/// Notion is the only engineering-knowledge authority. The local SQLite repository is only hydrated
/// as a runtime cache for downstream topology/layout features; it is never used as a fallback answer.
/// This service never searches the web, downloads a PDF, parses a PDF, or writes knowledge to Notion.
/// </summary>
public sealed class NotionOnlyComponentLookupService
{
    private readonly IComponentRepository _localCache;
    private readonly IComponentKnowledgeStore _notion;

    public NotionOnlyComponentLookupService(IComponentRepository localCache, IComponentKnowledgeStore notion)
    {
        _localCache = localCache ?? throw new ArgumentNullException(nameof(localCache));
        _notion = notion ?? throw new ArgumentNullException(nameof(notion));
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
                [.. row.ValidationFlags, identityReason, "NOTION_ONLY_MODE"]);

        if (!_notion.IsEnabled)
            return new PipelineResult(
                ResolutionStatus.Failed,
                null,
                null,
                null,
                false,
                ["NOTION_CENTRAL_REQUIRED", "NOTION_CENTRAL_DISABLED_NO_TOKEN", "ONLINE_SEARCH_DISABLED"]);

        var central = await _notion.FindByIdentityAsync(manufacturer!, normalizedModel!.Canonical, cancellationToken);
        var diagnostics = central.Diagnostics.ToList();
        diagnostics.Add("NOTION_ONLY_MODE");
        diagnostics.Add("ONLINE_SEARCH_DISABLED");
        diagnostics.Add("PDF_AUTO_ENRICHMENT_DISABLED");

        if (central.Component is null)
        {
            var readFailed = diagnostics.Any(item => item.StartsWith("NOTION_CENTRAL_READ_FAILED", StringComparison.OrdinalIgnoreCase));
            diagnostics.Add(readFailed ? "NOTION_LOOKUP_FAILED" : "NOTION_COMPONENT_NOT_FOUND");
            if (!readFailed)
            {
                diagnostics.Add("MANUAL_OFFICIAL_PDF_REQUIRED");
                diagnostics.Add("GPT_NOTION_UPDATE_REQUIRED");
            }

            return new PipelineResult(
                readFailed ? ResolutionStatus.Failed : ResolutionStatus.NotFound,
                null,
                null,
                null,
                false,
                diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var acceptedPins = PinEngineeringValidationPolicy.AcceptedPins(central.Component.Pins);
        var rejectedPins = central.Component.Pins.Count - acceptedPins.Count;
        var component = rejectedPins > 0
            ? central.Component with { Pins = acceptedPins }
            : central.Component;

        var topology = TopologyKnowledgePolicy.Evaluate(component);
        component = component with
        {
            Readiness = component.Readiness with
            {
                Topology = topology.Status,
                Wiring = topology.Status == ReadinessStatus.Ready
                    ? component.Readiness.Wiring
                    : component.Readiness.Wiring == ReadinessStatus.NotReady
                        ? ReadinessStatus.NotReady
                        : ReadinessStatus.Partial
            }
        };

        await _localCache.SaveAsync(component, cancellationToken);
        diagnostics.Add("NOTION_CENTRAL_HYDRATED_LOCAL_CACHE");
        if (rejectedPins > 0)
            diagnostics.Add($"NOTION_PIN_ENGINEERING_GATE_REJECTED:{rejectedPins}");
        diagnostics.AddRange(topology.Issues);

        var gaps = KnowledgeCompletenessPolicy.Assess(component);
        var required = gaps.Count(gap => gap.Priority == KnowledgeGapPriority.Required);
        var recommended = gaps.Count - required;
        diagnostics.Add($"KNOWLEDGE_GAPS_REQUIRED:{required}");
        diagnostics.Add($"KNOWLEDGE_GAPS_RECOMMENDED:{recommended}");
        if (required > 0)
        {
            diagnostics.Add("MANUAL_OFFICIAL_PDF_REQUIRED");
            diagnostics.Add("GPT_NOTION_UPDATE_REQUIRED");
        }
        else
        {
            diagnostics.Add("NOTION_REQUIRED_ENGINEERING_FIELDS_COMPLETE");
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
            RowId = "NOTION-LOOKUP",
            RawManufacturer = manufacturer,
            RawModelOrPartNumber = model,
            Manufacturer = manufacturer?.Trim(),
            ModelOrPartNumber = model?.Trim(),
            UsedQuantity = 1,
            TotalQuantity = 1,
            SpareQuantity = 0,
            Notes = "Notion-only component lookup"
        };

        var result = await LookupAsync(row, cancellationToken);
        return new ComponentSearchResult(row, result);
    }
}
