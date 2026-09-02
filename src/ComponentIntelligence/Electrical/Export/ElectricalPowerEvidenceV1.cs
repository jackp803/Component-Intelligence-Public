using System.Globalization;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Export;

/// <summary>
/// Evidence-only transport boundary for explicit power-domain membership and conversion declarations.
/// This contract carries facts to downstream power analysis but deliberately defines no DAG, source
/// selection, reachability, terminal pass-through, voltage compatibility, or conversion ordering.
/// </summary>
public sealed record ElectricalPowerEvidenceV1Contract
{
    public const string SupportedSchemaVersion = "electrical-power-evidence.v1";

    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = SupportedSchemaVersion;
    [JsonPropertyName("domains")] public IReadOnlyList<ElectricalPowerEvidenceDomain> Domains { get; init; } = [];
    [JsonPropertyName("participants")] public IReadOnlyList<ElectricalPowerEvidenceParticipant> Participants { get; init; } = [];
    [JsonPropertyName("conversions")] public IReadOnlyList<ElectricalPowerEvidenceConversion> Conversions { get; init; } = [];
    [JsonPropertyName("blockingRequirements")] public IReadOnlyList<ElectricalPowerEvidenceBlocker> BlockingRequirements { get; init; } = [];

    public static void EnsureSupportedSchema(string? schemaVersion)
    {
        if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"Power evidence schema '{schemaVersion ?? "<missing>"}' is unsupported; expected '{SupportedSchemaVersion}'.");
    }

    public void EnsureUniqueStableIdentities()
    {
        EnsureSupportedSchema(SchemaVersion);
        EnsureUnique(Domains.Select(item => item.PowerDomainId), "power domain");
        EnsureUnique(Participants.Select(item => item.EndpointId), "power participant");
        EnsureUnique(Conversions.Select(item => item.ConversionId).Where(value => !string.IsNullOrWhiteSpace(value))!, "power conversion");
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Duplicate {kind} identity '{duplicate.Key}' is not permitted.");
    }
}

public sealed record ElectricalPowerEvidenceDomain
{
    [JsonPropertyName("powerDomainId")] public required string PowerDomainId { get; init; }
    [JsonPropertyName("evidenceStatus")] public string EvidenceStatus { get; init; } = "Confirmed";
    [JsonPropertyName("provenance")] public IReadOnlyList<ElectricalPowerEvidenceProvenanceRef> Provenance { get; init; } = [];
}

public sealed record ElectricalPowerEvidenceParticipant
{
    [JsonPropertyName("endpointId")] public required string EndpointId { get; init; }
    [JsonPropertyName("componentInstanceId")] public required string ComponentInstanceId { get; init; }
    [JsonPropertyName("sourcePortId")] public string? SourcePortId { get; init; }
    [JsonPropertyName("sourcePinId")] public string? SourcePinId { get; init; }
    [JsonPropertyName("role")] public string Role { get; init; } = "Unknown";
    [JsonPropertyName("powerDomainId")] public string? PowerDomainId { get; init; }
    [JsonPropertyName("evidenceStatus")] public string EvidenceStatus { get; init; } = "Unknown";
    [JsonPropertyName("blockingReason")] public string? BlockingReason { get; init; }
    [JsonPropertyName("provenance")] public IReadOnlyList<ElectricalPowerEvidenceProvenanceRef> Provenance { get; init; } = [];
}

