using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;
using ContractPin = ComponentIntelligence.Contracts.ComponentPin;
using DomainPort = ComponentIntelligence.Electrical.Domain.ComponentPort;
using DomainPin = ComponentIntelligence.Electrical.Domain.ComponentPin;

namespace ComponentIntelligence.Electrical.Bridging;

public sealed class ComponentProjectBridge
{
    public ComponentInstance CreateInstance(
        ComponentIR source,
        string componentInstanceId,
        string? referenceDesignator = null,
        string? equipmentTag = null,
        string? typeKey = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentInstanceId);

        var instance = new ComponentInstance
        {
            ComponentInstanceId = componentInstanceId,
            ComponentDefinitionId = source.Identity.ComponentId,
            TypeKey = FirstNonBlank(typeKey, source.Classification.Subcategory, source.Classification.Category, "COMPONENT")!,
            ReferenceDesignator = NullIfBlank(referenceDesignator),
            ReferenceSource = string.IsNullOrWhiteSpace(referenceDesignator) ? ReferenceSource.AutoAssigned : ReferenceSource.Manual,
            ReferenceLocked = !string.IsNullOrWhiteSpace(referenceDesignator),
            EquipmentTag = NullIfBlank(equipmentTag),
            DisplayName = $"{source.Identity.Manufacturer} {source.Identity.Model}".Trim(),
            ResponsibilityScope = ResponsibilityScope.Unknown
        };

        foreach (var sourcePort in source.Ports)
            instance.Ports.Add(MapPort(sourcePort, source, componentInstanceId));

