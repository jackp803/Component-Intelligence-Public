using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Electrical.PowerTopology;

/// <summary>
/// Status for the production adapter from the accepted Engineering Graph v2 power-evidence
/// transport into the E2 deterministic analysis kernel. Blocked means the transport itself is
/// unsupported, structurally conflicting, or explicitly carries upstream blocking requirements.
/// </summary>
public enum PowerTopologyAdapterStatus
{
    Accepted,
    Blocked
}

public sealed record PowerTopologyAdapterDiagnostic
{
    public required string Code { get; init; }
    public required string SubjectId { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
    public required string Message { get; init; }
}

/// <summary>
/// A blocked adapter result never exposes an analyzer result as authoritative. An accepted adapter
/// result carries the canonical explicit input supplied by electrical-power-evidence.v1 and the E2
/// analyzer result, which may itself be Blocked for topology reasons such as cycles or coverage.
/// </summary>
public sealed record PowerTopologyAdapterResult
{
    public required PowerTopologyAdapterStatus Status { get; init; }
    public PowerTopologyInput? Input { get; init; }
    public PowerTopologyResult? Analysis { get; init; }
    public required IReadOnlyList<PowerTopologyAdapterDiagnostic> Diagnostics { get; init; }
}

/// <summary>
/// Production adapter for accepted explicit power evidence only. It deliberately ignores drawing,
/// page, route, voltage, name, TypeKey, geometry, and endpoint-order signals and creates no terminal
/// continuity or inferred power semantics.
/// </summary>
public sealed class ElectricalPowerEvidencePowerTopologyAdapter
{
    private static readonly StringComparer IdComparer = StringComparer.Ordinal;
    private readonly PowerTopologyAnalyzer _analyzer;

    public ElectricalPowerEvidencePowerTopologyAdapter(PowerTopologyAnalyzer? analyzer = null)
    {
        _analyzer = analyzer ?? new PowerTopologyAnalyzer();
    }

    public PowerTopologyAdapterResult AdaptAndAnalyze(AutocadStagingGraphV2Contract graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        try
        {
            AutocadStagingGraphV2Contract.EnsureSupportedSchema(graph.SchemaVersion);
        }
        catch (NotSupportedException exception)
        {
            return Blocked(
                Diagnostic(
                    "PWR-ADAPTER-OUTER-SCHEMA-UNSUPPORTED",
                    "ENGINEERING_GRAPH",
                    Array.Empty<string>(),
                    exception.Message));
        }

        var evidence = graph.PowerEvidence;
        if (evidence is null)
        {
            return Blocked(
                Diagnostic(
                    "PWR-ADAPTER-POWER-EVIDENCE-REQUIRED",
                    "POWER_EVIDENCE",
                    ["powerEvidence"],
                    "Engineering Graph powerEvidence is required for Power Topology analysis."));
        }

        try
        {
            evidence.EnsureUniqueStableIdentities();
        }
        catch (NotSupportedException exception)
        {
            return Blocked(
                Diagnostic(
                    "PWR-ADAPTER-POWER-SCHEMA-UNSUPPORTED",
                    "POWER_EVIDENCE",
                    ["schemaVersion"],
                    exception.Message));
        }
        catch (InvalidDataException exception)
        {
            return Blocked(
                Diagnostic(
                    "PWR-ADAPTER-DUPLICATE-IDENTITY",
                    "POWER_EVIDENCE",
                    Array.Empty<string>(),
                    exception.Message));
        }

        if (evidence.BlockingRequirements.Count > 0)
        {
            var blockerDiagnostics = evidence.BlockingRequirements
                .Select(blocker => Diagnostic(
                    blocker.Code,
                    blocker.SubjectId ?? "POWER_EVIDENCE",
                    blocker.MissingFields.OrderBy(field => field, IdComparer).ToArray(),
                    BlockerMessage(blocker)))
                .OrderBy(item => item.Code, IdComparer)
                .ThenBy(item => item.SubjectId, IdComparer)
                .ThenBy(item => string.Join("\u001f", item.MissingFields), IdComparer)
                .ToArray();
            return Blocked(blockerDiagnostics);
        }

        var defensiveDiagnostics = ValidateAcceptedEvidence(evidence);
        if (defensiveDiagnostics.Count > 0)
            return Blocked(defensiveDiagnostics);

        var input = Canonicalize(new PowerTopologyInput
        {
            Domains = evidence.Domains
                .Select(domain => new PowerDomainFact { DomainId = domain.PowerDomainId })
                .ToArray(),
            Producers = evidence.Participants
                .Where(participant => IsConfirmed(participant) &&
                                      string.Equals(participant.Role, "Producer", StringComparison.Ordinal))
                .Select(participant => new PowerProducerFact
                {
                    ProducerId = participant.EndpointId,
                    DomainId = participant.PowerDomainId!
                })
                .ToArray(),
            Consumers = evidence.Participants
                .Where(participant => IsConfirmed(participant) &&
                                      string.Equals(participant.Role, "Consumer", StringComparison.Ordinal))
                .Select(participant => new PowerConsumerFact
                {
                    ConsumerId = participant.EndpointId,
                    DomainId = participant.PowerDomainId!
                })
                .ToArray(),
            Conversions = evidence.Conversions
                .Where(conversion => IsConfirmedComplete(conversion))
                .Select(conversion => new PowerConversionFact
                {
                    ConversionId = conversion.ConversionId!,
                    InputDomainId = conversion.InputPowerDomainId!,
                    OutputDomainId = conversion.OutputPowerDomainId!
                })
                .ToArray()
        });

        var analysis = _analyzer.Analyze(input);
        return new PowerTopologyAdapterResult
        {
            Status = PowerTopologyAdapterStatus.Accepted,
            Input = input,
            Analysis = analysis,
            Diagnostics = Array.Empty<PowerTopologyAdapterDiagnostic>()
        };
    }

