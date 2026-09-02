using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Topology;

public sealed record CommonConnectorOption(string DefinitionId, string DisplayName)
{
    public string Display => DisplayName;
}

public static class CommonConnectorCatalog
{
    public const string ShieldedCat6Rj45FemaleCouplerId = "common:cat6-rj45-female-female-shielded-8c-coupler";
    public const string Rj45Male8PinCableEndId = "common:cable-end:rj45-male-8p8c";
    public const string Rj45Female8PinCableEndId = "common:cable-end:rj45-female-8p8c";
    public const string M12MaleACode4PinCableEndId = "common:cable-end:m12-male-a-4pin";
    public const string M12FemaleACode4PinCableEndId = "common:cable-end:m12-female-a-4pin";

    public static IReadOnlyList<CommonConnectorOption> Options { get; } =
    [
        new(Rj45Male8PinCableEndId, "自製線端 RJ45 公頭 8P8C（Pin 可展開）"),
        new(Rj45Female8PinCableEndId, "自製線端 RJ45 母頭 8P8C（Pin 可展開）"),
        new(M12MaleACode4PinCableEndId, "自製線端 M12 公頭 A-code 4Pin（舊版雙面接頭）"),
        new(M12FemaleACode4PinCableEndId, "自製線端 M12 母頭 A-code 4Pin（舊版雙面接頭）"),
        new(ShieldedCat6Rj45FemaleCouplerId, "金屬殼 Cat.6 RJ45 雙母頭 8C 遮蔽轉接座（串接延長用）")
    ];

    public static ComponentInstance Create(string definitionId, string referenceDesignator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceDesignator);