public sealed record ElectricalPowerEvidenceConversion
{
    [JsonPropertyName("conversionId")] public string? ConversionId { get; init; }
    [JsonPropertyName("componentInstanceId")] public required string ComponentInstanceId { get; init; }
    [JsonPropertyName("inputPowerDomainId")] public string? InputPowerDomainId { get; init; }
    [JsonPropertyName("outputPowerDomainId")] public string? OutputPowerDomainId { get; init; }
    [JsonPropertyName("inputSourcePortIds")] public IReadOnlyList<string> InputSourcePortIds { get; init; } = [];
    [JsonPropertyName("inputSourcePinIds")] public IReadOnlyList<string> InputSourcePinIds { get; init; } = [];
    [JsonPropertyName("outputSourcePortIds")] public IReadOnlyList<string> OutputSourcePortIds { get; init; } = [];
    [JsonPropertyName("outputSourcePinIds")] public IReadOnlyList<string> OutputSourcePinIds { get; init; } = [];
    [JsonPropertyName("inputEndpointIds")] public IReadOnlyList<string> InputEndpointIds { get; init; } = [];
    [JsonPropertyName("outputEndpointIds")] public IReadOnlyList<string> OutputEndpointIds { get; init; } = [];
    [JsonPropertyName("evidenceStatus")] public string EvidenceStatus { get; init; } = "Unknown";
    [JsonPropertyName("blockingReason")] public string? BlockingReason { get; init; }
    [JsonPropertyName("provenance")] public IReadOnlyList<ElectricalPowerEvidenceSourceEvidence> Provenance { get; init; } = [];
}

public sealed record ElectricalPowerEvidenceSourceEvidence
{
    [JsonPropertyName("sourceType")] public required string SourceType { get; init; }
    [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; init; }
    [JsonPropertyName("documentUrl")] public string? DocumentUrl { get; init; }
    [JsonPropertyName("documentHashSha256")] public string? DocumentHashSha256 { get; init; }
    [JsonPropertyName("pageNumber")] public int? PageNumber { get; init; }
    [JsonPropertyName("extractionMethod")] public required string ExtractionMethod { get; init; }
    [JsonPropertyName("rawValue")] public string? RawValue { get; init; }
    [JsonPropertyName("retrievedAt")] public DateTimeOffset RetrievedAt { get; init; }
    [JsonPropertyName("verificationStatus")] public required string VerificationStatus { get; init; }
}

public sealed record ElectricalPowerEvidenceProvenanceRef
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("sourceId")] public required string SourceId { get; init; }
}

public sealed record ElectricalPowerEvidenceBlocker
{
    [JsonPropertyName("code")] public required string Code { get; init; }
    [JsonPropertyName("subjectId")] public string? SubjectId { get; init; }
    [JsonPropertyName("missingFields")] public IReadOnlyList<string> MissingFields { get; init; } = [];
}

public static class ElectricalPowerEvidenceV1Builder
{
    private sealed record ResolvedConversionEndpoint(
        string SourceKind,
        string SourceId,
        string RuntimeEndpointId)
    {
        public string SourceReference => $"{SourceKind}:{SourceId}";
    }

    private sealed record ConversionSideResolution(
        IReadOnlyList<ResolvedConversionEndpoint> Mappings,
        bool Blocked);

