using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical;

internal static class Week1FirstSliceV2Fixture
{
    public const string ProjectId = "2fe3fb260c7d4c0eb3f5661e4b112a01";
    public const string BelimoComponentId = "bom:15:belimo:ev015r2-kbac:1";
    public const string X3ComponentId = "cmp-common-302774bd5b9e4bb5a03eae34736665fc";

    public const string ConnectionDPlusId = "conn-83ac398c69554ce882726b27557bebcd";
    public const string ConnectionDMinusId = "conn-8032fbadfc904b58bd62368f078c6abe";
    public const string ConnectionCommonId = "conn-1f91e8253f494afa801b1b2e3dea19dd";

    public const string X3Pin1Id = X3ComponentId + ":PORT:WIRE-B:PIN:1";
    public const string X3Pin2Id = X3ComponentId + ":PORT:WIRE-B:PIN:2";
    public const string X3Pin3Id = X3ComponentId + ":PORT:WIRE-B:PIN:3";
    public const string BelimoPin7Id = BelimoComponentId + ":port:belimo-ev015r2-kbac-control:pin:belimo_ev015r2_u002b_kbac_control_7";
    public const string BelimoPin6Id = BelimoComponentId + ":port:belimo-ev015r2-kbac-control:pin:belimo_ev015r2_u002b_kbac_control_6";
    public const string BelimoPin1Id = BelimoComponentId + ":port:belimo-ev015r2-kbac-control:pin:belimo_ev015r2_u002b_kbac_control_1";

    public static readonly string[] ConnectionIds =
    [
        ConnectionCommonId,
        ConnectionDMinusId,
        ConnectionDPlusId
    ];

    public static ElectricalProject CreateProject(bool reverseInputOrder = false)
    {
        var project = new ElectricalProject
        {
            ProjectId = ProjectId,
            Name = "Week 1 first-slice sanitized fixture"
        };
        var components = new[]
        {
            X3Component(reverseInputOrder),
            BelimoComponent(reverseInputOrder)
        };
        var connections = new[]
        {
            Connection(ConnectionDPlusId, X3Pin1Id, BelimoPin7Id),
            Connection(ConnectionDMinusId, X3Pin2Id, BelimoPin6Id),
            Connection(ConnectionCommonId, X3Pin3Id, BelimoPin1Id)
        };

        project.Components.AddRange(reverseInputOrder ? components.Reverse() : components);
        project.Connections.AddRange(reverseInputOrder ? connections.Reverse() : connections);
        return project;
    }

    public static IReadOnlyList<AutocadConnectionPointBinding> AuditedBindings(bool reverseInputOrder = false)
    {
        var endpointIds = new[]
        {
            X3Pin1Id,
            BelimoPin7Id,
            X3Pin2Id,
            BelimoPin6Id,
            X3Pin3Id,
            BelimoPin1Id
        };
        return (reverseInputOrder ? endpointIds.Reverse() : endpointIds)
            .Select(endpointId => new AutocadConnectionPointBinding
            {
                EndpointId = endpointId,
                SymbolKey = endpointId.StartsWith(X3ComponentId, StringComparison.Ordinal)
                    ? "TEST-ONLY:X3-M12-A-4PIN"
                    : "TEST-ONLY:BELIMO-EV015R2-KBAC",
                ConnectionPointId = $"TEST-ONLY:XTERM:{endpointId}"
            })
            .ToArray();
    }

    public static AutocadEngineeringDrawingEvidence DrawingEvidence(bool reverseInputOrder = false)
    {
        var roles = new[]
        {
            new AutocadComponentDrawingRoleEvidence
            {
                ComponentInstanceId = BelimoComponentId,
                Role = ComponentDrawingRole.ValveOrPump,
                Status = DrawingEvidenceStatus.Confirmed,
                EvidenceSource = "test-only-pm-accepted-first-slice"
            },
            new AutocadComponentDrawingRoleEvidence
            {
                ComponentInstanceId = X3ComponentId,
                Role = ComponentDrawingRole.CableOrConnector,
                Status = DrawingEvidenceStatus.Confirmed,
                EvidenceSource = "test-only-pm-accepted-first-slice"
            }
        };
        return new AutocadEngineeringDrawingEvidence
        {
            ComponentRoles = reverseInputOrder ? roles.Reverse().ToArray() : roles
        };
    }

    private static ComponentInstance X3Component(bool reversePins)
    {
        var pins = new[]
        {
            Pin(X3Pin1Id, "1", null),
            Pin(X3Pin2Id, "2", null),
            Pin(X3Pin3Id, "3", null)
        };
        return new ComponentInstance
        {
            ComponentInstanceId = X3ComponentId,
            ComponentDefinitionId = "common:cable-end:m12-male-a-4pin",
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = "X3",
            DisplayName = "M12 male A-code 4-pin field cable end",
            ResponsibilityScope = ResponsibilityScope.InScope,
            Ports =
            {
                new ComponentPort
                {
                    PortId = X3ComponentId + ":PORT:WIRE-B",
                    Name = "WIRE-B",
                    Pins = { }
                }
            }
        }.WithPins(reversePins ? pins.Reverse() : pins);
    }

    private static ComponentInstance BelimoComponent(bool reversePins)
    {
        var pins = new[]
        {
            Pin(BelimoPin7Id, "7", "BACnet MS/TP / Modbus RTU differential data positive.", "C2 / D+ / grey"),
            Pin(BelimoPin6Id, "6", "BACnet MS/TP / Modbus RTU differential data negative.", "C1 / D- / pink"),
            Pin(BelimoPin1Id, "1", "Common conductor for 24 V supply and serial-bus reference.", "COM / black")
        };
        return new ComponentInstance
        {
            ComponentInstanceId = BelimoComponentId,
            ComponentDefinitionId = "BELIMO_EV015R2+KBAC",
            TypeKey = "Energy Valve / Control Valve",
            DisplayName = "BELIMO EV015R2+KBAC",
            ResponsibilityScope = ResponsibilityScope.InScope,
            Ports =
            {
                new ComponentPort
                {
                    PortId = BelimoComponentId + ":port:belimo-ev015r2-kbac-control",
                    Name = "CONTROL_LEADS",
                    Protocol = "Modbus",
                    Pins = { }
                }
            }
        }.WithPins(reversePins ? pins.Reverse() : pins);
    }

    private static ComponentInstance WithPins(this ComponentInstance component, IEnumerable<ComponentPin> pins)
    {
        component.Ports[0].Pins.AddRange(pins);
        return component;
    }

    private static ComponentPin Pin(string pinId, string pinNumber, string? function, string? pinName = null) => new()
    {
        PinId = pinId,
        PinNumber = pinNumber,
        PinName = pinName,
        Function = function,
        Protocol = function is null ? null : "RS485",
        SignalStandardRaw = function is null ? null : "RS-485",
        Layer = function is null ? ElectricalLayer.Unknown : ElectricalLayer.Communication,
        Status = function is null ? PinStatus.Unknown : PinStatus.Normal
    };

    private static ElectricalConnection Connection(string connectionId, string fromEndpointId, string toEndpointId) => new()
    {
        ConnectionId = connectionId,
        FromEndpointId = fromEndpointId,
        ToEndpointId = toEndpointId,
        NetId = null,
        Kind = ConnectionKind.Wire
    };
}
