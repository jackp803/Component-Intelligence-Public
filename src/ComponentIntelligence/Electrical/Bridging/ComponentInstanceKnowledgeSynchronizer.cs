using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using DomainPort = ComponentIntelligence.Electrical.Domain.ComponentPort;
using DomainPin = ComponentIntelligence.Electrical.Domain.ComponentPin;

namespace ComponentIntelligence.Electrical.Bridging;

/// <summary>
/// Conservatively enriches an existing project instance from Component IR without replacing
/// project-specific identity, placement or already-used endpoint IDs. Existing project facts win;
/// newly discovered knowledge only fills missing fields or adds new ports/pins.
/// </summary>
public sealed class ComponentInstanceKnowledgeSynchronizer
{
    private static readonly string[] ArchiveCapabilityPrefixes =
    [
        "SOURCE_PORT_ID:",
        "ROLE:",
        "TOPOLOGY_ENDPOINT_MODE:",
        "DIRECTION:",
        "VOLTAGE_DOMAIN:",
        "ALLOWED:"
    ];

    private readonly ComponentProjectBridge _bridge = new();

    public void Apply(ComponentInstance target, ComponentIR source, bool overwriteExistingKnowledge = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        var lineage = new ComponentSourceIdentityRestorer().Restore(target, source);
        var powerIdentityValid = string.Equals(target.ComponentDefinitionId, source.Identity.ComponentId, StringComparison.Ordinal)
            && lineage.Status != SourceIdentityStatus.CONFLICT;

        var mapped = _bridge.CreateInstance(
            source,
            target.ComponentInstanceId,
            target.ReferenceDesignator,
            target.EquipmentTag,
            overwriteExistingKnowledge ? null : target.TypeKey);

        if (overwriteExistingKnowledge)
        {
            target.TypeKey = mapped.TypeKey;
            target.DisplayName = mapped.DisplayName;
        }
        else if (string.IsNullOrWhiteSpace(target.DisplayName))
        {
            target.DisplayName = mapped.DisplayName;
        }
        FillPhysicalFootprint(target, source, overwriteExistingKnowledge);

        foreach (var incomingPort in mapped.Ports)
        {
            var existingPort = FindMatchingPort(target, incomingPort);
            if (existingPort is null)
            {
                target.Ports.Add(incomingPort);
                continue;
            }

            if (overwriteExistingKnowledge)
            {
                existingPort.Name = incomingPort.Name;
                existingPort.Protocol = incomingPort.Protocol;
                existingPort.MaxConnections = incomingPort.MaxConnections;
                existingPort.PhysicalLocation = incomingPort.PhysicalLocation;
            }
            else
            {
                existingPort.Protocol ??= incomingPort.Protocol;
                existingPort.MaxConnections ??= incomingPort.MaxConnections;
                existingPort.PhysicalLocation ??= incomingPort.PhysicalLocation;
            }
            if (overwriteExistingKnowledge)
            {
                existingPort.Capabilities.RemoveAll(capability =>
                    ArchiveCapabilityPrefixes.Any(prefix =>
                        capability.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var capability in incomingPort.Capabilities)
                if (!existingPort.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
                    existingPort.Capabilities.Add(capability);

            if (existingPort.Connector is null)
            {
                existingPort.Connector = incomingPort.Connector;
            }
            else if (incomingPort.Connector is not null)
            {
                FillConnector(existingPort.Connector, incomingPort.Connector, overwriteExistingKnowledge);
            }

            foreach (var incomingPin in incomingPort.Pins)
            {
                var typedMatches = existingPort.Pins.Where(pin => !string.IsNullOrWhiteSpace(incomingPin.SourcePinId)
                    && string.Equals(pin.SourcePinId, incomingPin.SourcePinId, StringComparison.Ordinal)).ToArray();
                var existingPin = (typedMatches.Length == 1 ? typedMatches[0] : null) ?? existingPort.Pins.FirstOrDefault(pin =>
                    string.Equals(pin.PinId, incomingPin.PinId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pin.PinNumber, incomingPin.PinNumber, StringComparison.OrdinalIgnoreCase));
                if (existingPin is null)
                {
                    existingPort.Pins.Add(incomingPin);
                    continue;
                }
                var powerMatch = powerIdentityValid && typedMatches.Length == 1
                    && !string.IsNullOrWhiteSpace(existingPort.SourcePortId)
                    && string.Equals(existingPort.SourcePortId, incomingPort.SourcePortId, StringComparison.Ordinal);
                FillPin(existingPin, incomingPin, overwriteExistingKnowledge, powerMatch);
            }
        }
    }

    private static void FillPhysicalFootprint(ComponentInstance target, ComponentIR source, bool overwriteExistingKnowledge)
    {
        var incoming = ComponentPhysicalKnowledgeMapper.TryCreateFootprint(source);
        if (incoming is null) return;

        if (target.Footprint is null)
        {
            target.Footprint = incoming;
            return;
        }

        if (overwriteExistingKnowledge && !target.FootprintOverride)
        {
            target.Footprint.WidthMm = incoming.WidthMm;
            target.Footprint.HeightMm = incoming.HeightMm;
            target.Footprint.DepthMm = incoming.DepthMm;
            target.Footprint.MountingType = incoming.MountingType;
            return;
        }

        if (target.Footprint.WidthMm <= 0) target.Footprint.WidthMm = incoming.WidthMm;
        if (target.Footprint.HeightMm <= 0) target.Footprint.HeightMm = incoming.HeightMm;
        target.Footprint.DepthMm ??= incoming.DepthMm;
        if (target.Footprint.MountingType == MountingType.Unknown)
            target.Footprint.MountingType = incoming.MountingType;
    }

    private static DomainPort? FindMatchingPort(ComponentInstance target, DomainPort incoming)
    {
        var typed = target.Ports.Where(port => !string.IsNullOrWhiteSpace(incoming.SourcePortId)
            && string.Equals(port.SourcePortId, incoming.SourcePortId, StringComparison.Ordinal)).ToArray();
        // Legacy matching may still enrich display knowledge, but cannot authorize power updates.
        return (typed.Length == 1 ? typed[0] : null)
            ?? target.Ports.FirstOrDefault(port => string.Equals(port.PortId, incoming.PortId, StringComparison.OrdinalIgnoreCase))
            ?? target.Ports.FirstOrDefault(port => string.Equals(port.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static void FillConnector(ConnectorDefinition target, ConnectorDefinition incoming, bool overwriteExistingKnowledge)
    {
        if (overwriteExistingKnowledge)
        {
            target.Family = incoming.Family;
            target.SeriesOrSize = incoming.SeriesOrSize;
            target.PinCount = incoming.PinCount;
            target.Coding = incoming.Coding;
            target.Gender = incoming.Gender;
            target.MountType = incoming.MountType;
            target.Orientation = incoming.Orientation;
            target.Shielded = incoming.Shielded;
            target.CompatibilityClass = incoming.CompatibilityClass;
            target.MinTerminationAreaMm2 = incoming.MinTerminationAreaMm2;
            target.MaxTerminationAreaMm2 = incoming.MaxTerminationAreaMm2;
            return;
        }
        target.SeriesOrSize ??= incoming.SeriesOrSize;
        target.PinCount ??= incoming.PinCount;
        target.Coding ??= incoming.Coding;
        if (target.Gender == ConnectorGender.Unknown) target.Gender = incoming.Gender;
        if (target.MountType == ConnectorMountType.Unknown) target.MountType = incoming.MountType;
        if (target.Orientation == ConnectorOrientation.Unknown) target.Orientation = incoming.Orientation;
        target.Shielded ??= incoming.Shielded;
        target.CompatibilityClass ??= incoming.CompatibilityClass;
        target.MinTerminationAreaMm2 ??= incoming.MinTerminationAreaMm2;
        target.MaxTerminationAreaMm2 ??= incoming.MaxTerminationAreaMm2;
    }

    private static void FillPin(DomainPin target, DomainPin incoming, bool overwriteExistingKnowledge, bool powerIdentityConfirmed)
    {
        if (overwriteExistingKnowledge)
        {
            target.PinNumber = incoming.PinNumber;
            target.PinName = incoming.PinName;
            target.Function = incoming.Function;
            target.Protocol = incoming.Protocol;
            target.SignalStandardRaw = incoming.SignalStandardRaw;
            target.Layer = incoming.Layer;
            target.Status = incoming.Status;
            target.IsRequired = incoming.IsRequired;
            target.GroundReferenceType = incoming.GroundReferenceType;
            target.IsolationDomainId = incoming.IsolationDomainId;
            target.DifferentialRole = incoming.DifferentialRole;
            if (powerIdentityConfirmed) target.Power = incoming.Power;
            target.Digital = incoming.Digital;
            target.Analog = incoming.Analog;
            return;
        }
        target.PinName ??= incoming.PinName;
        target.Function ??= incoming.Function;
        target.Protocol ??= incoming.Protocol;
        target.SignalStandardRaw ??= incoming.SignalStandardRaw;
        if (target.Layer == ElectricalLayer.Unknown) target.Layer = incoming.Layer;
        if (target.Status == PinStatus.Unknown) target.Status = incoming.Status;
        if (target.GroundReferenceType is GroundReferenceType.None or GroundReferenceType.Unknown)
            target.GroundReferenceType = incoming.GroundReferenceType;
        target.IsolationDomainId ??= incoming.IsolationDomainId;
        if (target.DifferentialRole == DifferentialRole.None) target.DifferentialRole = incoming.DifferentialRole;
        if (powerIdentityConfirmed) FillPower(target, incoming.Power);
        target.Digital ??= incoming.Digital;
        target.Analog ??= incoming.Analog;
    }

    private static void FillPower(DomainPin target, PowerCapability? incoming)
    {
        if (incoming is null) return;
        if (target.Power is null)
        {
            target.Power = incoming;
            return;
        }

        var current = target.Power;
        target.Power = current with
        {
            Role = current.Role == PowerRole.Unknown ? incoming.Role : current.Role,
            Polarity = current.Polarity == Polarity.Unknown ? incoming.Polarity : current.Polarity,
            Voltage = current.Voltage ?? incoming.Voltage,
            RequiredCurrentAmp = current.RequiredCurrentAmp ?? incoming.RequiredCurrentAmp,
            MaxCurrentAmp = current.MaxCurrentAmp ?? incoming.MaxCurrentAmp
        };
    }
}