    public static ElectricalPowerEvidenceV1Contract Build(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var blockers = new List<ElectricalPowerEvidenceBlocker>();
        var participantCandidates = new List<ElectricalPowerEvidenceParticipant>();
        var domainProvenance = new Dictionary<string, List<ElectricalPowerEvidenceProvenanceRef>>(StringComparer.Ordinal);

        foreach (var component in project.Components.OrderBy(item => item.ComponentInstanceId, StringComparer.Ordinal))
        {
            foreach (var port in component.Ports.OrderBy(item => item.PortId, StringComparer.Ordinal))
            {
                var explicitPortDomain = ExplicitId(port.PowerDomainId);
                if (explicitPortDomain is not null)
                {
                    var provenance = ParticipantProvenance(component.ComponentDefinitionId, port.SourcePortId, null);
                    participantCandidates.Add(new ElectricalPowerEvidenceParticipant
                    {
                        EndpointId = port.PortId,
                        ComponentInstanceId = component.ComponentInstanceId,
                        SourcePortId = ExplicitId(port.SourcePortId),
                        Role = "Unknown",
                        PowerDomainId = explicitPortDomain,
                        EvidenceStatus = "Unknown",
                        Provenance = provenance
                    });
                    AddDomainProvenance(domainProvenance, explicitPortDomain, provenance);
                }

                foreach (var pin in port.Pins.OrderBy(item => item.PinId, StringComparer.Ordinal))
                {
                    var explicitPinDomain = ExplicitId(pin.PowerDomainId);
                    if (pin.Power is null && explicitPinDomain is null) continue;

                    var role = ParticipantRole(pin.Power?.Role ?? PowerRole.Unknown);
                    var roleBearing = role is "Producer" or "Consumer" or "Return";
                    var provenance = ParticipantProvenance(component.ComponentDefinitionId, port.SourcePortId, pin.SourcePinId);
                    var blockingReason = roleBearing && explicitPinDomain is null ? "POWER_DOMAIN_ID_REQUIRED" : null;
                    participantCandidates.Add(new ElectricalPowerEvidenceParticipant
                    {
                        EndpointId = pin.PinId,
                        ComponentInstanceId = component.ComponentInstanceId,
                        SourcePortId = ExplicitId(port.SourcePortId),
                        SourcePinId = ExplicitId(pin.SourcePinId),
                        Role = role,
                        PowerDomainId = explicitPinDomain,
                        EvidenceStatus = role != "Unknown" && explicitPinDomain is not null ? "Confirmed" : "Unknown",
                        BlockingReason = blockingReason,
                        Provenance = provenance
                    });
                    if (explicitPinDomain is not null)
                        AddDomainProvenance(domainProvenance, explicitPinDomain, provenance);
                    if (blockingReason is not null)
                        blockers.Add(Blocker(blockingReason, pin.PinId, "powerDomainId"));
                }
            }
        }

        var participants = ResolveParticipantIdentities(participantCandidates, blockers);
        var conversionCandidates = BuildConversions(project, blockers, domainProvenance);
        var conversions = ResolveConversionIdentities(conversionCandidates, blockers);

        if (project.Components.Count > 0 && project.Components.All(component => component.PowerConversions.Count == 0))
            blockers.Add(Blocker("POWER_CONVERSION_EVIDENCE_SOURCE_UNAVAILABLE", null, "powerConversions"));

        var domains = domainProvenance.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ElectricalPowerEvidenceDomain
            {
                PowerDomainId = pair.Key,
                EvidenceStatus = "Confirmed",
                Provenance = NormalizeProvenanceRefs(pair.Value)
            }).ToArray();

