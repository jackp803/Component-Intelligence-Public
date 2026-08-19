using ComponentIntelligence.Contracts;
using ComponentIntelligence.Enrichment;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Pipeline;

public sealed record PipelineResult(
    ResolutionStatus ResolutionStatus,
    ComponentIR? Component,
    RawComponentProfile? Raw,
    VerificationSummary? Verification,
    bool LocalRepositoryHit,
    IReadOnlyList<string> Issues);

public sealed class ComponentIntelligencePipeline
{
    private readonly IComponentRepository _repository;
    private readonly IComponentResolver _resolver;
    private readonly IComponentEnricher _enricher;
    private readonly IComponentNormalizer _normalizer;
    private readonly IVerificationEngine _verification;
    private readonly IComponentKnowledgeStore? _centralKnowledge;

    public ComponentIntelligencePipeline(
        IComponentRepository repository,
        IComponentResolver resolver,
        IComponentEnricher enricher,
        IComponentNormalizer normalizer,
        IVerificationEngine verification,
        IComponentKnowledgeStore? centralKnowledge = null)
    {
        _repository = repository;
        _resolver = resolver;
        _enricher = enricher;
        _normalizer = normalizer;
        _verification = verification;
        _centralKnowledge = centralKnowledge;
    }

    public Task<PipelineResult> ProcessAsync(BomRow row, CancellationToken cancellationToken = default) =>
        ProcessAsync(row, forceRefresh: false, enrichIncompleteExistingKnowledge: true, cancellationToken);

    public Task<PipelineResult> ProcessAsync(BomRow row, bool forceRefresh, CancellationToken cancellationToken = default) =>
        ProcessAsync(row, forceRefresh, enrichIncompleteExistingKnowledge: true, cancellationToken);

