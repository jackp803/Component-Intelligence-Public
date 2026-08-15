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
        return new ComponentSearchResult(row, await _pipeline.ProcessAsync(row, forceRefresh, cancellationToken));
    }
}
