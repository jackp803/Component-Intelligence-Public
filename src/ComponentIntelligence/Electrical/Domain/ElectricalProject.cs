namespace ComponentIntelligence.Electrical.Domain;

public sealed class ElectricalProject
{
    public string SchemaVersion { get; init; } = "0.3";
    public required string ProjectId { get; init; }
    public string? Name { get; set; }
    public List<ComponentInstance> Components { get; init; } = new();
    public List<NetDefinition> Nets { get; init; } = new();
    public List<ElectricalConnection> Connections { get; init; } = new();
    public List<CommunicationBus> Buses { get; init; } = new();
    public List<CableInstance> Cables { get; init; } = new();
    public List<CableAssembly> CableAssemblies { get; init; } = new();
    public List<TerminalBlock> TerminalBlocks { get; init; } = new();
    public List<LayoutContainer> LayoutContainers { get; init; } = new();
    public List<DinRail> DinRails { get; init; } = new();
    public List<CableDuct> CableDucts { get; init; } = new();
    public List<CableRoute> CableRoutes { get; init; } = new();
    public List<UnconnectedEndpointReview> EndpointReviews { get; init; } = new();
    public List<TopologyPlacement> TopologyPlacements { get; init; } = new();
    public List<TopologyRouteGeometry> TopologyRoutes { get; init; } = new();
}

public sealed class ComponentInstance
{
    public required string ComponentInstanceId { get; init; }
    public required string ComponentDefinitionId { get; init; }
    public required string TypeKey { get; set; }
    public string? ReferenceDesignator { get; set; }
    public ReferenceSource ReferenceSource { get; set; } = ReferenceSource.AutoAssigned;
    public bool ReferenceLocked { get; set; }
    public string? EquipmentTag { get; set; }
    public string? DisplayName { get; set; }
    public ResponsibilityScope ResponsibilityScope { get; set; } = ResponsibilityScope.Unknown;
    public List<ComponentPort> Ports { get; init; } = new();
    public List<PowerConversionEvidence> PowerConversions { get; init; } = new();
    public PhysicalFootprint? Footprint { get; set; }
    public bool FootprintOverride { get; set; }
    public PhysicalPlacement? Placement { get; set; }
}

public sealed class ComponentPort
{
    public required string PortId { get; init; }
    public required string Name { get; set; }
    public string? SourcePortId { get; set; }
    public string? PowerDomainId { get; set; }
    public string? Protocol { get; set; }
    public List<string> Capabilities { get; init; } = new();
    public ConnectorDefinition? Connector { get; set; }
    public int? MaxConnections { get; set; }
    public PhysicalPortLocation? PhysicalLocation { get; set; }
    public List<ComponentPin> Pins { get; init; } = new();
}

public sealed class ComponentPin
{
    public required string PinId { get; init; }
    public string? SourcePinId { get; set; }
    public string? PowerDomainId { get; set; }
    public required string PinNumber { get; set; }
    public string? PinName { get; set; }
    public string? Function { get; set; }
    public string? Protocol { get; set; }
    public string? SignalStandardRaw { get; set; }
    public ElectricalLayer Layer { get; set; } = ElectricalLayer.Unknown;
    public PinStatus Status { get; set; } = PinStatus.Unknown;
    public bool IsRequired { get; set; }
    public GroundReferenceType GroundReferenceType { get; set; } = GroundReferenceType.None;
    public string? IsolationDomainId { get; set; }
    public DifferentialRole DifferentialRole { get; set; } = DifferentialRole.None;
    public PowerCapability? Power { get; set; }
    public DigitalCapability? Digital { get; set; }
    public AnalogCapability? Analog { get; set; }
}

public sealed class ConnectorDefinition
{
    public required string ConnectorId { get; init; }
    public required string Family { get; set; }
    public string? SeriesOrSize { get; set; }
    public int? PinCount { get; set; }
    public string? Coding { get; set; }
    public ConnectorGender Gender { get; set; } = ConnectorGender.Unknown;
    public ConnectorMountType MountType { get; set; } = ConnectorMountType.Unknown;
    public ConnectorOrientation Orientation { get; set; } = ConnectorOrientation.Unknown;
    public bool? Shielded { get; set; }
    public string? CompatibilityClass { get; set; }
    public double? MinTerminationAreaMm2 { get; set; }
    public double? MaxTerminationAreaMm2 { get; set; }
}

