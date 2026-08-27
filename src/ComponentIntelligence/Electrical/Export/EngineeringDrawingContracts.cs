using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Export;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComponentDrawingRole
{
    Unknown,
    PowerSource,
    ConsumerOrConverter,
    TransparentTerminal,
    SensorOrControlDevice,
    ValveOrPump,
    InterfaceModule,
    CableOrConnector
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DrawingEvidenceStatus
{
    Unknown,
    Inferred,
    Confirmed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DrawingPageArchetype
{
    Unknown,
    PowerDistribution,
    ControlCircuit,
    DeviceLoop,
    Interface,
    TerminalContinuation,
    CableDetail
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PowerFlowOrientation
{
    Unknown,
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop
}

/// <summary>
/// Engineer-supplied drawing evidence. This export-only object is deliberately not persisted in
/// ElectricalProject snapshots and never derives roles, page allocation, or wiring from TypeKey.
/// </summary>
public sealed record AutocadEngineeringDrawingEvidence
{
    public IReadOnlyList<AutocadComponentDrawingRoleEvidence> ComponentRoles { get; init; } = [];
    public AutocadPageArchetypeHint? PageArchetypeHint { get; init; }
    public IReadOnlyList<AutocadPowerFlowOrientationEvidence> PowerFlowOrientations { get; init; } = [];
    public IReadOnlyList<AutocadCrossPageContinuationEvidence> CrossPageContinuations { get; init; } = [];
    public IReadOnlyList<AutocadCableInstanceOverride> CableInstanceOverrides { get; init; } = [];
}

public sealed record AutocadComponentDrawingRoleEvidence
{
    public required string ComponentInstanceId { get; init; }
    public ComponentDrawingRole Role { get; init; } = ComponentDrawingRole.Unknown;
    public DrawingEvidenceStatus Status { get; init; } = DrawingEvidenceStatus.Confirmed;
    public string? EvidenceSource { get; init; }
}

public sealed record AutocadPageArchetypeHint
{
    public DrawingPageArchetype Archetype { get; init; } = DrawingPageArchetype.Unknown;
    public DrawingEvidenceStatus Status { get; init; } = DrawingEvidenceStatus.Unknown;
    public string? EvidenceSource { get; init; }
}

public sealed record AutocadPowerFlowOrientationEvidence
{
    public required string NetIdentity { get; init; }
    public PowerFlowOrientation Orientation { get; init; } = PowerFlowOrientation.Unknown;
    public required string SourceEndpointId { get; init; }
    public required string DestinationEndpointId { get; init; }
    public DrawingEvidenceStatus Status { get; init; } = DrawingEvidenceStatus.Confirmed;
    public string? EvidenceSource { get; init; }
}

public sealed record AutocadCrossPageContinuationEvidence
{
    public required string PairIdentity { get; init; }
    public required string SourceEndpointId { get; init; }
    public required string DestinationEndpointId { get; init; }
    public required string SourcePageId { get; init; }
    public required string DestinationPageId { get; init; }
    public DrawingEvidenceStatus Status { get; init; } = DrawingEvidenceStatus.Confirmed;
    public string? EvidenceSource { get; init; }
}

public sealed record AutocadCableInstanceOverride
{
    public required string CableInstanceId { get; init; }
    public string? SpecificationOverride { get; init; }
    public string? CatalogOverride { get; init; }
}

public sealed record CableFamilySignature
{
    [JsonPropertyName("cableFamilyId")] public required string CableFamilyId { get; init; }
    [JsonPropertyName("endA")] public required CableInterfaceSignature EndA { get; init; }
    [JsonPropertyName("endB")] public required CableInterfaceSignature EndB { get; init; }
    [JsonPropertyName("pinCoreMap")] public required IReadOnlyList<CablePinCoreMapEntry> PinCoreMap { get; init; }

    public static CableFamilySignature Create(ElectricalProject project, CableInstance cable)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cable);

        var rawMap = cable.CoreAssignments.Select(assignment =>
        {
            var status = NormalizeStatus(assignment.Status);
            var unused = IsUnusedStatus(status);
            var fromEndpoint = ResolveEndpoint(project, assignment.FromEndpointId);
            var toEndpoint = ResolveEndpoint(project, assignment.ToEndpointId);
            return new RawMapEntry(
                Normalize(assignment.CoreId), status,
                fromEndpoint is null ? null : fromEndpoint with { IsUnused = unused },
                toEndpoint is null ? null : toEndpoint with { IsUnused = unused });
        }).ToArray();
        var endA = BuildInterface(rawMap.Select(item => item.From));
        var endB = BuildInterface(rawMap.Select(item => item.To));
        var swap = CompareInterface(endA, endB) > 0;
        if (CompareInterface(endA, endB) == 0)
        {
            var forward = MapKey(rawMap, swap: false);
            var reverse = MapKey(rawMap, swap: true);
            swap = string.CompareOrdinal(reverse, forward) < 0;
        }

        if (swap) (endA, endB) = (endB, endA);
        var map = rawMap.Select(item => new CablePinCoreMapEntry
            {
                CoreId = item.CoreId,
                Status = item.Status,
                EndAPin = PinNumber(swap ? item.To : item.From),
                EndBPin = PinNumber(swap ? item.From : item.To)
            })
            .OrderBy(item => item.CoreId, StringComparer.Ordinal)
            .ThenBy(item => item.EndAPin, StringComparer.Ordinal)
            .ThenBy(item => item.EndBPin, StringComparer.Ordinal)
            .ThenBy(item => item.Status, StringComparer.Ordinal)
            .ToArray();
        var canonical = string.Join("\u001e", InterfaceKey(endA), InterfaceKey(endB),
            string.Join("\u001d", map.Select(item => string.Join("\u001f",
                item.CoreId, item.Status, item.EndAPin ?? string.Empty, item.EndBPin ?? string.Empty))));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24];
        return new CableFamilySignature
        {
            CableFamilyId = $"cable-family:{hash}",
            EndA = endA,
            EndB = endB,
            PinCoreMap = map
        };
    }

    private static CableInterfaceSignature BuildInterface(IEnumerable<EndpointInfo?> endpointValues)
    {
        var endpoints = endpointValues.Where(item => item is not null).Select(item => item!).ToArray();
        var representative = endpoints.FirstOrDefault();
        var connector = representative?.Port.Connector;
        var used = endpoints.Where(item => !item.IsUnused)
            .Select(item => item.PinNumber).Where(item => item is not null).Select(item => item!)
            .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var allPins = representative?.Port.Pins.Select(pin => Normalize(pin.PinNumber))
            .Where(pin => pin.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(pin => pin, StringComparer.Ordinal).ToArray() ?? [];
        var unused = allPins.Except(used, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return new CableInterfaceSignature
        {
            Family = Normalize(connector?.Family),
            Series = NormalizeNullable(connector?.SeriesOrSize),
            PinCount = connector?.PinCount ?? (allPins.Length == 0 ? null : allPins.Length),
            Coding = NormalizeNullable(connector?.Coding),
            Gender = connector?.Gender ?? ConnectorGender.Unknown,
            UsedPins = used,
            UnusedPins = unused
        };
    }

    private static EndpointInfo? ResolveEndpoint(ElectricalProject project, string? endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return null;
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            var pin = port.Pins.FirstOrDefault(item => string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null) return new EndpointInfo(port, Normalize(pin.PinNumber), false);
        }
        return null;
    }

    private static int CompareInterface(CableInterfaceSignature left, CableInterfaceSignature right) =>
        string.CompareOrdinal(InterfaceKey(left), InterfaceKey(right));

    private static string InterfaceKey(CableInterfaceSignature value) => string.Join("\u001f",
        value.Family, value.Series ?? string.Empty, value.PinCount?.ToString() ?? string.Empty,
        value.Coding ?? string.Empty, value.Gender.ToString(), string.Join(",", value.UsedPins),
        string.Join(",", value.UnusedPins));

    private static string MapKey(IEnumerable<RawMapEntry> map, bool swap) => string.Join("\u001d", map
        .Select(item => string.Join("\u001f", item.CoreId, item.Status,
            PinNumber(swap ? item.To : item.From) ?? string.Empty,
            PinNumber(swap ? item.From : item.To) ?? string.Empty))
        .OrderBy(item => item, StringComparer.Ordinal));

    private static string? PinNumber(EndpointInfo? endpoint) => endpoint?.PinNumber;
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? "UNKNOWN"
        : string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
    private static string NormalizeStatus(string? value) => Normalize(value);
    private static bool IsUnusedStatus(string status) => status is "UNUSED" or "NC" or "SPARE" or "RESERVED" or "NOT_CONNECTED";

    private sealed record EndpointInfo(ComponentPort Port, string PinNumber, bool IsUnused);
    private sealed record RawMapEntry(string CoreId, string Status, EndpointInfo? From, EndpointInfo? To);
}

public sealed record CableInterfaceSignature
{
    [JsonPropertyName("family")] public required string Family { get; init; }
    [JsonPropertyName("series"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Series { get; init; }
    [JsonPropertyName("pinCount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? PinCount { get; init; }
    [JsonPropertyName("coding"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Coding { get; init; }
    [JsonPropertyName("gender"), JsonConverter(typeof(JsonStringEnumConverter))] public ConnectorGender Gender { get; init; }
    [JsonPropertyName("usedPins")] public required IReadOnlyList<string> UsedPins { get; init; }
    [JsonPropertyName("unusedPins")] public required IReadOnlyList<string> UnusedPins { get; init; }
}

public sealed record CablePinCoreMapEntry
{
    [JsonPropertyName("coreId")] public required string CoreId { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("endAPin"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EndAPin { get; init; }
    [JsonPropertyName("endBPin"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EndBPin { get; init; }
}

public sealed record AutocadStagingCableInstance
{
    [JsonPropertyName("cableInstanceId")] public required string CableInstanceId { get; init; }
    [JsonPropertyName("cableFamilyId")] public required string CableFamilyId { get; init; }
    [JsonPropertyName("cableDefinitionId")] public required string CableDefinitionId { get; init; }
    [JsonPropertyName("providedLengthMm"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ProvidedLengthMm { get; init; }
    [JsonPropertyName("lengthSource"), JsonConverter(typeof(JsonStringEnumConverter))] public CableLengthSource LengthSource { get; init; }
    [JsonPropertyName("specificationOverride"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SpecificationOverride { get; init; }
    [JsonPropertyName("catalogOverride"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CatalogOverride { get; init; }
}

public sealed record AutocadStagingPageArchetypeHint
{
    [JsonPropertyName("archetype")] public DrawingPageArchetype Archetype { get; init; } = DrawingPageArchetype.Unknown;
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; } = DrawingEvidenceStatus.Unknown;
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
}

public sealed record AutocadStagingPowerFlowOrientation
{
    [JsonPropertyName("orientation")] public PowerFlowOrientation Orientation { get; init; } = PowerFlowOrientation.Unknown;
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; } = DrawingEvidenceStatus.Unknown;
    [JsonPropertyName("sourceEndpointId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceEndpointId { get; init; }
    [JsonPropertyName("destinationEndpointId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DestinationEndpointId { get; init; }
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
    [JsonPropertyName("interventionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InterventionId { get; init; }
}

public sealed record AutocadStagingTerminalContinuity
{
    [JsonPropertyName("continuityId")] public required string ContinuityId { get; init; }
    [JsonPropertyName("terminalBlockId")] public required string TerminalBlockId { get; init; }
    [JsonPropertyName("terminalPositionId")] public required string TerminalPositionId { get; init; }
    [JsonPropertyName("levelId")] public required string LevelId { get; init; }
    [JsonPropertyName("fromConnectionPointId")] public required string FromConnectionPointId { get; init; }
    [JsonPropertyName("toConnectionPointId")] public required string ToConnectionPointId { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; } = DrawingEvidenceStatus.Confirmed;
}

public sealed record AutocadStagingCrossPageContinuation
{
    [JsonPropertyName("pairIdentity")] public required string PairIdentity { get; init; }
    [JsonPropertyName("sourceEndpointId")] public required string SourceEndpointId { get; init; }
    [JsonPropertyName("destinationEndpointId")] public required string DestinationEndpointId { get; init; }
    [JsonPropertyName("sourcePageId")] public required string SourcePageId { get; init; }
    [JsonPropertyName("destinationPageId")] public required string DestinationPageId { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; }
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
}
