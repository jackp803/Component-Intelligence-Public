namespace ComponentIntelligence.Electrical.Domain;

public enum ElectricalLayer
{
    Unknown,
    Power,
    Analog,
    Digital,
    Communication,
    Grounding,
    Safety
}

public enum GroundReferenceType
{
    None,
    PowerReturn,
    SignalGround,
    ProtectiveEarth,
    FunctionalEarth,
    Chassis,
    Shield,
    IsolatedReference,
    Unknown
}

public enum PinStatus
{
    Normal,
    Unused,
    Optional,
    Nc,
    Reserved,
    Unknown
}

public enum ResponsibilityScope
{
    InScope,
    OutOfScope,
    InterfaceOnly,
    Optional,
    NotRequired,
    Unknown
}

public enum ReferenceSource
{
    Imported,
    AutoAssigned,
    Manual,
    Migrated
}

public enum ConnectorGender
{
    Unknown,
    Male,
    Female,
    Genderless
}

public enum ConnectorMountType
{
    Unknown,
    Cable,
    Panel,
    Device,
    Pcb,
    Terminal
}

public enum ConnectorOrientation
{
    Unknown,
    Straight,
    RightAngle
}

public enum ConnectionKind
{
    Wire,
    Cable,
    DirectMating,
    Internal
}

public enum VoltageType
{
    Unknown,
    Dc,
    Ac
}

public enum ConductorMaterial
{
    Unknown,
    Copper,
    Aluminum
}

/// <summary>
/// Identifies who supplied the authoritative engineering cable length. Physical layout and drawn
/// cable routes are intentionally not cable-length sources: the mechanical/user/imported value is
/// the engineering input used for voltage-drop sizing and BOM quantity.
/// </summary>
public enum CableLengthSource
{
    Unknown,
    Mechanical,
    User,
    Imported
}

/// <summary>
/// Mounting face used by the 2.5D cabinet-fit validator. XY coordinates are local to the selected
/// face; therefore XY overlap on different faces is not automatically a physical collision.
/// </summary>
public enum MountingSurface
{
    Unknown,
    Backplate,
    Door,
    LeftWall,
    RightWall,
    Top,
    Bottom,
    External
}

public enum Polarity
{
    None,
    Positive,
    Negative,
    Return,
    Unknown
}

public enum PowerRole
{
    None,
    Source,
    Input,
    Return,
    Unknown
}

public enum DigitalIoType
{
    None,
    Di,
    Do,
    Bidirectional,
    Unknown
}

public enum SwitchingType
{
    None,
    Pnp,
    Npn,
    PushPull,
    DryContact,
    OpenCollector,
    Unknown
}

public enum ActiveLevel
{
    Unknown,
    High,
    Low
}

public enum DigitalElectricalBehavior
{
    Unknown,
    Source,
    Sink,
    Passive
}

public enum AnalogDirection
{
    None,
    Input,
    Output,
    Bidirectional,
    Unknown
}

public enum AnalogSignalStandard
{
    Unknown,
    Current4To20mA,
    Voltage0To10V,
    VoltagePlusMinus10V,
    Custom
}

public enum AnalogWiringScheme
{
    Unknown,
    TwoWireLoop,
    ThreeWire,
    FourWire,
    Differential,
    SingleEnded
}

public enum LoopRole
{
    Unknown,
    ActiveSource,
    PassiveDevice,
    ActiveInput,
    PassiveInput
}

public enum DifferentialRole
{
    None,
    Positive,
    Negative,
    Unknown
}

public enum ConnectionPointType
{
    ConductorEntry,
    JumperSlot,
    TestPoint,
    PeConnection,
    InternalConnection,
    MechanicalOnly
}

public enum MountingType
{
    Unknown,
    DinRail,
    Backplate,
    PanelCutout,
    Door,
    Surface,
    MachineFrame,
    FreeStanding
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
    Block
}

public enum DrawingReadiness
{
    Ready,
    ReviewRequired,
    Blocked
}

public enum EndpointDisposition
{
    None,
    IntentionallyUnused,
    OutOfScope,
    Tbd,
    NotApplicable,
    ReturnToEdit
}

public sealed record VoltageSpecification
{
    public VoltageType Type { get; init; } = VoltageType.Unknown;
    public double? NominalVoltage { get; init; }
    public double? MinVoltage { get; init; }
    public double? MaxVoltage { get; init; }
}

public sealed record PowerCapability
{
    public PowerRole Role { get; init; } = PowerRole.Unknown;
    public Polarity Polarity { get; init; } = Polarity.Unknown;
    public VoltageSpecification? Voltage { get; init; }
    public double? RequiredCurrentAmp { get; init; }
    public double? MaxCurrentAmp { get; init; }
}

public sealed record DigitalCapability
{
    public DigitalIoType IoType { get; init; } = DigitalIoType.Unknown;
    public SwitchingType SwitchingType { get; init; } = SwitchingType.Unknown;
    public ActiveLevel ActiveLevel { get; init; } = ActiveLevel.Unknown;
    public DigitalElectricalBehavior ElectricalBehavior { get; init; } = DigitalElectricalBehavior.Unknown;
    public double? MaxOutputCurrentAmp { get; init; }
    public double? RequiredInputCurrentAmp { get; init; }
}

public sealed record AnalogCapability
{
    public AnalogDirection Direction { get; init; } = AnalogDirection.Unknown;
    public AnalogSignalStandard SignalStandard { get; init; } = AnalogSignalStandard.Unknown;
    public AnalogWiringScheme WiringScheme { get; init; } = AnalogWiringScheme.Unknown;
    public LoopRole LoopRole { get; init; } = LoopRole.Unknown;
}