    /// <summary>
    /// Processes a component lookup/import.
    /// BOM and workflow callers keep enrichIncompleteExistingKnowledge=true so incomplete knowledge can be completed.
    /// A normal interactive Search passes false: an existing central/local knowledge hit is returned immediately and
    /// does not silently trigger network/PDF enrichment. Deep Search still sets forceRefresh=true explicitly.
    /// </summary>
    public async Task<PipelineResult> ProcessAsync(
        BomRow row,
        bool forceRefresh,
        bool enrichIncompleteExistingKnowledge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var manufacturer = ManufacturerNormalizer.NormalizeKey(row.Manufacturer ?? row.RawManufacturer);
        var normalizedModel = ModelNormalizer.Normalize(row.ModelOrPartNumber ?? row.RawModelOrPartNumber);
        if (IdentityPlaceholderDetector.TryGetReason(manufacturer, normalizedModel?.Canonical, out var identityReason))
            return new PipelineResult(ResolutionStatus.WaitingForInput, null, null, null, false, [.. row.ValidationFlags, identityReason]);

        var centralDiagnostics = new List<string>();
        var centralKnowledgeHit = false;
        var localRepositoryHit = false;
        ComponentIR? local = null;

        // Authority order:
        //   1) Notion central knowledge (when configured)
        //   2) Local SQLite runtime/offline cache
        //   3) Manufacturer/online resolution and enrichment
        // A normal interactive Search may stop at step 1/2 even when topology is incomplete; enrichment is then an
        // explicit Deep Search action. BOM/workflow processing keeps the historical completion behavior.
        if (!forceRefresh && _centralKnowledge?.IsEnabled == true)
        {
            var central = await _centralKnowledge.FindByIdentityAsync(manufacturer!, normalizedModel!.Canonical, cancellationToken);
            centralDiagnostics.AddRange(central.Diagnostics);
            if (central.Component is not null)
            {
                local = ApplyPinEngineeringGate(central.Component, centralDiagnostics, "NOTION_CENTRAL");
                centralKnowledgeHit = true;
                await _repository.SaveAsync(local, cancellationToken);
                centralDiagnostics.Add("NOTION_CENTRAL_HYDRATED_LOCAL_CACHE");
            }
            else
            {
                centralDiagnostics.Add("NOTION_CENTRAL_FALLBACK_TO_LOCAL_SQLITE");
            }
        }

        if (local is null)
        {
            local = await _repository.FindByIdentityAsync(manufacturer!, normalizedModel!.Canonical, cancellationToken);
            localRepositoryHit = local is not null;
            if (localRepositoryHit && !forceRefresh)
                centralDiagnostics.Add(_centralKnowledge?.IsEnabled == true
                    ? "LOCAL_SQLITE_HIT_AFTER_NOTION"
                    : "LOCAL_SQLITE_HIT_NO_NOTION");

            if (local is not null)
            {
                var filtered = ApplyPinEngineeringGate(local, centralDiagnostics, "LOCAL_SQLITE");
                if (filtered.Pins.Count != local.Pins.Count)
                    await _repository.SaveAsync(filtered, cancellationToken);
                local = filtered;
            }
        }

        RawComponentProfile? localRaw = null;
        VerificationSummary? localVerification = null;
        ComponentIR? verifiedLocal = null;
        TopologyKnowledgeAssessment? localTopology = null;

        if (local is not null)
        {
            localRaw = SnapshotFromComponent(local);
            var baseVerification = await _verification.VerifyAsync(local, localRaw, cancellationToken);
            localTopology = TopologyKnowledgePolicy.Evaluate(local);
            localVerification = ApplyTopologyPolicy(baseVerification, localTopology);
            verifiedLocal = local with { Readiness = localVerification.Readiness };

            if (!forceRefresh && (localTopology.IsReady || !enrichIncompleteExistingKnowledge))
            {
                var reuseDiagnostics = centralDiagnostics.ToList();
                if (centralKnowledgeHit)
                    reuseDiagnostics.Add(localTopology.IsReady ? "NOTION_CENTRAL_TOPOLOGY_READY" : "NOTION_CENTRAL_SEARCH_HIT_NO_ENRICHMENT");
                else if (localRepositoryHit)
                    reuseDiagnostics.Add(ResolutionDiagnostics.LocalRepositoryHit);

                if (localTopology.IsReady)
                    reuseDiagnostics.Add("EXISTING_KNOWLEDGE_TOPOLOGY_READY");
                else
                {
                    reuseDiagnostics.Add("EXISTING_KNOWLEDGE_RETURNED_WITHOUT_ENRICHMENT");
                    reuseDiagnostics.Add("EXPLICIT_DEEP_SEARCH_REQUIRED_FOR_REFRESH");
                }

                return new PipelineResult(
                    ResolutionStatus.Resolved,
                    verifiedLocal,
                    localRaw,
                    localVerification,
                    localRepositoryHit,
                    reuseDiagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }
        }

        var localNeedsEnrichment = local is not null && localTopology?.IsReady != true;
        var legacyCacheDetected = local is not null && !HasKnowledgeSnapshot(local);
        var resolution = await _resolver.ResolveAsync(new ComponentIdentityQuery
        {
            RawManufacturer = row.RawManufacturer ?? row.Manufacturer,
            RawModel = row.RawModelOrPartNumber ?? row.ModelOrPartNumber,
            NormalizedManufacturer = manufacturer,
            NormalizedModel = normalizedModel!.Canonical,
            SearchKey = normalizedModel.SearchKey
        }, cancellationToken);

        if (resolution.Status != ResolutionStatus.Resolved || resolution.ResolvedIdentity is null)
        {
            var issues = row.ValidationFlags
                .Concat(centralDiagnostics)
                .Concat(resolution.Diagnostics)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (legacyCacheDetected) issues.Add("LEGACY_LOCAL_CACHE_REQUIRES_REFRESH_BUT_ONLINE_RESOLUTION_FAILED");
            if (localNeedsEnrichment) issues.Add("EXISTING_KNOWLEDGE_NOT_TOPOLOGY_READY_AND_ONLINE_RESOLUTION_FAILED");
            if (centralKnowledgeHit) issues.Add("NOTION_CENTRAL_KNOWLEDGE_NOT_TOPOLOGY_READY");
            if (forceRefresh) issues.Add("DEEP_SEARCH_REFRESH_FAILED");
            if (issues.Count == 0) issues.Add($"RESOLUTION_{resolution.Status.ToString().ToUpperInvariant()}");
            return new PipelineResult(
                resolution.Status,
                verifiedLocal ?? local,
                localRaw,
                localVerification,
                localRepositoryHit,
                issues);
        }

        RawComponentProfile onlineRaw;
        using (DocumentIdentityContext.Push(resolution.ResolvedIdentity))
            onlineRaw = await _enricher.EnrichAsync(resolution.ResolvedIdentity, cancellationToken);

        var raw = localRaw is null ? onlineRaw : MergeProfiles(localRaw, onlineRaw);
        var normalizedComponent = await _normalizer.NormalizeAsync(raw, cancellationToken);
        var rejectedOnlinePins = normalizedComponent.Pins.Count(pin => !PinEngineeringValidationPolicy.IsAccepted(pin));
        var component = rejectedOnlinePins == 0
            ? normalizedComponent
            : normalizedComponent with { Pins = PinEngineeringValidationPolicy.AcceptedPins(normalizedComponent.Pins) };

        var baseOnlineVerification = await _verification.VerifyAsync(component, raw, cancellationToken);
        var topology = TopologyKnowledgePolicy.Evaluate(component);
        var verification = ApplyTopologyPolicy(baseOnlineVerification, topology);
        component = component with { Readiness = verification.Readiness };
        await _repository.SaveAsync(component, cancellationToken);

        var diagnostics = verification.Issues
            .Concat(centralDiagnostics)
            .Concat(resolution.Diagnostics)
            .ToList();
        if (rejectedOnlinePins > 0)
            diagnostics.Add($"PIN_ENGINEERING_GATE_REJECTED:{rejectedOnlinePins}");
        if (_centralKnowledge?.IsEnabled == true)
        {
            var centralWrite = await _centralKnowledge.UpsertAsync(component, cancellationToken);
            diagnostics.AddRange(centralWrite.Diagnostics);
        }
        if (legacyCacheDetected) diagnostics.Add("LEGACY_LOCAL_CACHE_REFRESHED");
        if (localNeedsEnrichment) diagnostics.Add("EXISTING_KNOWLEDGE_ENRICHMENT_ATTEMPTED");
        if (centralKnowledgeHit) diagnostics.Add("NOTION_CENTRAL_KNOWLEDGE_ENRICHED");
        if (forceRefresh) diagnostics.Add("DEEP_SEARCH_REFRESHED");
        if (!topology.IsReady)
        {
            diagnostics.Add("TOPOLOGY_READINESS_INCOMPLETE_AFTER_ENRICHMENT");
            diagnostics.Add("MANUAL_KNOWLEDGE_OR_ADDITIONAL_DOCUMENT_REQUIRED");
        }
        else
        {
            diagnostics.Add("TOPOLOGY_READINESS_READY");
        }

        return new PipelineResult(
            ResolutionStatus.Resolved,
            component,
            raw,
            verification,
            false,
            diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static ComponentIR ApplyPinEngineeringGate(ComponentIR component, ICollection<string> diagnostics, string sourcePrefix)
    {
        var acceptedPins = PinEngineeringValidationPolicy.AcceptedPins(component.Pins);
        var rejected = component.Pins.Count - acceptedPins.Count;
        if (rejected <= 0) return component;
        diagnostics.Add($"{sourcePrefix}_PIN_ENGINEERING_GATE_REJECTED:{rejected}");
        return component with { Pins = acceptedPins };
    }

    private static VerificationSummary ApplyTopologyPolicy(
        VerificationSummary verification,
        TopologyKnowledgeAssessment topology)
    {
        var readiness = verification.Readiness with
        {
            Topology = topology.Status,
            Wiring = topology.Status == ReadinessStatus.Ready
                ? verification.Readiness.Wiring
                : verification.Readiness.Wiring == ReadinessStatus.NotReady
                    ? ReadinessStatus.NotReady
                    : ReadinessStatus.Partial
        };

        return verification with
        {
            Readiness = readiness,
            Issues = verification.Issues
                .Concat(topology.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static bool HasKnowledgeSnapshot(ComponentIR component) =>
        component.Specifications.Any(spec => !string.IsNullOrWhiteSpace(spec.Value)) ||
        component.Documents.Count > 0 ||
        component.Pins.Count > 0 ||
        component.Ports.Count > 0;

    private static RawComponentProfile MergeProfiles(RawComponentProfile local, RawComponentProfile online)
    {
        var specifications = local.Specifications
            .Concat(online.Specifications)
            .Where(spec => !string.IsNullOrWhiteSpace(spec.RawName) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .GroupBy(spec => $"{spec.Section}\u001f{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray()
            })
            .ToArray();

        var pins = local.Pins
            .Concat(online.Pins)
            .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group
                    .OrderByDescending(pin => !string.IsNullOrWhiteSpace(pin.Function))
                    .ThenByDescending(pin => SourceTrustPolicy.Score(pin.Evidence))
                    .First();
                var functions = group
                    .Select(pin => pin.Function)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return preferred with
                {
                    Function = functions.Length <= 1 ? preferred.Function : string.Join(" | ", functions),
                    Evidence = group.SelectMany(pin => pin.Evidence).Distinct().ToArray()
                };
            })
            .ToArray();

        var ports = local.Ports
            .Concat(online.Ports)
            .GroupBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(PortKnowledgeScore)
                .First())
            .ToArray();

        var documents = local.Documents
            .Concat(online.Documents)
            .GroupBy(document => document.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(document => SourceTrustPolicy.Score(document.SourceType))
                .ThenByDescending(document => document.Sha256 is not null)
                .First())
            .ToArray();

        var assets = local.Assets
            .Concat(online.Assets)
            .GroupBy(asset => $"{asset.Type}\u001f{asset.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new RawComponentProfile
        {
            Identity = online.Identity,
            Specifications = specifications,
            Ports = ports,
            Pins = pins,
            Documents = documents,
            Assets = assets,
            Evidence = local.Evidence.Concat(online.Evidence).Distinct().ToArray(),
            MissingData = local.MissingData.Concat(online.MissingData).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static int PortKnowledgeScore(ComponentPort port)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(port.PortType)) score++;
        if (!string.IsNullOrWhiteSpace(port.ConnectorFamily)) score++;
        if (!string.IsNullOrWhiteSpace(port.SignalType)) score++;
        if (!string.IsNullOrWhiteSpace(port.Direction)) score++;
        if (!string.IsNullOrWhiteSpace(port.VoltageDomain)) score++;
        if (!string.IsNullOrWhiteSpace(port.Protocol)) score++;
        score += port.AllowedConnections.Count;
        return score;
    }

    private static RawComponentProfile SnapshotFromComponent(ComponentIR component)
    {
        var identity = new ComponentIdentity
        {
            OfficialManufacturer = component.Identity.Manufacturer,
            OfficialModel = component.Identity.Model,
            Mpn = component.Identity.Mpn,
            OfficialProductUrl = component.Assets.ProductPageUrl
        };
        var specs = component.Specifications.Select(spec => new RawSpecification
        {
            RawName = spec.Name,
            Section = spec.Section,
            RawValue = spec.Value,
            ProposedKey = spec.Key,
            Status = spec.Status,
            Evidence = spec.Evidence
        }).ToArray();
        var assets = new List<ComponentAsset>();
        if (component.Assets.ProductPageUrl is not null)
            assets.Add(new ComponentAsset { Type = "product-page", Url = component.Assets.ProductPageUrl });
        if (component.Assets.DatasheetUrl is not null)
            assets.Add(new ComponentAsset { Type = "datasheet", Url = component.Assets.DatasheetUrl });
        if (component.Assets.ImageUrl is not null)
            assets.Add(new ComponentAsset { Type = "image", Url = component.Assets.ImageUrl });
        if (component.Assets.CadUrl is not null)
            assets.Add(new ComponentAsset { Type = "cad", Url = component.Assets.CadUrl });
        return new RawComponentProfile
        {
            Identity = identity,
            Specifications = specs,
            Ports = component.Ports,
            Pins = component.Pins,
            Documents = component.Documents,
            Assets = assets,
            Evidence = specs.SelectMany(spec => spec.Evidence).Distinct().ToArray(),
            MissingData = Array.Empty<string>()
        };
    }
}
