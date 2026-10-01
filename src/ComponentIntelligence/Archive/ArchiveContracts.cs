using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComponentIntelligence.Archive;

public enum ArchiveOperation { CREATE, UPDATE, REVIEW }
public enum ArchiveValidationStatus { PASS, REJECT, REVIEW_REQUIRED, TOOL_FAILURE }
public enum ArchiveValidationSeverity { ERROR, REVIEW, WARNING, INFO }
public enum ArchiveJobStatus { COMPLETE, VALIDATED_NOT_WRITTEN, REVIEW_REQUIRED, REJECTED, PARTIAL, BLOCKED }
public enum ArchiveReadbackStatus { MATCH, MISMATCH, NOT_PERFORMED, FAILED }
public enum ArchiveTopologyStatus { Ready, Review, NeedsData }
public enum ArchiveLayoutStatus { Ready, Review, NeedsData }
public enum ArchiveTopologyEndpointMode { Connector, Pins }
public enum ArchivePinStatus { Used, Unused, NC, Reserved, Optional, Unknown, NotApplicable }

public sealed record ArchiveEvidenceReference
{
    public string? EvidenceId { get; init; }
    public string? SourceType { get; init; }
    public string? SourceUrl { get; init; }
    public string? DocumentPath { get; init; }
    public string? SourcePage { get; init; }
    public string? Notes { get; init; }
}

public sealed record ArchivePinChange
{
    public required string PinId { get; init; }
    public string? PriorStablePinId { get; init; }
    public required string PortId { get; init; }
    public required string PinNumber { get; init; }
    public string? PinName { get; init; }
    public string? PinRole { get; init; }
    public string? Direction { get; init; }
    public string? SignalType { get; init; }
    public string? Voltage { get; init; }
    public string? Function { get; init; }
    public ArchivePinStatus PinStatus { get; init; } = ArchivePinStatus.Unknown;
    public string? SourcePage { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<ArchiveEvidenceReference> Evidence { get; init; } = Array.Empty<ArchiveEvidenceReference>();
}

public sealed record ArchivePortChange
{
    public required string PortId { get; init; }
    public string? PriorStablePortId { get; init; }
    public required string ComponentId { get; init; }
    public required string PortName { get; init; }
    public string? PortRole { get; init; }
    public string? Direction { get; init; }
    public string? SignalType { get; init; }
    public string? Voltage { get; init; }
    public string? Protocol { get; init; }
    public string? Connector { get; init; }
    public string? ConnectorCoding { get; init; }
    public string? Gender { get; init; }
    public int? PinCount { get; init; }
    public int? ActualPinCount { get; init; }
    public string? PhysicalSide { get; init; }
    public string? SourcePage { get; init; }
    public string? Notes { get; init; }
    public ArchiveTopologyEndpointMode? TopologyEndpointMode { get; init; }
    public IReadOnlyList<ArchivePinChange> Pins { get; init; } = Array.Empty<ArchivePinChange>();
}

public sealed record ArchiveComponentChange
{
    public required string ComponentId { get; init; }
    public string? PriorStableComponentId { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public string? Voltage { get; init; }
    public string? IOType { get; init; }
    public string? OutputType { get; init; }
    public string? Protocol { get; init; }
    public string? GeometryType { get; init; }
    public double? WidthMm { get; init; }
    public double? HeightMm { get; init; }
    public double? DepthMm { get; init; }
    public double? DiameterMm { get; init; }
    public string? MountingType { get; init; }
    public string? Fastener { get; init; }
    public string? DatasheetPath { get; init; }
    public string? ImagePath { get; init; }
    public string? DrawingPath { get; init; }
    public string? DatasheetUrl { get; init; }
    public string? DrawingUrl { get; init; }
    public ArchiveTopologyStatus TopologyStatus { get; init; } = ArchiveTopologyStatus.Review;
    public ArchiveLayoutStatus LayoutStatus { get; init; } = ArchiveLayoutStatus.Review;
    public IReadOnlyList<ArchivePortChange> Ports { get; init; } = Array.Empty<ArchivePortChange>();
    public IReadOnlyList<ArchiveEvidenceReference> Evidence { get; init; } = Array.Empty<ArchiveEvidenceReference>();
}

public sealed record ArchiveChangeSet
{
    public required string JobId { get; init; }
    public required string PolicyVersion { get; init; }
    public string? PolicyRevision { get; init; }
    public required ArchiveOperation Operation { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public string? ExistingComponentId { get; init; }
    public IReadOnlyList<ArchiveComponentChange> Components { get; init; } = Array.Empty<ArchiveComponentChange>();
    public IReadOnlyList<string> DocumentPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnresolvedUnknowns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Conflicts { get; init; } = Array.Empty<string>();
}

public sealed record ArchiveValidationIssue
{
    public required string RuleId { get; init; }
    public required ArchiveValidationSeverity Severity { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> ObjectIds { get; init; } = Array.Empty<string>();
}

public sealed record ArchiveValidationReport
{
    public required string JobId { get; init; }
    public required ArchiveValidationStatus Status { get; init; }
    public IReadOnlyList<ArchiveValidationIssue> Issues { get; init; } = Array.Empty<ArchiveValidationIssue>();

    [JsonIgnore]
    public bool CanWrite => Status == ArchiveValidationStatus.PASS;
}

public sealed record ArchiveReadbackResult
{
    public required string JobId { get; init; }
    public required ArchiveReadbackStatus ReadbackStatus { get; init; }
    public required ArchiveJobStatus JobStatus { get; init; }
    public string? ExpectedRevision { get; init; }
    public string? ObservedRevision { get; init; }
    public string? SyncStatus { get; init; }
    public IReadOnlyList<string> Mismatches { get; init; } = Array.Empty<string>();
}

public static class ArchiveJson
{
    public static JsonSerializerOptions CreateOptions(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
