using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Sources;

namespace ComponentIntelligence.Resolution;

public sealed class ComponentResolver : IComponentResolver
{
    private readonly IComponentRepository _repository;
    private readonly IReadOnlyList<IComponentSource> _sources;

    public ComponentResolver(IComponentRepository repository, IEnumerable<IComponentSource> sources)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();
    }

    public async Task<ResolutionResult> ResolveAsync(
        ComponentIdentityQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var manufacturer = ManufacturerNormalizer.NormalizeKey(query.NormalizedManufacturer ?? query.RawManufacturer);
        var model = ModelNormalizer.Normalize(query.NormalizedModel ?? query.RawModel);
        var normalized = query with
        {
            NormalizedManufacturer = manufacturer,
            NormalizedModel = model?.Canonical,
            SearchKey = model?.SearchKey
        };

        if (IdentityPlaceholderDetector.TryGetReason(manufacturer, model?.Canonical, out var placeholderReason))
            return Result(ResolutionStatus.WaitingForInput, MatchLevel.None, normalized, diagnostics: [placeholderReason]);

        var local = await _repository.FindByIdentityAsync(manufacturer!, model!.Canonical, cancellationToken);
        if (local is not null)
        {
            return new ResolutionResult
            {
                Status = ResolutionStatus.Resolved,
                MatchLevel = MatchLevel.Exact,
                Input = normalized,
                ResolvedIdentity = new ComponentIdentity
                {
                    OfficialManufacturer = local.Identity.Manufacturer,
                    OfficialModel = local.Identity.Model,
                    Mpn = local.Identity.Mpn,
                    OfficialProductUrl = local.Assets.ProductPageUrl
                },
                Diagnostics = [ResolutionDiagnostics.LocalRepositoryHit]
            };
        }

        var compatibleSources = _sources
            .Where(source => source is not ISecondaryEnrichmentSource)
            .Where(source => source.CanHandle(manufacturer!, model.Canonical))
            .ToArray();
        if (compatibleSources.Length == 0)
        {
            return Result(
                ResolutionStatus.NotFound,
                MatchLevel.None,
                normalized,
                diagnostics: [ResolutionDiagnostics.WithValue(ResolutionDiagnostics.UnsupportedManufacturer, manufacturer)]);
        }

        var candidates = new List<ComponentCandidate>();
        var diagnostics = new List<string>();
        foreach (var source in compatibleSources)
        {
            try
            {
                candidates.AddRange(await source.SearchAsync(normalized, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add($"SOURCE_ERROR:{source.DisplayName()}:{exception.GetType().Name}:{exception.Message}");
            }
        }

        var exact = candidates.Where(candidate =>
            string.Equals(ManufacturerNormalizer.NormalizeKey(candidate.Manufacturer), manufacturer, StringComparison.Ordinal) &&
            string.Equals(ModelNormalizer.Normalize(candidate.OfficialModel)?.Canonical, model.Canonical, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exact.Length == 0)
        {
            diagnostics.Add(diagnostics.Any(item => item.StartsWith("SOURCE_ERROR:", StringComparison.Ordinal))
                ? ResolutionDiagnostics.SearchFailed
                : ResolutionDiagnostics.ProductNotFound);
            return Result(ResolutionStatus.NotFound, MatchLevel.None, normalized, candidates, diagnostics);
        }

        var selected = exact
            .OrderByDescending(CandidateQuality)
            .ThenBy(candidate => candidate.ProductUrl?.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .First() with
        {
            Evidence = exact.SelectMany(candidate => candidate.Evidence).Distinct().ToArray()
        };

        if (exact.Length > 1)
            diagnostics.Add($"MULTIPLE_EXACT_EVIDENCE_MERGED:{exact.Length}");

        return new ResolutionResult
        {
            Status = ResolutionStatus.Resolved,
            MatchLevel = MatchLevel.Exact,
            Input = normalized,
            ResolvedIdentity = new ComponentIdentity
            {
                OfficialManufacturer = ManufacturerNormalizer.NormalizeKey(selected.Manufacturer) ?? selected.Manufacturer,
                OfficialModel = selected.OfficialModel,
                Mpn = selected.Mpn,
                OfficialProductUrl = selected.ProductUrl
            },
            Candidates = candidates,
            Evidence = selected.Evidence,
            Diagnostics = diagnostics
        };
    }

    private static int CandidateQuality(ComponentCandidate candidate)
    {
        if (candidate.ProductUrl is null) return 0;
        var uri = candidate.ProductUrl;
        var path = uri.AbsolutePath;
        var score = 20;
        if (path.Contains("/product/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/products/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/p/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("product_detail", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/family/", StringComparison.OrdinalIgnoreCase))
            score += 80;
        if (path.Contains("search", StringComparison.OrdinalIgnoreCase) || uri.Query.Contains("search", StringComparison.OrdinalIgnoreCase))
            score -= 30;
        return score;
    }

    private static ResolutionResult Result(
        ResolutionStatus status,
        MatchLevel match,
        ComponentIdentityQuery input,
        IReadOnlyList<ComponentCandidate>? candidates = null,
        IReadOnlyList<string>? diagnostics = null) => new()
    {
        Status = status,
        MatchLevel = match,
        Input = input,
        Candidates = candidates ?? Array.Empty<ComponentCandidate>(),
        Diagnostics = diagnostics ?? Array.Empty<string>()
    };
}