    private static IReadOnlyList<PowerTopologyAdapterDiagnostic> ValidateAcceptedEvidence(
        ElectricalPowerEvidenceV1Contract evidence)
    {
        var diagnostics = new List<PowerTopologyAdapterDiagnostic>();

        foreach (var domain in evidence.Domains.OrderBy(item => item.PowerDomainId, IdComparer))
        {
            if (!IsStableIdentity(domain.PowerDomainId))
            {
                diagnostics.Add(Diagnostic(
                    "PWR-ADAPTER-DOMAIN-IDENTITY-INVALID",
                    RenderIdentity(domain.PowerDomainId),
                    ["powerDomainId"],
                    "Power domain identity must be an explicit stable identity."));
            }
            if (!string.Equals(domain.EvidenceStatus, "Confirmed", StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "PWR-ADAPTER-DOMAIN-UNCONFIRMED",
                    domain.PowerDomainId,
                    ["evidenceStatus"],
                    "Power domain evidence must be Confirmed before production Power Topology analysis."));
            }
        }

        foreach (var participant in evidence.Participants.OrderBy(item => item.EndpointId, IdComparer))
        {
            if (!IsStableIdentity(participant.EndpointId))
            {
                diagnostics.Add(Diagnostic(
                    "PWR-ADAPTER-PARTICIPANT-IDENTITY-INVALID",
                    RenderIdentity(participant.EndpointId),
                    ["endpointId"],
                    "Power participant endpoint identity must be an explicit stable identity."));
                continue;
            }

            var roleBearing = string.Equals(participant.Role, "Producer", StringComparison.Ordinal) ||
                              string.Equals(participant.Role, "Consumer", StringComparison.Ordinal);
            if (!roleBearing) continue;

            if (!string.Equals(participant.EvidenceStatus, "Confirmed", StringComparison.Ordinal) ||
                !IsStableIdentity(participant.PowerDomainId))
            {
                diagnostics.Add(Diagnostic(
                    "PWR-ADAPTER-PARTICIPANT-INCOMPLETE",
                    participant.EndpointId,
                    ["powerDomainId", "evidenceStatus"],
                    "Producer/Consumer participant must be Confirmed with an explicit stable powerDomainId."));
            }
        }

        foreach (var conversion in evidence.Conversions
                     .OrderBy(item => item.ConversionId ?? string.Empty, IdComparer)
                     .ThenBy(item => item.ComponentInstanceId, IdComparer))
        {
            if (IsConfirmedComplete(conversion)) continue;
            diagnostics.Add(Diagnostic(
                "PWR-ADAPTER-CONVERSION-INCOMPLETE",
                conversion.ConversionId ?? conversion.ComponentInstanceId,
                ConversionMissingFields(conversion),
                "Only Confirmed conversions with explicit stable conversion/input/output domain identities are admissible."));
        }

