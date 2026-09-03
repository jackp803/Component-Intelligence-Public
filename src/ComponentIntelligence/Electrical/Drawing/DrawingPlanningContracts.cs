namespace ComponentIntelligence.Electrical.Drawing;

public enum DrawingRepresentationRole { Schematic, ConnectorDetail, PanelFootprint, TopologyVisual, CableFunctional, CableDetail, PowerReference, ParentChildElectrical, HeavyDuty }
public enum DrawingRepresentationFamily { FunctionalGeneric, ImageModule, StandardSymbol, ArchivedExact, CableFunctional, CableDetail, ConnectorDetail, HeavyDuty, ParentChildElectrical }
public enum DrawingRepresentationControlState { Auto, ManualOverride, Locked }
public enum DrawingRepresentationOwnerKind { Component, CableInstance, CableAssembly, Terminal, Network, PowerDomain, SeriesChain, HeavyDutyConnector }
public enum DrawingPlanningIssueSeverity { Info, Warning, Blocker }
public enum DrawingInterfaceLayoutFamily { M12, RJ45, LooseLead, Special, Other }

public sealed record DrawingPlanningInput
{
    public const string V1 = "electrical-drawing-planning-input.v1";
    public string SchemaVersion { get; init; } = V1;
    public required string ProjectId { get; init; }
    public string? PlanningInputHash { get; init; }
    public List<DrawingRepresentationDecision> Representations { get; init; } = [];
    public List<DrawingConnectionPlanningItem> Connections { get; init; } = [];
    public List<DrawingCablePlanningItem> Cables { get; init; } = [];
    public List<DrawingControllerModuleItem> ControllerModules { get; init; } = [];
    public List<DrawingNetworkItem> Networks { get; init; } = [];
    public List<DrawingSeriesChainItem> SeriesChains { get; init; } = [];
    public List<DrawingHeavyDutyItem> HeavyDutyConnectors { get; init; } = [];
    public List<DrawingPowerDomainItem> PowerDomains { get; init; } = [];
    public List<DrawingWiringRuleItem> WiringRules { get; init; } = [];
    public List<DrawingPlanningIssue> Issues { get; init; } = [];
}

public sealed record DrawingRepresentationDecision
{
    public required string RepresentationId { get; init; }
    public DrawingRepresentationOwnerKind OwnerKind { get; init; }
    public required string OwnerId { get; init; }
    public DrawingRepresentationRole Role { get; init; }
    public DrawingRepresentationFamily Family { get; init; }
    public DrawingRepresentationControlState ControlState { get; init; }
    public IReadOnlyList<int> AllowedRotations { get; init; } = [0];
    public string? SourceType { get; init; }
    public string? AssetRevision { get; init; }
    public string? AssetPath { get; init; }
    public string? AssetHashSha256 { get; init; }
    public IReadOnlyList<DrawingPortBinding> PortBindings { get; init; } = [];
    public string? FieldDeviceClass { get; init; }
    public string? ControllerId { get; init; }
    public string? PhysicalModuleId { get; init; }
    public string? FunctionKind { get; init; }
    public string? MachineZoneId { get; init; }
    public string? NetworkId { get; init; }
    public string? NetworkKind { get; init; }
    public string? SeriesChainId { get; init; }
    public string? HeavyDutyConnectorId { get; init; }
    public bool PhysicalInterfaceMeaning { get; init; }
}

public sealed record DrawingPortBinding
{
    public required string EngineeringEndpointId { get; init; }
    public required string ConnectionPointId { get; init; }
}

public sealed record DrawingConnectionPlanningItem
{
    public required string ConnectionId { get; init; }
    public required string FromEndpointId { get; init; }
    public required string ToEndpointId { get; init; }
    public string? NetId { get; init; }
    public string? CableInstanceId { get; init; }
    public string? ControllerId { get; init; }
    public string? PhysicalModuleId { get; init; }
    public string? FunctionKind { get; init; }
    public string? MachineZoneId { get; init; }
    public string? NetworkId { get; init; }
    public string? SeriesChainId { get; init; }
    public string? HeavyDutyConnectorId { get; init; }
    public bool PhysicalInterfaceMeaning { get; init; }
}

public sealed record DrawingCableEndpoint
{
    public required string EndpointId { get; init; }
    public string? ConnectorId { get; init; }
    public DrawingInterfaceLayoutFamily InterfaceLayoutFamily { get; init; } = DrawingInterfaceLayoutFamily.Other;
    public string? ConnectorFamily { get; init; }
    public string? Coding { get; init; }
    public int? PinCount { get; init; }
    public IReadOnlyList<string> ContactIds { get; init; } = [];
}

public sealed record DrawingPinCoreMapping
{
    public required string MappingId { get; init; }
    public required string CoreId { get; init; }
    public string? EndAContactId { get; init; }
    public string? EndBContactId { get; init; }
    public string? Function { get; init; }
}

public sealed record DrawingCablePlanningItem
{
    public required string CableInstanceId { get; init; }
    public required string ConstructionType { get; init; }
    public required DrawingCableEndpoint EndA { get; init; }
    public required DrawingCableEndpoint EndB { get; init; }
    public IReadOnlyList<DrawingPinCoreMapping> PinCoreMappings { get; init; } = [];
    public string? Shield { get; init; }
    public double? Length { get; init; }
    public string? SourceControllerId { get; init; }
    public string? SourcePhysicalModuleId { get; init; }
}

public sealed record DrawingControllerModuleItem
{
    public required string ControllerModuleId { get; init; }
    public required string ControllerId { get; init; }
    public string? PhysicalModuleId { get; init; }
    public string? FunctionKind { get; init; }
    public string? MachineZoneId { get; init; }
    public IReadOnlyList<string> RepresentationIds { get; init; } = [];
}

public sealed record DrawingNetworkItem
{
    public required string NetworkId { get; init; }
    public string? NetworkKind { get; init; }
    public IReadOnlyList<string> RepresentationIds { get; init; } = [];
    public IReadOnlyList<string> ConnectionIds { get; init; } = [];
}

public sealed record DrawingSeriesChainItem
{
    public required string SeriesChainId { get; init; }
    public IReadOnlyList<string> RepresentationIds { get; init; } = [];
    public IReadOnlyList<string> ConnectionIds { get; init; } = [];
}

public sealed record DrawingHeavyDutyItem
{
    public required string HeavyDutyConnectorId { get; init; }
    public IReadOnlyList<string> RepresentationIds { get; init; } = [];
    public IReadOnlyList<string> ContactIds { get; init; } = [];
    public int RowsPerPage { get; init; } = 16;
}

public sealed record DrawingPowerDomainItem
{
    public required string PowerDomainId { get; init; }
    public IReadOnlyList<string> RepresentationIds { get; init; } = [];
}

public sealed record DrawingWiringRuleItem
{
    public required string WiringRuleId { get; init; }
    public required string RuleKind { get; init; }
    public object? Value { get; init; }
    public required string Source { get; init; }
}

public sealed record DrawingPlanningIssue
{
    public required string IssueId { get; init; }
    public DrawingPlanningIssueSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetId { get; init; }
}