public sealed class NetDefinition
{
    public required string NetId { get; init; }
    public required string Label { get; set; }
    public ElectricalLayer Layer { get; set; } = ElectricalLayer.Unknown;
    public GroundReferenceType GroundReferenceType { get; set; } = GroundReferenceType.None;
    public string? IsolationDomainId { get; set; }
    public string? BusId { get; set; }
}

public sealed class ElectricalConnection
{
    public required string ConnectionId { get; init; }
    public required string FromEndpointId { get; init; }
    public required string ToEndpointId { get; init; }
    public string? NetId { get; set; }
    public ConnectionKind Kind { get; set; } = ConnectionKind.Wire;
    public string? CableInstanceId { get; set; }
    public string? CableCoreId { get; set; }
    public double? ConductorAreaMm2 { get; set; }
    public double? ProvidedLengthMm { get; set; }
    public CableLengthSource LengthSource { get; set; } = CableLengthSource.Unknown;
    public double? MaxVoltageDropPercent { get; set; }
    public ConductorMaterial ConductorMaterial { get; set; } = ConductorMaterial.Unknown;
    public string? InstallationMethod { get; set; }
}

public sealed class CommunicationBus
{
    public required string BusId { get; init; }
    public required string Protocol { get; init; }
    public int? BaudRate { get; set; }
    public List<string> NetIds { get; init; } = new();
    public Dictionary<string, int> NodeAddresses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CableDefinition
{
    public required string CableDefinitionId { get; init; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public int CoreCount { get; set; }
    public double? VoltageRating { get; set; }
    public bool? Shielded { get; set; }
    public bool? DragChainSuitable { get; set; }
    public List<string> CommunicationCapabilities { get; init; } = new();
    public List<CableCoreDefinition> Cores { get; init; } = new();
}

public sealed class CableCoreDefinition
{
    public required string CoreId { get; init; }
    public required string CoreNumber { get; init; }
    public string? ColorCode { get; set; }
    public double? AreaMm2 { get; set; }
    public int? Awg { get; set; }
    public string? PairGroup { get; set; }
    public string? ShieldGroup { get; set; }
    public double? CurrentRatingAmp { get; set; }
    public ConductorMaterial Material { get; set; } = ConductorMaterial.Unknown;
}

public sealed class CableInstance
{
    public required string CableInstanceId { get; init; }
    public required string CableDefinitionId { get; set; }
    public string? DisplayName { get; set; }
    public string? ReferenceDesignator { get; set; }
    public double? ProvidedLengthMm { get; set; }
    public CableLengthSource LengthSource { get; set; } = CableLengthSource.Unknown;
    public List<CoreAssignment> CoreAssignments { get; init; } = new();
}

public sealed class CoreAssignment
{
    public required string CoreId { get; init; }
    public string? NetId { get; set; }
    public string? Signal { get; set; }
    public ElectricalLayer Layer { get; set; } = ElectricalLayer.Unknown;
    public string Status { get; set; } = "UNUSED";
    public string? FromEndpointId { get; set; }
    public string? ToEndpointId { get; set; }
}

public sealed class CableAssembly
{
    public required string CableAssemblyId { get; init; }
    public string? ReferenceDesignator { get; set; }
    public bool IsCustom { get; set; }
    public List<CableAssemblyMember> Members { get; init; } = new();
    public string? EndAConnectorId { get; set; }
    public string? EndBConnectorId { get; set; }
}

public sealed class CableAssemblyMember
{
    public required string CableInstanceId { get; init; }
    public string? Purpose { get; set; }
}

public sealed class TerminalBlock
{
    public required string TerminalBlockId { get; init; }
    public required string ReferenceDesignator { get; set; }
    public string? FunctionTag { get; set; }
    public string? DisplayName { get; set; }
    public List<TerminalPosition> Positions { get; init; } = new();
    public List<ShortingJumper> Jumpers { get; init; } = new();
    public PhysicalFootprint? Footprint { get; set; }
    public PhysicalPlacement? Placement { get; set; }
}

public sealed class TerminalPosition
{
    public required string TerminalPositionId { get; init; }
    public required string PositionLabel { get; init; }
    public string? TerminalType { get; set; }
    public List<TerminalLevel> Levels { get; init; } = new();
}

public sealed class TerminalLevel
{
    public required string LevelId { get; init; }
    public required string LevelName { get; init; }
    public List<TerminalConnectionPoint> ConnectionPoints { get; init; } = new();
    public List<InternalTerminalConnection> InternalConnections { get; init; } = new();
}

public sealed class TerminalConnectionPoint
{
    public required string ConnectionPointId { get; init; }
    public required ConnectionPointType Type { get; init; }
    public string? PhysicalSide { get; set; }
    public int MaxConductors { get; set; } = 1;
    public double? MinWireAreaMm2 { get; set; }
    public double? MaxWireAreaMm2 { get; set; }
}

public sealed class InternalTerminalConnection
{
    public required string FromConnectionPointId { get; init; }
    public required string ToConnectionPointId { get; init; }
}

public sealed class ShortingJumper
{
    public required string JumperId { get; init; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public int? PoleCount { get; set; }
    public double? CurrentRatingAmp { get; set; }
    public List<string> ConnectionPointIds { get; init; } = new();
}

public sealed class LayoutContainer
{
    public required string ContainerId { get; init; }
    public required string Name { get; set; }
    public string? ParentContainerId { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double? DepthMm { get; set; }
    public List<LayoutZone> Zones { get; init; } = new();
}

public sealed class LayoutZone
{
    public required string ZoneId { get; init; }
    public required string Name { get; init; }
    public required RectMm Bounds { get; init; }
    public MountingSurface Surface { get; set; } = MountingSurface.Unknown;
    public bool IsKeepOut { get; set; }
    public bool IsForbidden { get; set; }
}

public sealed record RectMm(double X, double Y, double Width, double Height);

public sealed class PhysicalFootprint
{
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double? DepthMm { get; set; }
    public MountingType MountingType { get; set; } = MountingType.Unknown;
    public ClearanceRequirement Clearance { get; set; } = new();
}

public sealed class ClearanceRequirement
{
    public double TopMm { get; set; }
    public double BottomMm { get; set; }
    public double LeftMm { get; set; }
    public double RightMm { get; set; }
    public double WiringMm { get; set; }
    public double ConnectorMm { get; set; }
    public double ServiceMm { get; set; }
}

public sealed class PhysicalPlacement
{
    public required string ParentContainerId { get; init; }
    public double XMm { get; set; }
    public double YMm { get; set; }
    public int RotationDegrees { get; set; }
    public ComponentMountOrientation MountOrientation { get; set; } = ComponentMountOrientation.Front;
    public string? MountTargetId { get; set; }
    public MountingSurface Surface { get; set; } = MountingSurface.Unknown;
    public double DepthOffsetMm { get; set; }
}

public sealed class PhysicalPortLocation
{
    public string? Side { get; set; }
    public double? LocalXMm { get; set; }
    public double? LocalYMm { get; set; }
    public string? FacingDirection { get; set; }
}

public sealed class DinRail
{
    public required string DinRailId { get; init; }
    public required string ParentContainerId { get; init; }
    public double XMm { get; set; }
    public double YMm { get; set; }
    public double LengthMm { get; set; }
    public bool Horizontal { get; set; } = true;
    public MountingSurface Surface { get; set; } = MountingSurface.Unknown;
}

public sealed class CableDuct
{
    public required string CableDuctId { get; init; }
    public required string ParentContainerId { get; init; }
    public required RectMm Bounds { get; init; }
    public MountingSurface Surface { get; set; } = MountingSurface.Unknown;
}

/// <summary>
/// Visual/physical routing geometry. Segment geometry may be used for drawing and routing review,
/// but must not be treated as the authoritative engineering cable length.
/// </summary>
public sealed class CableRoute
{
    public required string CableRouteId { get; init; }
    public required string ConnectionOrCableId { get; init; }
    public List<RouteSegment> Segments { get; init; } = new();
}

public sealed class RouteSegment
{
    public required string SegmentId { get; init; }
    public double StartXMm { get; set; }
    public double StartYMm { get; set; }
    public double EndXMm { get; set; }
    public double EndYMm { get; set; }
    public string? CableDuctId { get; set; }
}

public sealed class UnconnectedEndpointReview
{
    public required string EndpointId { get; init; }
    public EndpointDisposition Disposition { get; set; } = EndpointDisposition.None;
    public string? Reason { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public sealed class TopologyPlacement
{
    public required string ObjectId { get; init; }
    public required string ObjectKind { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 140;
    public double Height { get; set; } = 76;
    public int RotationDegrees { get; set; }
}

/// <summary>
/// Saved topology-canvas geometry. These coordinates preserve the engineer's visual route across
/// save/load and are never used as authoritative cable length or physical installation distance.
/// </summary>
public sealed class TopologyRouteGeometry
{
    public required string ConnectionId { get; init; }
    public List<TopologyRoutePoint> Points { get; init; } = new();
    public double? ManualWaypointX { get; set; }
    public double? ManualWaypointY { get; set; }
}

public sealed class TopologyRoutePoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