        foreach (var sourcePin in source.Pins)
        {
            var explicitOwner = FindExplicitPinOwner(instance, sourcePin.PortId);
            if (explicitOwner is not null)
            {
                explicitOwner.Pins.Add(MapPin(sourcePin, source, componentInstanceId, sourcePin.PortId));
                continue;
            }

            // A declared-but-unmatched PortId is engineering evidence of unresolved ownership. Never
            // silently move that pin to another port just because the component currently has one port.
            if (!string.IsNullOrWhiteSpace(sourcePin.PortId))
            {
                var unresolved = GetOrCreateUnassignedPinPort(instance, componentInstanceId, source);
                if (!unresolved.Capabilities.Contains("NEEDS_PORT_MAPPING", StringComparer.OrdinalIgnoreCase))
                    unresolved.Capabilities.Add("NEEDS_PORT_MAPPING");
                unresolved.Pins.Add(MapPin(sourcePin, source, componentInstanceId, sourcePin.PortId));
                continue;
            }

            var physicalPorts = instance.Ports
                .Where(port => !string.Equals(port.Name, "UNASSIGNED-PINS", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (physicalPorts.Length == 1)
            {
                physicalPorts[0].Pins.Add(MapPin(sourcePin, source, componentInstanceId, physicalPorts[0].Name));
                continue;
            }

            var fallback = GetOrCreateUnassignedPinPort(instance, componentInstanceId, source);
            if (physicalPorts.Length > 1 && !fallback.Capabilities.Contains("NEEDS_PORT_MAPPING", StringComparer.OrdinalIgnoreCase))
                fallback.Capabilities.Add("NEEDS_PORT_MAPPING");
            fallback.Pins.Add(MapPin(sourcePin, source, componentInstanceId, null));
        }

        return instance;
    }

    private static DomainPort? FindExplicitPinOwner(ComponentInstance instance, string? logicalPortId)
    {
        if (string.IsNullOrWhiteSpace(logicalPortId)) return null;
        var expected = logicalPortId.Trim();
        return instance.Ports.FirstOrDefault(port =>
            string.Equals(port.Name, expected, StringComparison.OrdinalIgnoreCase) ||
            port.Capabilities.Any(capability =>
                capability.StartsWith("SOURCE_PORT_ID:", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(capability["SOURCE_PORT_ID:".Length..].Trim(), expected, StringComparison.OrdinalIgnoreCase)));
    }

    private static DomainPort GetOrCreateUnassignedPinPort(ComponentInstance instance, string instanceId, ComponentIR source)
    {
        var existing = instance.Ports.FirstOrDefault(port =>
            string.Equals(port.Name, "UNASSIGNED-PINS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(port.Name, "PORT-1", StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var hasNoPorts = instance.Ports.Count == 0;
        var port = new DomainPort
        {
            PortId = $"{instanceId}:port:unassigned",
            Name = hasNoPorts ? "PORT-1" : "UNASSIGNED-PINS",
            Connector = hasNoPorts ? MapRootConnector(source, instanceId) : null
        };
        instance.Ports.Add(port);
        return port;
    }

    private static DomainPort MapPort(ContractPort sourcePort, ComponentIR source, string instanceId)
    {
        var portId = string.IsNullOrWhiteSpace(sourcePort.PortId)
            ? $"{instanceId}:port:{Guid.NewGuid():N}"
            : $"{instanceId}:port:{Sanitize(sourcePort.PortId)}";

        var connectorFamily = FirstNonBlank(sourcePort.ConnectorFamily, source.Connector.Family);
        var connector = string.IsNullOrWhiteSpace(connectorFamily)
            ? null
            : new ConnectorDefinition
            {
                ConnectorId = $"{portId}:connector",
                Family = connectorFamily!,
                Coding = FirstNonBlank(sourcePort.ConnectorCoding, source.Connector.Coding),
                PinCount = sourcePort.PinCount ?? source.Connector.Pins,
                Gender = ParseConnectorGender(sourcePort.ConnectorGender),
                MountType = ConnectorMountType.Device
            };

        var port = new DomainPort
        {
            PortId = portId,
            // PortId remains the stable engineering identity while PortName is the human-readable label
            // from the central workbook (INPUT, OUTPUT, X01, FIELD_IO...).
            Name = FirstNonBlank(sourcePort.PortName, sourcePort.PortId, sourcePort.PortType, "PORT")!,
            Protocol = NormalizeProtocol(FirstNonBlank(sourcePort.Protocol, sourcePort.SignalType)),
            Connector = connector,
            PhysicalLocation = string.IsNullOrWhiteSpace(sourcePort.PhysicalSide)
                ? null
                : new PhysicalPortLocation { Side = sourcePort.PhysicalSide!.Trim() }
        };
        if (!string.IsNullOrWhiteSpace(sourcePort.PortId)) port.Capabilities.Add($"SOURCE_PORT_ID:{sourcePort.PortId}");
        if (!string.IsNullOrWhiteSpace(sourcePort.PortRole)) port.Capabilities.Add($"ROLE:{sourcePort.PortRole}");
        if (!string.IsNullOrWhiteSpace(sourcePort.TopologyEndpointMode))
            port.Capabilities.Add($"TOPOLOGY_ENDPOINT_MODE:{sourcePort.TopologyEndpointMode}");
        if (!string.IsNullOrWhiteSpace(sourcePort.SignalType)) port.Capabilities.Add(sourcePort.SignalType!);
        if (!string.IsNullOrWhiteSpace(sourcePort.Direction)) port.Capabilities.Add($"DIRECTION:{sourcePort.Direction}");
        if (!string.IsNullOrWhiteSpace(sourcePort.VoltageDomain)) port.Capabilities.Add($"VOLTAGE_DOMAIN:{sourcePort.VoltageDomain}");
        foreach (var allowed in sourcePort.AllowedConnections.Where(value => !string.IsNullOrWhiteSpace(value)))
            port.Capabilities.Add($"ALLOWED:{allowed}");
        return port;
    }

    private static ConnectorDefinition? MapRootConnector(ComponentIR source, string instanceId)
    {
        if (string.IsNullOrWhiteSpace(source.Connector.Family)) return null;
        return new ConnectorDefinition
        {
            ConnectorId = $"{instanceId}:port:unassigned:connector",
            Family = source.Connector.Family!,
            Coding = source.Connector.Coding,
            PinCount = source.Connector.Pins,
            Gender = ConnectorGender.Unknown,
            MountType = ConnectorMountType.Device
        };
    }

    private static DomainPin MapPin(ContractPin sourcePin, ComponentIR source, string instanceId, string? ownerPortId)
    {
        var raw = string.Join(' ', new[] { sourcePin.Function, sourcePin.PinRole, sourcePin.SignalType, sourcePin.Description }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var layer = DetermineLayer(sourcePin.SignalType, FirstNonBlank(sourcePin.Function, sourcePin.PinRole));
        var ownerIdentity = string.IsNullOrWhiteSpace(ownerPortId) ? "unassigned" : Sanitize(ownerPortId);
        var stableSourcePinId = FirstNonBlank(
            sourcePin.PinId,
            $"{FirstNonBlank(ownerPortId, "unassigned")}:{sourcePin.PinNumber}")!;
        var status = DeterminePinStatus(sourcePin.PinStatus, sourcePin.Function);
        return new DomainPin
        {
            PinId = $"{instanceId}:port:{ownerIdentity}:pin:{EncodeStableIdSegment(stableSourcePinId)}",
            PinNumber = sourcePin.PinNumber,
            PinName = FirstNonBlank(sourcePin.PinName, sourcePin.Description),
            Function = FirstNonBlank(sourcePin.Function, sourcePin.PinRole),
            Protocol = NormalizeProtocol(FirstNonBlank(sourcePin.SignalType, sourcePin.PinRole)),
            SignalStandardRaw = FirstNonBlank(sourcePin.SignalType, sourcePin.PinRole),
            Layer = layer,
            Status = status,
            IsRequired = status == PinStatus.Normal,
            GroundReferenceType = DetermineGroundReference(raw),
            Power = layer == ElectricalLayer.Power ? BuildPowerCapability(sourcePin, source.Power) : null
        };
    }

    private static PowerCapability? BuildPowerCapability(ContractPin pin, ComponentIntelligence.Contracts.ComponentPower sourcePower)
    {
        var voltage = MapVoltage(sourcePower.OperatingVoltage);
        var role = DeterminePowerRole(pin.Direction);
        var function = $"{pin.PinName} {pin.Function} {pin.PinRole} {pin.SignalType} {pin.VoltageDomain} {pin.Description}".ToUpperInvariant();
        var isReturn = ContainsAny(function, "0V", "V-", "L-", "RETURN", "RTN");
        var isPositiveSupply = ContainsAny(function, "+24V", "+54V", "V+", "L+", "SUPPLY+", "POWER+", "POSITIVE") ||
                               (!isReturn && ContainsAny(function, "SUPPLY", "POWER"));

        if (isReturn && role == PowerRole.Unknown) role = PowerRole.Return;

        double? requiredCurrent = null;
        double? maxCurrent = null;
        if (isPositiveSupply)
        {
            if (role == PowerRole.Source)
            {
                maxCurrent = ToDouble(sourcePower.MaximumCurrentAmp);
            }
            else
            {
                requiredCurrent = ToDouble(sourcePower.CurrentConsumptionAmp ?? sourcePower.MaximumCurrentAmp);
            }
        }

        if (voltage is null && role == PowerRole.Unknown && !isReturn && !isPositiveSupply && requiredCurrent is null && maxCurrent is null)
            return null;

        return new PowerCapability
        {
            Role = role,
            Polarity = isReturn ? Polarity.Return : isPositiveSupply ? Polarity.Positive : Polarity.Unknown,
            Voltage = voltage,
            RequiredCurrentAmp = requiredCurrent,
            MaxCurrentAmp = maxCurrent
        };
    }

    private static VoltageSpecification? MapVoltage(NormalizedVoltage? voltage)
    {
        if (voltage is null) return null;
        var min = ToDouble(voltage.Min);
        var max = ToDouble(voltage.Max);
        double? nominal = min is double minValue && max is double maxValue && Math.Abs(minValue - maxValue) < 1e-9 ? minValue : null;
        return new VoltageSpecification
        {
            Type = string.Equals(voltage.Type, "DC", StringComparison.OrdinalIgnoreCase)
                ? VoltageType.Dc
                : string.Equals(voltage.Type, "AC", StringComparison.OrdinalIgnoreCase) ? VoltageType.Ac : VoltageType.Unknown,
            NominalVoltage = nominal,
            MinVoltage = nominal is null ? min : null,
            MaxVoltage = nominal is null ? max : null
        };
    }

    private static PowerRole DeterminePowerRole(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return PowerRole.Unknown;
        var normalized = direction.Trim().ToUpperInvariant();
        if (ContainsAny(normalized, "OUTPUT", "SOURCE", "OUT")) return PowerRole.Source;
        if (ContainsAny(normalized, "INPUT", "SINK", "IN")) return PowerRole.Input;
        if (ContainsAny(normalized, "RETURN")) return PowerRole.Return;
        return PowerRole.Unknown;
    }

    private static double? ToDouble(decimal? value) => value is decimal number ? (double)number : null;

    private static ElectricalLayer DetermineLayer(string? signalType, string? function)
    {
        var text = $"{signalType} {function}".ToUpperInvariant();
        if (ContainsAny(text, "RS485", "RS-485", "ETHERNET", "ETHERCAT", "IO-LINK", "IOLINK", "CAN", "PROFINET", "MODBUS")) return ElectricalLayer.Communication;
        if (ContainsAny(text, "4-20MA", "4…20MA", "4..20MA", "0-10V", "ANALOG") ||
            ContainsToken(text, "AI") || ContainsToken(text, "AO")) return ElectricalLayer.Analog;
        if (ContainsAny(text, "DIGITAL", "PNP", "NPN") ||
            ContainsToken(text, "DI") || ContainsToken(text, "DO")) return ElectricalLayer.Digital;
        if (ContainsAny(text, "SHIELD", "CHASSIS", "SIGNAL GROUND") ||
            ContainsToken(text, "PE") || ContainsToken(text, "FE") || ContainsToken(text, "SG")) return ElectricalLayer.Grounding;
        if (ContainsAny(text, "+24V", "24V", "+54V", "54V", "0V", "V+", "V-", "L+", "L-", "POWER", "SUPPLY")) return ElectricalLayer.Power;
        return ElectricalLayer.Unknown;
    }

    private static GroundReferenceType DetermineGroundReference(string raw)
    {
        var text = raw.ToUpperInvariant();
        if (ContainsToken(text, "PE")) return GroundReferenceType.ProtectiveEarth;
        if (ContainsToken(text, "FE")) return GroundReferenceType.FunctionalEarth;
        if (text.Contains("CHASSIS", StringComparison.Ordinal)) return GroundReferenceType.Chassis;
        if (text.Contains("SHIELD", StringComparison.Ordinal)) return GroundReferenceType.Shield;
        if (ContainsToken(text, "SG") || text.Contains("SIGNAL GROUND", StringComparison.Ordinal)) return GroundReferenceType.SignalGround;
        if (ContainsToken(text, "0V")) return GroundReferenceType.PowerReturn;
        if (ContainsToken(text, "GND")) return GroundReferenceType.Unknown;
        return GroundReferenceType.None;
    }

    private static PinStatus DeterminePinStatus(string? explicitStatus, string? function)
    {
        var status = explicitStatus?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status is "USED" or "NORMAL") return PinStatus.Normal;
            if (status is "UNUSED" or "OPEN") return PinStatus.Unused;
            if (status is "NC" or "N.C." or "NOT CONNECTED") return PinStatus.Nc;
            if (status.Contains("RESERVED", StringComparison.Ordinal)) return PinStatus.Reserved;
            if (status.Contains("OPTION", StringComparison.Ordinal)) return PinStatus.Optional;
            if (status is "UNKNOWN" or "NOTAPPLICABLE" or "NOT APPLICABLE") return PinStatus.Unknown;
        }

        var text = function?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text)) return PinStatus.Unknown;
        if (text is "NC" or "N.C." or "NOT CONNECTED") return PinStatus.Nc;
        if (text.Contains("UNUSED", StringComparison.Ordinal) || text.Contains("OPEN", StringComparison.Ordinal)) return PinStatus.Unused;
        if (text.Contains("RESERVED", StringComparison.Ordinal)) return PinStatus.Reserved;
        if (text.Contains("OPTION", StringComparison.Ordinal)) return PinStatus.Optional;
        return PinStatus.Normal;
    }

    private static ConnectorGender ParseConnectorGender(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ConnectorGender.Unknown;
        return value.Trim().ToUpperInvariant() switch
        {
            "MALE" => ConnectorGender.Male,
            "FEMALE" => ConnectorGender.Female,
            "GENDERLESS" => ConnectorGender.Genderless,
            _ => ConnectorGender.Unknown
        };
    }

    private static string? NormalizeProtocol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        if (normalized.Contains("RS485", StringComparison.Ordinal)) return "RS485";
        if (normalized.Contains("ETHERCAT", StringComparison.Ordinal)) return "EtherCAT";
        if (normalized.Contains("ETHERNET", StringComparison.Ordinal)) return "Ethernet";
        if (normalized.Contains("IOLINK", StringComparison.Ordinal)) return "IO-Link";
        if (normalized.Contains("PROFINET", StringComparison.Ordinal)) return "PROFINET";
        if (normalized.Contains("MODBUS", StringComparison.Ordinal)) return "Modbus";
        return null;
    }

    private static bool ContainsAny(string source, params string[] values) => values.Any(value => source.Contains(value, StringComparison.Ordinal));

    private static bool ContainsToken(string source, string token)
    {
        var separators = new[] { ' ', '/', '\\', '-', '_', '+', ':', ';', ',', '(', ')', '[', ']' };
        return source.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase));
    }

    private static string EncodeStableIdSegment(string value)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            // Encode punctuation instead of deleting it. This keeps +, -, /, :, and other engineering
            // identifiers deterministic and collision-free even for legacy rows without a central PinID.
            builder.Append("_u")
                .Append(((int)character).ToString("x4"))
                .Append('_');
        }
        return builder.Length == 0 ? "empty" : builder.ToString();
    }

    private static string Sanitize(string value)
    {
        var chars = value.Trim().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
        return new string(chars).Trim('-').ToLowerInvariant();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
