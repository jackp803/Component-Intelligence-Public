using ComponentIntelligence.Contracts;
using ComponentIntelligence.Pipeline;

namespace ComponentIntelligence.Search;

public sealed record ComponentSearchResult(BomRow Query, PipelineResult Result);

public sealed class ComponentSearchService
{
    private readonly ComponentIntelligencePipeline _pipeline;
    public ComponentSearchService(ComponentIntelligencePipeline pipeline) => _pipeline = pipeline;

    public async Task<ComponentSearchResult> SearchAsync(
        string? manufacturer,
        string? model,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var row = new BomRow
        {
            RowId = "SEARCH",
            RawManufacturer = manufacturer,
            RawModelOrPartNumber = model,
            Manufacturer = manufacturer?.Trim(),
            ModelOrPartNumber = model?.Trim(),
            UsedQuantity = 1,
            TotalQuantity = 1,
            SpareQuantity = 0,
            Notes = forceRefresh ? "Manual component deep search" : "Manual component search"
        };

        // Normal Search is a read-through lookup: if central/local knowledge already exists, return it
        // without silently starting network/PDF enrichment. Deep Search is the explicit refresh action.
        var result = await _pipeline.ProcessAsync(
            row,
            forceRefresh,
            enrichIncompleteExistingKnowledge: forceRefresh,
            cancellationToken);
        return new ComponentSearchResult(row, result);
    }
}