        return diagnostics
            .OrderBy(item => item.Code, IdComparer)
            .ThenBy(item => item.SubjectId, IdComparer)
            .ThenBy(item => string.Join("\u001f", item.MissingFields), IdComparer)
            .ThenBy(item => item.Message, IdComparer)
            .ToArray();
    }

    private static IReadOnlyList<string> ConversionMissingFields(ElectricalPowerEvidenceConversion conversion)
    {
        var missing = new List<string>();
        if (!IsStableIdentity(conversion.ConversionId)) missing.Add("conversionId");
        if (!IsStableIdentity(conversion.InputPowerDomainId)) missing.Add("inputPowerDomainId");
        if (!IsStableIdentity(conversion.OutputPowerDomainId)) missing.Add("outputPowerDomainId");
        if (!string.Equals(conversion.EvidenceStatus, "Confirmed", StringComparison.Ordinal)) missing.Add("evidenceStatus");
        return missing.OrderBy(item => item, IdComparer).ToArray();
    }

    private static bool IsConfirmed(ElectricalPowerEvidenceParticipant participant) =>
        string.Equals(participant.EvidenceStatus, "Confirmed", StringComparison.Ordinal) &&
        IsStableIdentity(participant.PowerDomainId);

    private static bool IsConfirmedComplete(ElectricalPowerEvidenceConversion conversion) =>
        string.Equals(conversion.EvidenceStatus, "Confirmed", StringComparison.Ordinal) &&
        IsStableIdentity(conversion.ConversionId) &&
        IsStableIdentity(conversion.InputPowerDomainId) &&
        IsStableIdentity(conversion.OutputPowerDomainId);

    private static PowerTopologyInput Canonicalize(PowerTopologyInput input) => new()
    {
        Domains = input.Domains
            .OrderBy(item => item.DomainId, IdComparer)
            .ToArray(),
        Producers = input.Producers
            .OrderBy(item => item.ProducerId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .ToArray(),
        Consumers = input.Consumers
            .OrderBy(item => item.ConsumerId, IdComparer)
            .ThenBy(item => item.DomainId, IdComparer)
            .ToArray(),
        Conversions = input.Conversions
            .OrderBy(item => item.ConversionId, IdComparer)
            .ThenBy(item => item.InputDomainId, IdComparer)
            .ThenBy(item => item.OutputDomainId, IdComparer)
            .ToArray()
    };

    private static PowerTopologyAdapterResult Blocked(params PowerTopologyAdapterDiagnostic[] diagnostics) =>
        Blocked((IReadOnlyList<PowerTopologyAdapterDiagnostic>)diagnostics);

    private static PowerTopologyAdapterResult Blocked(IReadOnlyList<PowerTopologyAdapterDiagnostic> diagnostics) => new()
    {
        Status = PowerTopologyAdapterStatus.Blocked,
        Input = null,
        Analysis = null,
        Diagnostics = diagnostics
            .OrderBy(item => item.Code, IdComparer)
            .ThenBy(item => item.SubjectId, IdComparer)
            .ThenBy(item => string.Join("\u001f", item.MissingFields), IdComparer)
            .ThenBy(item => item.Message, IdComparer)
            .ToArray()
    };

    private static PowerTopologyAdapterDiagnostic Diagnostic(
        string code,
        string subjectId,
        IReadOnlyList<string> missingFields,
        string message) => new()
    {
        Code = code,
        SubjectId = subjectId,
        MissingFields = missingFields,
        Message = message
    };

    private static string BlockerMessage(ElectricalPowerEvidenceBlocker blocker)
    {
        var fields = blocker.MissingFields.OrderBy(item => item, IdComparer).ToArray();
        return fields.Length == 0
            ? $"Upstream electrical power evidence blocker '{blocker.Code}' is unresolved."
            : $"Upstream electrical power evidence blocker '{blocker.Code}' requires: {string.Join(", ", fields)}.";
    }

    private static bool IsStableIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static string RenderIdentity(string? value)
    {
        if (value is null) return "<NULL>";
        if (string.IsNullOrWhiteSpace(value)) return "<EMPTY>";
        return new string(value.Select(character => char.IsControl(character) ? '?' : character).ToArray());
    }
}