        if (string.Equals(definitionId, Rj45Male8PinCableEndId, StringComparison.OrdinalIgnoreCase))
            return BuildRj45FieldCableEnd(definitionId, referenceDesignator, ConnectorGender.Male);
        if (string.Equals(definitionId, Rj45Female8PinCableEndId, StringComparison.OrdinalIgnoreCase))
            return BuildRj45FieldCableEnd(definitionId, referenceDesignator, ConnectorGender.Female);
        if (string.Equals(definitionId, M12MaleACode4PinCableEndId, StringComparison.OrdinalIgnoreCase))
            return BuildM12FieldCableEnd(definitionId, referenceDesignator, ConnectorGender.Male);
        if (string.Equals(definitionId, M12FemaleACode4PinCableEndId, StringComparison.OrdinalIgnoreCase))
            return BuildM12FieldCableEnd(definitionId, referenceDesignator, ConnectorGender.Female);
        if (!string.Equals(definitionId, ShieldedCat6Rj45FemaleCouplerId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unknown common connector '{definitionId}'.");

        var componentId = $"cmp-common-{Guid.NewGuid():N}";
        return new ComponentInstance
        {
            ComponentInstanceId = componentId,
            ComponentDefinitionId = ShieldedCat6Rj45FemaleCouplerId,
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = referenceDesignator.Trim(),
            ReferenceSource = ReferenceSource.Manual,
            ReferenceLocked = true,
            DisplayName = "Cat.6 RJ45 female-to-female shielded 8C coupler",
            EquipmentTag = "金屬殼 Cat.6 RJ45 雙母頭 8C 遮蔽轉接座",
            ResponsibilityScope = ResponsibilityScope.InScope,
            Ports =
            {
                BuildRj45Port(componentId, "RJ45-A", "ROLE:Input"),
                BuildRj45Port(componentId, "RJ45-B", "ROLE:Output")
            }
        };
    }

    /// <summary>
    /// Upgrades the short-lived one-port M12 palette shape without breaking saved endpoint IDs.
    /// Existing Pin objects move to the loose-wire face, so every old wire still resolves; the old
    /// Port ID remains the mating face, so an existing DirectMating record also remains valid.
    /// </summary>
    public static int UpgradeLegacyM12CableEnds(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var upgraded = 0;
        foreach (var component in project.Components.Where(component =>
                     component.Ports.Count == 1 &&
                     (string.Equals(component.ComponentDefinitionId, M12MaleACode4PinCableEndId, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(component.ComponentDefinitionId, M12FemaleACode4PinCableEndId, StringComparison.OrdinalIgnoreCase))))
        {
            var mating = component.Ports[0];
            if (mating.Connector is null || !string.Equals(mating.Connector.Family, "M12", StringComparison.OrdinalIgnoreCase))
                continue;

            var isFemale = mating.Connector.Gender == ConnectorGender.Female;
            mating.Name = isFemale ? "M12-F" : "M12-M";
            mating.Connector.CompatibilityClass = "M12-A-4";
            mating.Capabilities.RemoveAll(capability => capability.StartsWith("ROLE:", StringComparison.OrdinalIgnoreCase));
            mating.Capabilities.Add(isFemale ? "ROLE:Mating Output" : "ROLE:Mating Input");

            var wire = new ComponentPort
            {
                PortId = $"{component.ComponentInstanceId}:PORT:{(isFemale ? "WIRE-A" : "WIRE-B")}",
                Name = isFemale ? "WIRE-A" : "WIRE-B",
                MaxConnections = 1
            };
            wire.Capabilities.Add(isFemale ? "ROLE:Loose Wire Input" : "ROLE:Loose Wire Output");
            wire.Capabilities.Add("ALLOW_MANUAL_BRANCHING");
            foreach (var pin in mating.Pins.ToArray())
                wire.Pins.Add(pin);
            mating.Pins.Clear();

            component.Ports.Clear();
            if (isFemale)
            {
                component.Ports.Add(wire);
                component.Ports.Add(mating);
            }
            else
            {
                component.Ports.Add(mating);
                component.Ports.Add(wire);
            }
            upgraded++;
        }
        return upgraded;
    }

    public static int UpgradeLegacyRj45CableEnds(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var upgraded = 0;
        foreach (var component in project.Components.Where(component =>
                     component.Ports.Count == 1 &&
                     (string.Equals(component.ComponentDefinitionId, Rj45Male8PinCableEndId, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(component.ComponentDefinitionId, Rj45Female8PinCableEndId, StringComparison.OrdinalIgnoreCase))))
        {
            var mating = component.Ports[0];
            if (mating.Connector is null || !string.Equals(mating.Connector.Family, "RJ45", StringComparison.OrdinalIgnoreCase))
                continue;

            var isFemale = mating.Connector.Gender == ConnectorGender.Female;
            mating.Name = isFemale ? "RJ45-F" : "RJ45-M";
            mating.Connector.CompatibilityClass = "RJ45-8P8C";
            mating.Capabilities.RemoveAll(capability => capability.StartsWith("ROLE:", StringComparison.OrdinalIgnoreCase));
            mating.Capabilities.Add("ROLE:Mating Output");

            var cable = BuildExpandableCablePort(component.ComponentInstanceId, 8);
            var existingPins = mating.Pins.ToArray();
            if (existingPins.Length > 0)
            {
                cable.Pins.Clear();
                foreach (var pin in existingPins) cable.Pins.Add(pin);
            }
            mating.Pins.Clear();
            component.Ports.Clear();
            component.Ports.Add(cable);
            component.Ports.Add(mating);
            upgraded++;
        }
        return upgraded;
    }

    /// <summary>
    /// Removes an obsolete, unplaced adapter cable that still occupies a visible palette M12 face.
    /// The operation is deliberately strict: both legacy cable ends must be unplaced, and every
    /// connection involving them must belong to that cable except the one mate being replaced.
    /// </summary>
    public static IReadOnlyList<string> RemoveSupersededLegacyMate(
        ElectricalProject project,
        string visiblePortId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(visiblePortId);

        var directMate = project.Connections.SingleOrDefault(connection =>
            connection.Kind == ConnectionKind.DirectMating &&
            (string.Equals(connection.FromEndpointId, visiblePortId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(connection.ToEndpointId, visiblePortId, StringComparison.OrdinalIgnoreCase)));
        if (directMate is null) return [];

        var legacyPortId = string.Equals(directMate.FromEndpointId, visiblePortId, StringComparison.OrdinalIgnoreCase)
            ? directMate.ToEndpointId
            : directMate.FromEndpointId;
        var legacyEnd = project.Components.FirstOrDefault(component => component.Ports.Any(port =>
            string.Equals(port.PortId, legacyPortId, StringComparison.OrdinalIgnoreCase)));
        if (!IsUnplacedLegacyEnd(legacyEnd)) return [];

        var legacyConnectorIds = legacyEnd!.Ports.Where(port => port.Connector is not null)
            .Select(port => port.Connector!.ConnectorId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assembly = project.CableAssemblies.FirstOrDefault(candidate =>
            legacyConnectorIds.Contains(candidate.EndAConnectorId ?? string.Empty) ||
            legacyConnectorIds.Contains(candidate.EndBConnectorId ?? string.Empty));
        if (assembly is null) return [];

        var assemblyConnectorIds = new[] { assembly.EndAConnectorId, assembly.EndBConnectorId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyEnds = project.Components.Where(component => component.Ports.Any(port =>
                port.Connector is not null && assemblyConnectorIds.Contains(port.Connector.ConnectorId)))
            .ToArray();
        if (legacyEnds.Length != 2 || legacyEnds.Any(component => !IsUnplacedLegacyEnd(component))) return [];

        var cableIds = assembly.Members.Select(member => member.CableInstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyEndpointIds = legacyEnds.SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins.Select(pin => pin.PinId).Append(port.PortId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var touching = project.Connections.Where(connection =>
                legacyEndpointIds.Contains(connection.FromEndpointId) ||
                legacyEndpointIds.Contains(connection.ToEndpointId))
            .ToArray();
        if (touching.Any(connection =>
                !string.Equals(connection.ConnectionId, directMate.ConnectionId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(connection.CableInstanceId) || !cableIds.Contains(connection.CableInstanceId))))
            return [];

        var removedConnectionIds = touching.Select(connection => connection.ConnectionId).ToArray();
        foreach (var connection in touching) project.Connections.Remove(connection);
        foreach (var cable in project.Cables.Where(cable => cableIds.Contains(cable.CableInstanceId)).ToArray())
            project.Cables.Remove(cable);
        project.CableAssemblies.Remove(assembly);
        foreach (var component in legacyEnds) project.Components.Remove(component);
        return removedConnectionIds;

        bool IsUnplacedLegacyEnd(ComponentInstance? component) =>
            component is not null &&
            component.ComponentDefinitionId.StartsWith("inline-mated-adapter:", StringComparison.OrdinalIgnoreCase) &&
            !project.TopologyPlacements.Any(placement =>
                string.Equals(placement.ObjectId, component.ComponentInstanceId, StringComparison.OrdinalIgnoreCase));
    }

    private static ComponentInstance BuildRj45FieldCableEnd(
        string definitionId,
        string referenceDesignator,
        ConnectorGender gender)
    {
        var componentId = $"cmp-common-{Guid.NewGuid():N}";
        var isFemale = gender == ConnectorGender.Female;
        var mating = new ComponentPort
        {
            PortId = $"{componentId}:PORT:FACE",
            Name = isFemale ? "RJ45-F" : "RJ45-M",
            MaxConnections = 1,
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{componentId}:CONN:FACE",
                Family = "RJ45",
                PinCount = 8,
                Gender = gender,
                MountType = ConnectorMountType.Cable,
                CompatibilityClass = "RJ45-8P8C"
            }
        };
        mating.Capabilities.Add("ROLE:Mating Output");
        var cable = BuildExpandableCablePort(componentId, 8);

        return new ComponentInstance
        {
            ComponentInstanceId = componentId,
            ComponentDefinitionId = definitionId,
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = referenceDesignator.Trim(),
            ReferenceSource = ReferenceSource.Manual,
            ReferenceLocked = true,
            DisplayName = isFemale ? "RJ45 female 8P8C cable end" : "RJ45 male 8P8C cable end",
            EquipmentTag = isFemale ? "RJ45 母頭 8P8C" : "RJ45 公頭 8P8C",
            ResponsibilityScope = ResponsibilityScope.InScope,
            Ports = { cable, mating }
        };
    }

    private static ComponentPort BuildExpandableCablePort(string componentId, int pinCount)
    {
        var port = new ComponentPort
        {
            PortId = $"{componentId}:PORT:CABLE",
            Name = "CABLE",
            MaxConnections = 1
        };
        port.Capabilities.Add("ROLE:Cable Input");
        port.Capabilities.Add("TOPOLOGY_ENDPOINT_MODE:CONNECTOR");
        port.Capabilities.Add("ALLOW_MANUAL_BRANCHING");
        for (var pinNumber = 1; pinNumber <= pinCount; pinNumber++)
        {
            port.Pins.Add(new ComponentPin
            {
                PinId = $"{port.PortId}:PIN:{pinNumber}",
                PinNumber = pinNumber.ToString(),
                PinName = $"Pin {pinNumber}",
                Layer = ElectricalLayer.Unknown,
                Status = PinStatus.Unknown
            });
        }
        return port;
    }

    private static ComponentInstance BuildM12FieldCableEnd(
        string definitionId,
        string referenceDesignator,
        ConnectorGender gender)
    {
        var componentId = $"cmp-common-{Guid.NewGuid():N}";
        var isFemale = gender == ConnectorGender.Female;
        var matingName = isFemale ? "M12-F" : "M12-M";
        var wireName = isFemale ? "WIRE-A" : "WIRE-B";
        var mating = new ComponentPort
        {
            PortId = $"{componentId}:PORT:{matingName}",
            Name = matingName,
            MaxConnections = 1,
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{componentId}:CONN:{matingName}",
                Family = "M12",
                Coding = "A",
                PinCount = 4,
                Gender = gender,
                MountType = ConnectorMountType.Cable,
                CompatibilityClass = "M12-A-4"
            }
        };
        mating.Capabilities.Add(isFemale ? "ROLE:Mating Output" : "ROLE:Mating Input");
        AddPins(mating, 4);

        var wire = new ComponentPort
        {
            PortId = $"{componentId}:PORT:{wireName}",
            Name = wireName,
            MaxConnections = 1
        };
        wire.Capabilities.Add(isFemale ? "ROLE:Loose Wire Input" : "ROLE:Loose Wire Output");
        wire.Capabilities.Add("ALLOW_MANUAL_BRANCHING");
        AddPins(wire, 4);

        var component = new ComponentInstance
        {
            ComponentInstanceId = componentId,
            ComponentDefinitionId = definitionId,
            TypeKey = "INLINE_CONNECTOR",
            ReferenceDesignator = referenceDesignator.Trim(),
            ReferenceSource = ReferenceSource.Manual,
            ReferenceLocked = true,
            DisplayName = isFemale
                ? "M12 female A-code 4-pin field cable end"
                : "M12 male A-code 4-pin field cable end",
            EquipmentTag = isFemale
                ? "M12 母頭 A-code 4Pin"
                : "M12 公頭 A-code 4Pin",
            ResponsibilityScope = ResponsibilityScope.InScope
        };

        // Mirror the old field-adapter presentation: loose-wire Pins face outward while the mating
        // socket/plug faces its complementary connector. The order also keeps old drawing behavior.
        if (isFemale)
        {
            component.Ports.Add(wire);
            component.Ports.Add(mating);
        }
        else
        {
            component.Ports.Add(mating);
            component.Ports.Add(wire);
        }
        return component;
    }

    private static void AddPins(ComponentPort port, int pinCount)
    {
        for (var pinNumber = 1; pinNumber <= pinCount; pinNumber++)
        {
            port.Pins.Add(new ComponentPin
            {
                PinId = $"{port.PortId}:PIN:{pinNumber}",
                PinNumber = pinNumber.ToString(),
                PinName = $"Pin {pinNumber}",
                Layer = ElectricalLayer.Unknown,
                Status = PinStatus.Unknown
            });
        }
    }

    private static ComponentPort BuildRj45Port(string componentId, string name, string role)
    {
        var port = new ComponentPort
        {
            PortId = $"{componentId}:PORT:{name}",
            Name = name,
            Protocol = "Ethernet",
            MaxConnections = 1,
            Connector = new ConnectorDefinition
            {
                ConnectorId = $"{componentId}:CONN:{name}",
                Family = "RJ45",
                SeriesOrSize = "Cat.6 8C",
                PinCount = 8,
                Gender = ConnectorGender.Female,
                MountType = ConnectorMountType.Device,
                Shielded = true,
                CompatibilityClass = "RJ45-CAT6-8C-SHIELDED"
            }
        };
        port.Capabilities.Add(role);
        port.Capabilities.Add("SHIELDED");
        for (var pinNumber = 1; pinNumber <= 8; pinNumber++)
        {
            port.Pins.Add(new ComponentPin
            {
                PinId = $"{port.PortId}:PIN:{pinNumber}",
                PinNumber = pinNumber.ToString(),
                Protocol = "Ethernet",
                Layer = ElectricalLayer.Communication,
                Status = PinStatus.Unknown
            });
        }
        return port;
    }
}
