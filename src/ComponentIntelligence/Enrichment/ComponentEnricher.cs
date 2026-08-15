using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Sources;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Enrichment;

public sealed class ComponentEnricher : IComponentEnricher
{
    private readonly IReadOnlyList<IComponentSource> _sources;
    private readonly PinoutExtractor _pinout = new();
    private readonly NetworkEquipmentExtractor _networkEquipment = new();

    public ComponentEnricher(IEnumerable<IComponentSource> sources) =>
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();

    public async Task<RawComponentProfile> EnrichAsync(ComponentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var specs = new List<RawSpecification>();
        var ports = new List<ComponentPort>();
        var pins = new List<ComponentPin>();
        var documents = new List<ComponentDocument>();
        var assets = new List<ComponentAsset>();
        var issues = new List<string>();
        Uri? productPage = identity.OfficialProductUrl;

        var compatibleSources = _sources.Where(source => source.CanHandle(identity.OfficialManufacturer, identity.OfficialModel)).ToArray();
        var primarySources = compatibleSources.Where(source => source is not ISecondaryEnrichmentSource).ToArray();
        var secondarySources = compatibleSources.Where(source => source is ISecondaryEnrichmentSource).ToArray();

        // Fast-path policy requested by the product workflow: once a usable engineering PDF is acquired,
        // stop widening the search to more websites. The current source has already downloaded and parsed
        // the PDF through DocumentPipeline; additional gaps remain visible for later manual review instead
        // of spending more time crawling secondary sources.
        foreach (var source in primarySources)
        {
            await CollectAsync(source, identity, false, specs, ports, pins, documents, assets, issues, value => productPage ??= value, cancellationToken);
            DeriveConnectivity(specs, ports, pins);
            if (HasAcquiredEngineeringPdf(documents))
            {
                issues.Add("PDF_ACQUIRED_ADDITIONAL_SOURCE_SEARCH_SKIPPED");
                break;
            }
        }

        if (!HasAcquiredEngineeringPdf(documents) && ShouldContinueAcquisition(specs, ports, pins, documents))
        {
            foreach (var source in secondarySources)
            {
                await CollectAsync(source, identity, true, specs, ports, pins, documents, assets, issues, _ => { }, cancellationToken);
                DeriveConnectivity(specs, ports, pins);
                if (HasAcquiredEngineeringPdf(documents))
                {
                    issues.Add("PDF_ACQUIRED_ADDITIONAL_SOURCE_SEARCH_SKIPPED");
                    break;
                }
                if (!ShouldContinueAcquisition(specs, ports, pins, documents)) break;
            }
        }

        if (productPage is not null) assets.Add(new ComponentAsset { Type = "product-page", Url = productPage });

        DeriveConnectivity(specs, ports, pins);

        var pinCountSpec = SourceTrustPolicy.BestSpecification(specs, "connector.pin_count");
        var pinCount = int.TryParse(pinCountSpec?.RawValue, out var parsedPins) ? parsedPins : 0;
        if (pinCount > 0)
        {
            var countEvidence = pinCountSpec?.Evidence ?? Array.Empty<Evidence>();
            var knownNumbers = pins.Select(pin => pin.PinNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var number in Enumerable.Range(1, pinCount))
            {
                if (knownNumbers.Contains(number.ToString())) continue;
                pins.Add(new ComponentPin
                {
                    PinNumber = number.ToString(),
                    Function = null,
                    Description = "Pin exists by connector pin-count evidence; function remains unknown until manufacturer wiring evidence is parsed.",
                    Evidence = countEvidence
                });
            }
        }

        var acquisitionGaps = AcquisitionGaps(specs, ports, pins, documents);
        if (acquisitionGaps.Count > 0 && secondarySources.Length > 0 && !HasAcquiredEngineeringPdf(documents))
            issues.Add("KNOWLEDGE_ACQUISITION_EXHAUSTED_WITH_TOPOLOGY_GAPS");

        var required = new[] { "power.operating_voltage", "io.output_type", "connector.family", "connector.pin_count" };
        var missing = required
            .Where(key => string.IsNullOrWhiteSpace(Value(specs, key)))
            .Select(key => $"MISSING:{key}")
            .Concat(acquisitionGaps)
            .Concat(issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var evidenceAll = specs.SelectMany(spec => spec.Evidence).Concat(pins.SelectMany(pin => pin.Evidence)).Distinct().ToArray();
        return new RawComponentProfile
        {
            Identity = identity with { OfficialProductUrl = productPage },
            Specifications = specs
                .Where(spec => !string.IsNullOrWhiteSpace(spec.RawName) && !string.IsNullOrWhiteSpace(spec.RawValue))
                .GroupBy(spec => $"{spec.Section}\u001f{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
                .ToArray(),
            Ports = ports
                .GroupBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(PortKnowledgeScore).First())
                .ToArray(),
            Pins = pins
                .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
                .Select(MergePinGroup)
                .OrderBy(pin => int.TryParse(pin.PinNumber, out var number) ? number : int.MaxValue)
                .ThenBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Documents = documents
                .GroupBy(document => document.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => SourceTrustPolicy.Score(item.SourceType))
                    .ThenByDescending(item => item.Sha256 is not null)
                    .First())
                .ToArray(),
            Assets = assets.GroupBy(asset => $"{asset.Type}\u001f{asset.Url}", StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray(),
            Evidence = evidenceAll,
            MissingData = missing
        };
    }

    private void DeriveConnectivity(
        IReadOnlyCollection<RawSpecification> specs,
        List<ComponentPort> ports,
        List<ComponentPin> pins)
    {
        pins.AddRange(_pinout.Extract(specs));
        ports.AddRange(_networkEquipment.Extract(specs).Ports);
    }

    private bool ShouldContinueAcquisition(
        IReadOnlyCollection<RawSpecification> specs,
        IReadOnlyCollection<ComponentPort> ports,
        IReadOnlyCollection<ComponentPin> pins,
        IReadOnlyCollection<ComponentDocument> documents)
    {
        if (HasAcquiredEngineeringPdf(documents)) return false;

        var topologyReady = HasTopologyReadyKnowledge(specs, ports, pins);
        var hasEngineeringDocument = documents.Any(IsEngineeringDocument);
        return !topologyReady || !hasEngineeringDocument;
    }

    private static bool HasAcquiredEngineeringPdf(IEnumerable<ComponentDocument> documents) =>
        documents.Any(document => IsEngineeringDocument(document) && IsPdf(document));

    private static bool IsEngineeringDocument(ComponentDocument document) =>
        document.SourceType is ComponentSourceType.ManufacturerDatasheet or ComponentSourceType.ManufacturerManual ||
        document.Type.Contains("datasheet", StringComparison.OrdinalIgnoreCase) ||
        document.Type.Contains("data sheet", StringComparison.OrdinalIgnoreCase) ||
        document.Type.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
        document.Type.Contains("technical", StringComparison.OrdinalIgnoreCase) ||
        document.Type.Contains("specification", StringComparison.OrdinalIgnoreCase);

    private static bool IsPdf(ComponentDocument document)
    {
        if (document.Url.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(document.LocalPath) &&
            Path.GetExtension(document.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return true;

        // Dynamic download endpoints often have no .pdf suffix. A SHA means DocumentPipeline actually
        // downloaded the document; for an engineering-document classification that is sufficient to stop
        // widening the external search after the current source has processed it.
        return !string.IsNullOrWhiteSpace(document.Sha256);
    }

    private bool HasTopologyReadyKnowledge(
        IReadOnlyCollection<RawSpecification> specs,
        IReadOnlyCollection<ComponentPort> ports,
        IReadOnlyCollection<ComponentPin> pins)
    {
        var derivedNetwork = _networkEquipment.Extract(specs);
        var combinedPorts = ports
            .Concat(derivedNetwork.Ports)
            .GroupBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(PortKnowledgeScore).First())
            .ToArray();

        if (combinedPorts.Length > 0)
        {
            var networkPorts = combinedPorts.Where(port =>
                string.Equals(port.PortType, "Network", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(port.SignalType, "Communication", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(port.Protocol)).ToArray();

            if (derivedNetwork.NetworkEvidenceDetected)
            {
                var expected = derivedNetwork.ExpectedPortCount;
                var enoughPorts = expected > 0 && networkPorts.Length >= expected;
                var typedPorts = networkPorts.Length > 0 && networkPorts.All(port =>
                    !string.IsNullOrWhiteSpace(port.ConnectorFamily) &&
                    (!string.IsNullOrWhiteSpace(port.Protocol) || !string.IsNullOrWhiteSpace(port.SignalType)));
                if (enoughPorts && typedPorts) return true;
            }
            else if (combinedPorts.All(port =>
                         !string.IsNullOrWhiteSpace(port.ConnectorFamily) &&
                         (!string.IsNullOrWhiteSpace(port.Protocol) ||
                          !string.IsNullOrWhiteSpace(port.SignalType) ||
                          !string.IsNullOrWhiteSpace(port.PortType))))
            {
                return true;
            }
        }

        var connectorKnown = !string.IsNullOrWhiteSpace(Value(specs, "connector.family"));
        var expectedPins = int.TryParse(Value(specs, "connector.pin_count"), out var parsedPins) ? parsedPins : 0;
        var derivedPins = pins
            .Concat(_pinout.Extract(specs))
            .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(pin => !string.IsNullOrWhiteSpace(pin.Function)).First())
            .ToArray();
        var knownFunctions = derivedPins.Count(pin => !string.IsNullOrWhiteSpace(pin.Function));

        return connectorKnown && expectedPins > 0 && derivedPins.Length >= expectedPins && knownFunctions >= expectedPins;
    }

    private IReadOnlyList<string> AcquisitionGaps(
        IReadOnlyCollection<RawSpecification> specs,
        IReadOnlyCollection<ComponentPort> ports,
        IReadOnlyCollection<ComponentPin> pins,
        IReadOnlyCollection<ComponentDocument> documents)
    {
        var gaps = new List<string>();
        if (!HasTopologyReadyKnowledge(specs, ports, pins)) gaps.Add("TOPOLOGY_KNOWLEDGE_INCOMPLETE");
        if (!documents.Any(IsEngineeringDocument))
            gaps.Add("ENGINEERING_DOCUMENT_NOT_ACQUIRED");

        var network = _networkEquipment.Extract(specs);
        if (network.NetworkEvidenceDetected)
        {
            var actual = ports
                .Concat(network.Ports)
                .Where(port => string.Equals(port.PortType, "Network", StringComparison.OrdinalIgnoreCase))
                .Select(port => port.PortId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (actual < network.ExpectedPortCount)
                gaps.Add($"NETWORK_PORT_COVERAGE:{actual}/{network.ExpectedPortCount}");
            if (network.Ports.Any(port => string.IsNullOrWhiteSpace(port.ConnectorFamily)))
                gaps.Add("NETWORK_PORT_CONNECTOR_TYPE_INCOMPLETE");
        }

        return gaps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static ComponentPin MergePinGroup(IGrouping<string, ComponentPin> group)
    {
        var preferred = group
            .OrderByDescending(pin => !string.IsNullOrWhiteSpace(pin.Function))
            .ThenByDescending(pin => SourceTrustPolicy.Score(pin.Evidence))
            .First();
        var functions = group.Select(pin => pin.Function).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return preferred with
        {
            Function = functions.Length <= 1 ? preferred.Function : string.Join(" | ", functions),
            Evidence = group.SelectMany(pin => pin.Evidence).Distinct().ToArray()
        };
    }

    private static async Task CollectAsync(
        IComponentSource source,
        ComponentIdentity identity,
        bool secondary,
        List<RawSpecification> specs,
        List<ComponentPort> ports,
        List<ComponentPin> pins,
        List<ComponentDocument> documents,
        List<ComponentAsset> assets,
        List<string> issues,
        Action<Uri> captureProductPage,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await source.GetProductPageAsync(identity, cancellationToken);
            if (page is not null && !secondary) captureProductPage(page.Url);

            // Most online adapters perform document discovery as part of ExtractAsync because they need
            // to download/parse the documents immediately. Prefer that self-contained result first so
            // JavaScript-heavy sites are not browsed/clicked twice. Legacy/offline sources whose Extract
            // result carries no documents still get one explicit DiscoverDocumentsAsync call.
            var extracted = await source.ExtractAsync(identity, cancellationToken);
            documents.AddRange(extracted.Documents);
            if (extracted.Documents.Count == 0)
                documents.AddRange(await source.DiscoverDocumentsAsync(identity, cancellationToken));

            specs.AddRange(extracted.Specifications);
            ports.AddRange(extracted.Ports);
            pins.AddRange(extracted.Pins);
            assets.AddRange(extracted.Assets);
            issues.AddRange(extracted.Issues);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            issues.Add(secondary
                ? $"SECONDARY_SOURCE_UNAVAILABLE:{source.DisplayName()}:{exception.GetType().Name}:{exception.Message}"
                : $"ENRICHMENT_SOURCE_ERROR:{source.DisplayName()}:{exception.GetType().Name}:{exception.Message}");
        }
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

    private static string? Value(IEnumerable<RawSpecification> specs, string key) =>
        SourceTrustPolicy.BestSpecification(specs, key)?.RawValue;
}
