using ComponentIntelligence.Contracts;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingPlanningRuntimeFactory
{
    public static DrawingPlanningInputBuilder Create(string? archiveRootOrWorkbook, IReadOnlyList<ComponentIR> catalog)
    {
        if (string.IsNullOrWhiteSpace(archiveRootOrWorkbook))
            return new DrawingPlanningInputBuilder(new RepresentationPolicy(new UnconfiguredAssets()));
        var repository = new SymbolArchiveRepository(archiveRootOrWorkbook);
        _ = repository.Load();
        return new DrawingPlanningInputBuilder(new RepresentationPolicy(
            new Cp3aDrawingAssetResolver(new SymbolResolver(repository, catalog), repository)), catalog);
    }

    private sealed class UnconfiguredAssets : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => null;
    }
}