        var contract = new ElectricalPowerEvidenceV1Contract
        {
            SchemaVersion = ElectricalPowerEvidenceV1Contract.SupportedSchemaVersion,
            Domains = domains,
            Participants = participants,
            Conversions = conversions,
            BlockingRequirements = NormalizeBlockers(blockers)
        };
        contract.EnsureUniqueStableIdentities();
        return contract;
    }

    private static IReadOnlyList<ElectricalPowerEvidenceParticipant> ResolveParticipantIdentities(
        IEnumerable<ElectricalPowerEvidenceParticipant> candidates,
        ICollection<ElectricalPowerEvidenceBlocker> blockers)
    {
        var result = new List<ElectricalPowerEvidenceParticipant>();
        foreach (var group in candidates.GroupBy(item => item.EndpointId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var normalized = group.OrderBy(ParticipantCanonicalKey, StringComparer.Ordinal).ToArray();
            if (normalized.Select(ParticipantCanonicalKey).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                result.Add(normalized[0]);
                continue;
            }

            blockers.Add(Blocker("DUPLICATE_POWER_PARTICIPANT_IDENTITY_CONFLICT", group.Key, "endpointId"));
        }
        return result;
    }

    private static IReadOnlyList<ElectricalPowerEvidenceConversion> BuildConversions(
        ElectricalProject project,
        ICollection<ElectricalPowerEvidenceBlocker> blockers,
        IDictionary<string, List<ElectricalPowerEvidenceProvenanceRef>> domainProvenance)
    {
        var result = new List<ElectricalPowerEvidenceConversion>();
        foreach (var component in project.Components.OrderBy(item => item.ComponentInstanceId, StringComparer.Ordinal))
        foreach (var source in component.PowerConversions.OrderBy(ConversionDomainCanonicalKey, StringComparer.Ordinal))
        {
            var conversionId = ExplicitId(source.ConversionId);
            var inputDomain = ExplicitId(source.InputPowerDomainId);
            var outputDomain = ExplicitId(source.OutputPowerDomainId);
            var missing = new List<string>();
            if (conversionId is null) missing.Add("conversionId");
            if (inputDomain is null) missing.Add("inputPowerDomainId");
            if (outputDomain is null) missing.Add("outputPowerDomainId");

            var inputPorts = NormalizeIds(source.InputSourcePortIds);
            var inputPins = NormalizeIds(source.InputSourcePinIds);
            var outputPorts = NormalizeIds(source.OutputSourcePortIds);
            var outputPins = NormalizeIds(source.OutputSourcePinIds);
            var provenance = NormalizeSourceEvidence(source.Evidence);
            var subjectId = conversionId ?? component.ComponentInstanceId;
            if (missing.Count > 0)
                blockers.Add(new ElectricalPowerEvidenceBlocker
                {
                    Code = "POWER_CONVERSION_FIELDS_REQUIRED",
                    SubjectId = subjectId,
                    MissingFields = missing.OrderBy(item => item, StringComparer.Ordinal).ToArray()
                });

            var inputResolution = ResolveConversionSideEndpoints(
                project, component, subjectId, "INPUT", inputPorts, inputPins, blockers);
            var outputResolution = ResolveConversionSideEndpoints(
                project, component, subjectId, "OUTPUT", outputPorts, outputPins, blockers);
            var (inputEndpointIds, outputEndpointIds) = ApplyRuntimeEndpointInjectivity(
                subjectId, inputResolution, outputResolution, blockers);

            var conversion = new ElectricalPowerEvidenceConversion
            {
                ConversionId = conversionId,
                ComponentInstanceId = component.ComponentInstanceId,
                InputPowerDomainId = inputDomain,
                OutputPowerDomainId = outputDomain,
                InputSourcePortIds = inputPorts,
                InputSourcePinIds = inputPins,
                OutputSourcePortIds = outputPorts,
                OutputSourcePinIds = outputPins,
                InputEndpointIds = inputEndpointIds,
                OutputEndpointIds = outputEndpointIds,
                EvidenceStatus = missing.Count == 0 ? "Confirmed" : "Unknown",
                BlockingReason = missing.Count == 0 ? null : "POWER_CONVERSION_FIELDS_REQUIRED",
                Provenance = provenance
            };
            result.Add(conversion);

            var conversionRef = conversionId is null
                ? Array.Empty<ElectricalPowerEvidenceProvenanceRef>()
                : [new ElectricalPowerEvidenceProvenanceRef { Kind = "Conversion", SourceId = conversionId }];
            if (inputDomain is not null)
                AddDomainProvenance(domainProvenance, inputDomain, conversionRef);
            if (outputDomain is not null)
                AddDomainProvenance(domainProvenance, outputDomain, conversionRef);
        }
        return result.OrderBy(ConversionCanonicalKey, StringComparer.Ordinal).ToArray();
    }

    private static ConversionSideResolution ResolveConversionSideEndpoints(
        ElectricalProject project,
        ComponentInstance component,
        string subjectId,
        string side,
        IReadOnlyList<string> sourcePortIds,
        IReadOnlyList<string> sourcePinIds,
        ICollection<ElectricalPowerEvidenceBlocker> blockers)
    {
        if (sourcePortIds.Count == 0 && sourcePinIds.Count == 0)
        {
            blockers.Add(Blocker(
                $"POWER_CONVERSION_{side}_SOURCE_REFERENCE_REQUIRED",
                subjectId,
                side == "INPUT" ? "inputSourcePortIds|inputSourcePinIds" : "outputSourcePortIds|outputSourcePinIds"));
            return new ConversionSideResolution([], true);
        }

        var resolved = new List<ResolvedConversionEndpoint>();
        var blocked = false;
        foreach (var sourcePortId in sourcePortIds)
        {
            var matches = DistinctObjectsByReference(component.Ports
                .Where(port => string.Equals(ExplicitId(port.SourcePortId), sourcePortId, StringComparison.Ordinal)));
            if (matches.Length == 1)
            {
                resolved.Add(new ResolvedConversionEndpoint("Port", sourcePortId, matches[0].PortId));
                continue;
            }

            blocked = true;
            if (matches.Length > 1)
            {
                blockers.Add(Blocker($"POWER_CONVERSION_{side}_SOURCE_PORT_AMBIGUOUS", subjectId, sourcePortId));
                continue;
            }

            var existsElsewhere = project.Components
                .Where(other => !ReferenceEquals(other, component))
                .Any(other => other.Ports.Any(port =>
                    string.Equals(ExplicitId(port.SourcePortId), sourcePortId, StringComparison.Ordinal)));
            blockers.Add(Blocker(
                existsElsewhere
                    ? $"POWER_CONVERSION_{side}_SOURCE_PORT_CROSS_COMPONENT"
                    : $"POWER_CONVERSION_{side}_SOURCE_PORT_UNRESOLVED",
                subjectId,
                sourcePortId));
        }

        foreach (var sourcePinId in sourcePinIds)
        {
            var matches = DistinctObjectsByReference(component.Ports
                .SelectMany(port => port.Pins)
                .Where(pin => string.Equals(ExplicitId(pin.SourcePinId), sourcePinId, StringComparison.Ordinal)));
            if (matches.Length == 1)
            {
                resolved.Add(new ResolvedConversionEndpoint("Pin", sourcePinId, matches[0].PinId));
                continue;
            }

            blocked = true;
            if (matches.Length > 1)
            {
                blockers.Add(Blocker($"POWER_CONVERSION_{side}_SOURCE_PIN_AMBIGUOUS", subjectId, sourcePinId));
                continue;
            }

            var existsElsewhere = project.Components
                .Where(other => !ReferenceEquals(other, component))
                .Any(other => other.Ports.SelectMany(port => port.Pins).Any(pin =>
                    string.Equals(ExplicitId(pin.SourcePinId), sourcePinId, StringComparison.Ordinal)));
            blockers.Add(Blocker(
                existsElsewhere
                    ? $"POWER_CONVERSION_{side}_SOURCE_PIN_CROSS_COMPONENT"
                    : $"POWER_CONVERSION_{side}_SOURCE_PIN_UNRESOLVED",
                subjectId,
                sourcePinId));
        }

        return new ConversionSideResolution(
            resolved.OrderBy(item => item.SourceKind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.RuntimeEndpointId, StringComparer.Ordinal)
                .ToArray(),
            blocked);
    }

    private static (IReadOnlyList<string> InputEndpointIds, IReadOnlyList<string> OutputEndpointIds)
        ApplyRuntimeEndpointInjectivity(
            string subjectId,
            ConversionSideResolution input,
            ConversionSideResolution output,
            ICollection<ElectricalPowerEvidenceBlocker> blockers)
    {
        var inputCollision = false;
        var outputCollision = false;
        var allMappings = input.Mappings.Select(item => (Side: "INPUT", Mapping: item))
            .Concat(output.Mappings.Select(item => (Side: "OUTPUT", Mapping: item)))
            .OrderBy(item => item.Mapping.RuntimeEndpointId, StringComparer.Ordinal)
            .ThenBy(item => item.Side, StringComparer.Ordinal)
            .ThenBy(item => item.Mapping.SourceReference, StringComparer.Ordinal)
            .ToArray();

        foreach (var group in allMappings.GroupBy(item => item.Mapping.RuntimeEndpointId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var sourceRefs = group.Select(item => item.Mapping.SourceReference)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (sourceRefs.Length <= 1) continue;

            var missingFields = new[] { $"runtimeEndpointId:{group.Key}" }
                .Concat(sourceRefs)
                .ToArray();
            var affectedSides = group.Select(item => item.Side)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            foreach (var side in affectedSides)
            {
                blockers.Add(Blocker(
                    $"POWER_CONVERSION_{side}_RUNTIME_ENDPOINT_ID_COLLISION",
                    subjectId,
                    missingFields));
                if (side == "INPUT") inputCollision = true;
                if (side == "OUTPUT") outputCollision = true;
            }
        }

        return (
            input.Blocked || inputCollision
                ? []
                : NormalizeIds(input.Mappings.Select(item => item.RuntimeEndpointId)),
            output.Blocked || outputCollision
                ? []
                : NormalizeIds(output.Mappings.Select(item => item.RuntimeEndpointId)));
    }

    private static IReadOnlyList<ElectricalPowerEvidenceConversion> ResolveConversionIdentities(
        IEnumerable<ElectricalPowerEvidenceConversion> candidates,
        ICollection<ElectricalPowerEvidenceBlocker> blockers)
    {
        var result = new List<ElectricalPowerEvidenceConversion>();
        var withoutIdentity = candidates.Where(item => string.IsNullOrWhiteSpace(item.ConversionId))
            .GroupBy(ConversionCanonicalKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(ConversionCanonicalKey, StringComparer.Ordinal);
        result.AddRange(withoutIdentity);

        foreach (var group in candidates.Where(item => !string.IsNullOrWhiteSpace(item.ConversionId))
                     .GroupBy(item => item.ConversionId!, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var normalized = group.OrderBy(ConversionCanonicalKey, StringComparer.Ordinal).ToArray();
            if (normalized.Select(ConversionCanonicalKey).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                result.Add(normalized[0]);
                continue;
            }

            blockers.Add(Blocker("DUPLICATE_POWER_CONVERSION_IDENTITY_CONFLICT", group.Key, "conversionId"));
        }

        return result.OrderBy(item => item.ConversionId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(ConversionCanonicalKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ParticipantRole(PowerRole role) => role switch
    {
        PowerRole.Source => "Producer",
        PowerRole.Input => "Consumer",
        PowerRole.Return => "Return",
        _ => "Unknown"
    };

    private static IReadOnlyList<ElectricalPowerEvidenceProvenanceRef> ParticipantProvenance(
        string componentDefinitionId,
        string? sourcePortId,
        string? sourcePinId)
    {
        var refs = new List<ElectricalPowerEvidenceProvenanceRef>
        {
            new() { Kind = "ComponentDefinition", SourceId = componentDefinitionId }
        };
        var port = ExplicitId(sourcePortId);
        var pin = ExplicitId(sourcePinId);
        if (port is not null) refs.Add(new ElectricalPowerEvidenceProvenanceRef { Kind = "SourcePort", SourceId = port });
        if (pin is not null) refs.Add(new ElectricalPowerEvidenceProvenanceRef { Kind = "SourcePin", SourceId = pin });
        return NormalizeProvenanceRefs(refs);
    }

    private static void AddDomainProvenance(
        IDictionary<string, List<ElectricalPowerEvidenceProvenanceRef>> target,
        string powerDomainId,
        IEnumerable<ElectricalPowerEvidenceProvenanceRef> provenance)
    {
        if (!target.TryGetValue(powerDomainId, out var refs))
        {
            refs = new List<ElectricalPowerEvidenceProvenanceRef>();
            target[powerDomainId] = refs;
        }
        refs.AddRange(provenance);
    }

    private static IReadOnlyList<ElectricalPowerEvidenceProvenanceRef> NormalizeProvenanceRefs(
        IEnumerable<ElectricalPowerEvidenceProvenanceRef> values) => values
        .Where(item => !string.IsNullOrWhiteSpace(item.Kind) && !string.IsNullOrWhiteSpace(item.SourceId))
        .Select(item => new ElectricalPowerEvidenceProvenanceRef
        {
            Kind = item.Kind.Trim(),
            SourceId = item.SourceId.Trim()
        })
        .GroupBy(item => $"{item.Kind}\u001f{item.SourceId}", StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(item => item.Kind, StringComparer.Ordinal)
        .ThenBy(item => item.SourceId, StringComparer.Ordinal)
        .ToArray();

    private static T[] DistinctObjectsByReference<T>(IEnumerable<T> values) where T : class
    {
        var result = new List<T>();
        foreach (var value in values)
        {
            if (result.Any(existing => ReferenceEquals(existing, value))) continue;
            result.Add(value);
        }
        return result.ToArray();
    }

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<ElectricalPowerEvidenceSourceEvidence> NormalizeSourceEvidence(
        IEnumerable<PowerEvidenceProvenance> values) => values
        .Select(item => new ElectricalPowerEvidenceSourceEvidence
        {
            SourceType = item.SourceType,
            SourceUrl = item.SourceUrl,
            DocumentUrl = item.DocumentUrl,
            DocumentHashSha256 = item.DocumentHashSha256,
            PageNumber = item.PageNumber,
            ExtractionMethod = item.ExtractionMethod,
            RawValue = item.RawValue,
            RetrievedAt = item.RetrievedAt,
            VerificationStatus = item.VerificationStatus
        })
        .OrderBy(SourceEvidenceCanonicalKey, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<ElectricalPowerEvidenceBlocker> NormalizeBlockers(
        IEnumerable<ElectricalPowerEvidenceBlocker> blockers) => blockers
        .Select(item => item with
        {
            MissingFields = item.MissingFields.OrderBy(value => value, StringComparer.Ordinal).ToArray()
        })
        .GroupBy(BlockerCanonicalKey, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(item => item.Code, StringComparer.Ordinal)
        .ThenBy(item => item.SubjectId ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(item => string.Join("\u001f", item.MissingFields), StringComparer.Ordinal)
        .ToArray();

    private static ElectricalPowerEvidenceBlocker Blocker(string code, string? subjectId, params string[] missingFields) => new()
    {
        Code = code,
        SubjectId = subjectId,
        MissingFields = missingFields.OrderBy(item => item, StringComparer.Ordinal).ToArray()
    };

    private static string ParticipantCanonicalKey(ElectricalPowerEvidenceParticipant item) => string.Join("\u001f",
        item.EndpointId,
        item.ComponentInstanceId,
        item.SourcePortId ?? string.Empty,
        item.SourcePinId ?? string.Empty,
        item.Role,
        item.PowerDomainId ?? string.Empty,
        item.EvidenceStatus,
        item.BlockingReason ?? string.Empty,
        string.Join("\u001e", item.Provenance.Select(refItem => $"{refItem.Kind}:{refItem.SourceId}")));

    private static string ConversionDomainCanonicalKey(PowerConversionEvidence item) => string.Join("\u001f",
        item.ConversionId ?? string.Empty,
        item.InputPowerDomainId ?? string.Empty,
        item.OutputPowerDomainId ?? string.Empty,
        string.Join("\u001e", item.InputSourcePortIds.OrderBy(value => value, StringComparer.Ordinal)),
        string.Join("\u001e", item.InputSourcePinIds.OrderBy(value => value, StringComparer.Ordinal)),
        string.Join("\u001e", item.OutputSourcePortIds.OrderBy(value => value, StringComparer.Ordinal)),
        string.Join("\u001e", item.OutputSourcePinIds.OrderBy(value => value, StringComparer.Ordinal)));

    private static string ConversionCanonicalKey(ElectricalPowerEvidenceConversion item) => string.Join("\u001f",
        item.ConversionId ?? string.Empty,
        item.ComponentInstanceId,
        item.InputPowerDomainId ?? string.Empty,
        item.OutputPowerDomainId ?? string.Empty,
        string.Join("\u001e", item.InputSourcePortIds),
        string.Join("\u001e", item.InputSourcePinIds),
        string.Join("\u001e", item.OutputSourcePortIds),
        string.Join("\u001e", item.OutputSourcePinIds),
        string.Join("\u001e", item.InputEndpointIds),
        string.Join("\u001e", item.OutputEndpointIds),
        item.EvidenceStatus,
        item.BlockingReason ?? string.Empty,
        string.Join("\u001e", item.Provenance.Select(SourceEvidenceCanonicalKey)));

    private static string SourceEvidenceCanonicalKey(ElectricalPowerEvidenceSourceEvidence item) => string.Join("\u001f",
        item.SourceType,
        item.SourceUrl ?? string.Empty,
        item.DocumentUrl ?? string.Empty,
        item.DocumentHashSha256 ?? string.Empty,
        item.PageNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        item.ExtractionMethod,
        item.RawValue ?? string.Empty,
        item.RetrievedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        item.VerificationStatus);

    private static string BlockerCanonicalKey(ElectricalPowerEvidenceBlocker item) => string.Join("\u001f",
        item.Code,
        item.SubjectId ?? string.Empty,
        string.Join("\u001e", item.MissingFields.OrderBy(value => value, StringComparer.Ordinal)));

    private static string? ExplicitId(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
