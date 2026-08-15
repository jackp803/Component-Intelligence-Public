using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Cables;

public sealed record ConnectionEngineeringAnalysis
{
    public required string ConnectionId { get; init; }
    public ElectricalLayer Layer { get; init; } = ElectricalLayer.Unknown;
    public string? Protocol { get; init; }
    public string? FromEndpoint { get; init; }
    public string? ToEndpoint { get; init; }
    public string? ConnectorA { get; init; }
    public string? ConnectorB { get; init; }
    public VoltageType VoltageType { get; init; } = VoltageType.Unknown;
    public double? NominalVoltage { get; init; }
    public double? MinVoltage { get; init; }
    public double? MaxVoltage { get; init; }
    public double? RequiredCurrentAmp { get; init; }
    public double? SourceCapacityAmp { get; init; }
    public double? PowerWatt { get; init; }
    public double? MinPowerWatt { get; init; }
    public double? MaxPowerWatt { get; init; }
    public int? RequiredCoreCount { get; init; }
    public double? SelectedConductorAreaMm2 { get; init; }
    public double? TerminationMinAreaMm2 { get; init; }
    public double? TerminationMaxAreaMm2 { get; init; }
    public double? ProvidedLengthMm { get; init; }
    public CableLengthSource LengthSource { get; init; } = CableLengthSource.Unknown;
    public RequirementLevel TwistedPair { get; init; } = RequirementLevel.Unknown;
    public RequirementLevel Shielding { get; init; } = RequirementLevel.Unknown;
    public RequirementLevel DragChain { get; init; } = RequirementLevel.Unknown;
    public IReadOnlyList<string> CommunicationStandards { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingData { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasEnoughElectricalDataForPowerCalculation =>
        RequiredCurrentAmp is not null && (NominalVoltage is not null || MinVoltage is not null || MaxVoltage is not null);
}

/// <summary>
/// Derives engineering requirements from an existing topology connection without inventing missing
/// electrical facts. Cable length is an external engineering input (normally supplied by Mechanical),
/// never a value inferred from topology/layout/cable-route drawing geometry.
/// </summary>
public sealed class ConnectionEngineeringAnalyzer
{
    public ConnectionEngineeringAnalysis Analyze(ElectricalProject project, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var connection = project.Connections.FirstOrDefault(item =>
            string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Connection '{connectionId}' does not exist.");

        var from = ResolveEndpoint(project, connection.FromEndpointId);
        var to = ResolveEndpoint(project, connection.ToEndpointId);
        var warnings = new List<string>();
        var missing = new List<string>();

        var protocol = ResolveProtocol(project, connection, from, to, warnings);
        var layer = ResolveLayer(project, connection, from, to, protocol, warnings);
        var voltage = ResolveVoltage(from, to, warnings);
        var current = ResolveRequiredCurrent(from, to, warnings);
        var sourceCapacity = ResolveSourceCapacity(from, to);
        var termination = ResolveTerminationRange(from, to, warnings);
        var coreCount = ResolveCoreCount(from, to, warnings);
        var length = ResolveProvidedLength(project, connection);
        var twistedPair = ResolveTwistedPair(protocol, from, to);
        var shielding = ResolveRequirementFromCapabilities(from, to, "SHIELD_REQUIRED", "SHIELD_PREFERRED");
        var dragChain = ResolveRequirementFromCapabilities(from, to, "DRAG_CHAIN_REQUIRED", "DRAG_CHAIN_PREFERRED");

        if (from is null) missing.Add("A 端 Endpoint（端點）不存在或尚未建模");
        if (to is null) missing.Add("B 端 Endpoint（端點）不存在或尚未建模");
        if (layer == ElectricalLayer.Unknown) missing.Add("Electrical Layer（電氣層）未知");
        if (layer == ElectricalLayer.Communication && string.IsNullOrWhiteSpace(protocol))
            missing.Add("Protocol（通訊協定）未知");

        AddConnectorMissing(from, "A", missing);
        AddConnectorMissing(to, "B", missing);

        if (layer == ElectricalLayer.Power)
        {
            if (voltage is null) missing.Add("Voltage（電壓）未知，無法做功率／壓降計算");
            if (current is null) missing.Add("Load Current（負載電流）未知，不能安全決定線徑");
        }

        if (coreCount is null && (connection.Kind == ConnectionKind.Cable || from?.Port is not null || to?.Port is not null))
            missing.Add("Required Core Count（需要芯數）未知／Pin Mapping 尚未完整");

        if (length.Value is null)
            missing.Add("Cable Length（線長）尚未由 Mechanical / User / Import（機構／人工／匯入）提供；Layout / Cable Route 不會代替工程線長");

        if (connection.ConductorAreaMm2 is null && layer is ElectricalLayer.Power or ElectricalLayer.Digital or ElectricalLayer.Analog)
            missing.Add("Conductor Area（導體截面積）尚未選定；需依負載、線長、壓降、安裝條件與適用標準決定");

        if (connection.ConductorAreaMm2 is double selectedArea)
        {
            if (termination.Min is double minArea && selectedArea + 1e-9 < minArea)
                warnings.Add($"已選線徑 {selectedArea:0.###} mm² 小於端點允許最小值 {minArea:0.###} mm²。");
            if (termination.Max is double maxArea && selectedArea - 1e-9 > maxArea)
                warnings.Add($"已選線徑 {selectedArea:0.###} mm² 大於端點允許最大值 {maxArea:0.###} mm²。");
        }

        if (current is double loadCurrent && sourceCapacity is double capacity && loadCurrent - 1e-9 > capacity)
            warnings.Add($"需求電流 {loadCurrent:0.###} A 超過來源已知最大能力 {capacity:0.###} A。");

        double? power = null;
        double? minPower = null;
        double? maxPower = null;
        if (current is double requiredCurrent)
        {
            if (voltage?.NominalVoltage is double nominal)
                power = nominal * requiredCurrent;
            if (voltage?.MinVoltage is double minVoltage)
                minPower = minVoltage * requiredCurrent;
            if (voltage?.MaxVoltage is double maxVoltage)
                maxPower = maxVoltage * requiredCurrent;
        }

        var standards = string.IsNullOrWhiteSpace(protocol) ? Array.Empty<string>() : new[] { protocol };
        return new ConnectionEngineeringAnalysis
        {
            ConnectionId = connection.ConnectionId,
            Layer = layer,
            Protocol = protocol,
            FromEndpoint = DescribeEndpoint(from, connection.FromEndpointId),
            ToEndpoint = DescribeEndpoint(to, connection.ToEndpointId),
            ConnectorA = DescribeConnector(from?.Port?.Connector),
            ConnectorB = DescribeConnector(to?.Port?.Connector),
            VoltageType = voltage?.Type ?? VoltageType.Unknown,
            NominalVoltage = voltage?.NominalVoltage,
            MinVoltage = voltage?.MinVoltage,
            MaxVoltage = voltage?.MaxVoltage,
            RequiredCurrentAmp = current,
            SourceCapacityAmp = sourceCapacity,
            PowerWatt = power,
            MinPowerWatt = minPower,
            MaxPowerWatt = maxPower,
            RequiredCoreCount = coreCount,
            SelectedConductorAreaMm2 = connection.ConductorAreaMm2,
            TerminationMinAreaMm2 = termination.Min,
            TerminationMaxAreaMm2 = termination.Max,
            ProvidedLengthMm = length.Value,
            LengthSource = length.Source,
            TwistedPair = twistedPair,
            Shielding = shielding,
            DragChain = dragChain,
            CommunicationStandards = standards,
            MissingData = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static EndpointDescriptor? ResolveEndpoint(ElectricalProject project, string endpointId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return new EndpointDescriptor(component, port, null, null, null);

            var pin = port.Pins.FirstOrDefault(item => string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null)
                return new EndpointDescriptor(component, port, pin, null, null);
        }

        foreach (var block in project.TerminalBlocks)
        foreach (var position in block.Positions)
        foreach (var level in position.Levels)
        foreach (var point in level.ConnectionPoints)
            if (string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase))
                return new EndpointDescriptor(null, null, null, block, point);

        return null;
    }

    private static string? ResolveProtocol(
        ElectricalProject project,
        ElectricalConnection connection,
        EndpointDescriptor? from,
        EndpointDescriptor? to,
        List<string> warnings)
    {
        var candidates = new List<string?>
        {
            from?.Pin?.Protocol,
            from?.Port?.Protocol,
            to?.Pin?.Protocol,
            to?.Port?.Protocol
        };

        if (!string.IsNullOrWhiteSpace(connection.NetId))
        {
            var net = project.Nets.FirstOrDefault(item => string.Equals(item.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(net?.BusId))
            {
                var bus = project.Buses.FirstOrDefault(item => string.Equals(item.BusId, net.BusId, StringComparison.OrdinalIgnoreCase));
                candidates.Add(bus?.Protocol);
            }
        }

        var distinct = candidates.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinct.Length == 1) return distinct[0];
        if (distinct.Length > 1) warnings.Add($"端點 Protocol 不一致：{string.Join(" / ", distinct)}");
        return null;
    }

    private static ElectricalLayer ResolveLayer(
        ElectricalProject project,
        ElectricalConnection connection,
        EndpointDescriptor? from,
        EndpointDescriptor? to,
        string? protocol,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(connection.NetId))
        {
            var net = project.Nets.FirstOrDefault(item => string.Equals(item.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase));
            if (net is not null && net.Layer != ElectricalLayer.Unknown) return net.Layer;
        }

        var layers = new[] { from?.Pin?.Layer, to?.Pin?.Layer }
            .Where(layer => layer is not null and not ElectricalLayer.Unknown)
            .Select(layer => layer!.Value)
            .Distinct()
            .ToArray();
        if (layers.Length == 1) return layers[0];
        if (layers.Length > 1)
        {
            warnings.Add($"端點 Electrical Layer 不一致：{string.Join(" / ", layers)}");
            return ElectricalLayer.Unknown;
        }
        if (!string.IsNullOrWhiteSpace(protocol)) return ElectricalLayer.Communication;
        return ElectricalLayer.Unknown;
    }

    private static VoltageSpecification? ResolveVoltage(EndpointDescriptor? from, EndpointDescriptor? to, List<string> warnings)
    {
        var fromVoltages = EndpointVoltages(from);
        var toVoltages = EndpointVoltages(to);
        var voltages = fromVoltages.Concat(toVoltages).ToArray();
        if (voltages.Length == 0) return null;

        var source = fromVoltages.FirstOrDefault(item => item.Role == PowerRole.Source)
                     ?? toVoltages.FirstOrDefault(item => item.Role == PowerRole.Source);
        var sink = fromVoltages.FirstOrDefault(item => item.Role == PowerRole.Input)
                   ?? toVoltages.FirstOrDefault(item => item.Role == PowerRole.Input);

        if (source is not null && sink is not null && !VoltageCompatible(source.Voltage, sink.Voltage))
            warnings.Add("Power Source / Input 的電壓範圍或 AC/DC 類型不相容。");

        if (source is not null) return source.Voltage;
        if (sink is not null) return sink.Voltage;

        var distinct = voltages.Select(item => item.Voltage).GroupBy(VoltageKey).Select(group => group.First()).ToArray();
        if (distinct.Length == 1) return distinct[0];
        warnings.Add("同一連線找到多個不同 Voltage Specification（電壓規格），需要人工確認。");
        return null;
    }

    private static IReadOnlyList<PowerVoltage> EndpointVoltages(EndpointDescriptor? endpoint)
    {
        if (endpoint is null) return Array.Empty<PowerVoltage>();
        if (endpoint.Pin?.Power?.Voltage is VoltageSpecification direct)
            return new[] { new PowerVoltage(direct, endpoint.Pin.Power.Role) };
        if (endpoint.Port is null) return Array.Empty<PowerVoltage>();

        return endpoint.Port.Pins
            .Where(pin => pin.Power?.Voltage is not null)
            .Select(pin => new PowerVoltage(pin.Power!.Voltage!, pin.Power.Role))
            .GroupBy(item => $"{VoltageKey(item.Voltage)}|{item.Role}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static double? ResolveRequiredCurrent(EndpointDescriptor? from, EndpointDescriptor? to, List<string> warnings)
    {
        var pins = EndpointPins(from).Concat(EndpointPins(to)).ToArray();
        var powerValues = pins
            .Select(pin => pin.Power?.RequiredCurrentAmp)
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (powerValues.Length == 1) return powerValues[0];
        if (powerValues.Length > 1)
        {
            warnings.Add("找到多個 Power Required Current（電源需求電流）值，無法確認是否應相加；未自動推算總負載。");
            return null;
        }

        var digitalValues = pins
            .Select(pin => pin.Digital?.RequiredInputCurrentAmp)
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (digitalValues.Length == 0) return null;
        if (digitalValues.Length == 1) return digitalValues[0];
        warnings.Add("找到多個 Digital Required Input Current（數位輸入需求電流）值，無法確認是否應相加；未自動推算總負載。");
        return null;
    }

    private static double? ResolveSourceCapacity(EndpointDescriptor? from, EndpointDescriptor? to)
    {
        var pins = EndpointPins(from).Concat(EndpointPins(to)).ToArray();
        var values = pins.Select(pin => pin.Power?.MaxCurrentAmp)
            .Concat(pins.Select(pin => pin.Digital?.MaxOutputCurrentAmp))
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Min();
    }

    private static (double? Min, double? Max) ResolveTerminationRange(EndpointDescriptor? from, EndpointDescriptor? to, List<string> warnings)
    {
        var mins = new[]
            {
                from?.Port?.Connector?.MinTerminationAreaMm2,
                to?.Port?.Connector?.MinTerminationAreaMm2,
                from?.TerminalPoint?.MinWireAreaMm2,
                to?.TerminalPoint?.MinWireAreaMm2
            }
            .Where(value => value is not null).Select(value => value!.Value).ToArray();
        var maxs = new[]
            {
                from?.Port?.Connector?.MaxTerminationAreaMm2,
                to?.Port?.Connector?.MaxTerminationAreaMm2,
                from?.TerminalPoint?.MaxWireAreaMm2,
                to?.TerminalPoint?.MaxWireAreaMm2
            }
            .Where(value => value is not null).Select(value => value!.Value).ToArray();
        double? min = mins.Length == 0 ? null : mins.Max();
        double? max = maxs.Length == 0 ? null : maxs.Min();
        if (min is double minimum && max is double maximum && minimum > maximum)
            warnings.Add($"兩端可接受的導體截面範圍沒有交集：min {minimum:0.###} mm² > max {maximum:0.###} mm²。");
        return (min, max);
    }

    private static int? ResolveCoreCount(EndpointDescriptor? from, EndpointDescriptor? to, List<string> warnings)
    {
        if (from?.Pin is not null && to?.Pin is not null) return 1;
        var counts = new[] { CountUsedPins(from?.Port), CountUsedPins(to?.Port) }
            .Where(value => value is > 0).Select(value => value!.Value).ToArray();
        if (counts.Length == 0) return null;
        if (counts.Length == 2 && counts[0] != counts[1])
            warnings.Add($"兩端目前可用 Pin 數不同：A={counts[0]} / B={counts[1]}；需確認 Pin Mapping（腳位對應）。");
        return counts.Max();
    }

    private static int? CountUsedPins(ComponentPort? port)
    {
        if (port is null || port.Pins.Count == 0) return null;
        var required = port.Pins.Count(pin => pin.IsRequired && pin.Status is not (PinStatus.Nc or PinStatus.Reserved));
        if (required > 0) return required;
        var known = port.Pins.Count(pin => pin.Status is not (PinStatus.Nc or PinStatus.Reserved) && !string.IsNullOrWhiteSpace(pin.Function));
        return known > 0 ? known : null;
    }

    private static ProvidedLength ResolveProvidedLength(ElectricalProject project, ElectricalConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.CableInstanceId))
        {
            var cable = project.Cables.FirstOrDefault(item =>
                string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
            if (cable?.ProvidedLengthMm is > 0)
                return new ProvidedLength(cable.ProvidedLengthMm, cable.LengthSource);
        }

        return connection.ProvidedLengthMm is > 0
            ? new ProvidedLength(connection.ProvidedLengthMm, connection.LengthSource)
            : new ProvidedLength(null, CableLengthSource.Unknown);
    }

    private static RequirementLevel ResolveTwistedPair(string? protocol, EndpointDescriptor? from, EndpointDescriptor? to)
    {
        if (EndpointPins(from).Concat(EndpointPins(to)).Any(pin => pin.DifferentialRole is DifferentialRole.Positive or DifferentialRole.Negative))
            return RequirementLevel.Required;
        if (string.Equals(protocol, "RS485", StringComparison.OrdinalIgnoreCase)) return RequirementLevel.Required;
        if (protocol is not null && (protocol.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) || protocol.Contains("EtherCAT", StringComparison.OrdinalIgnoreCase)))
        {
            var connectors = new[] { from?.Port?.Connector?.Family, to?.Port?.Connector?.Family }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();
            if (connectors.Any(value => IsFiberConnector(value))) return RequirementLevel.Unknown;
            if (connectors.Any(value => value.Contains("RJ45", StringComparison.OrdinalIgnoreCase) || value.Contains("M12", StringComparison.OrdinalIgnoreCase)))
                return RequirementLevel.Required;
        }
        return RequirementLevel.Unknown;
    }

    private static RequirementLevel ResolveRequirementFromCapabilities(
        EndpointDescriptor? from,
        EndpointDescriptor? to,
        string requiredToken,
        string preferredToken)
    {
        var capabilities = new[] { from?.Port, to?.Port }.Where(port => port is not null)
            .SelectMany(port => port!.Capabilities).ToArray();
        if (capabilities.Any(value => string.Equals(value, requiredToken, StringComparison.OrdinalIgnoreCase))) return RequirementLevel.Required;
        if (capabilities.Any(value => string.Equals(value, preferredToken, StringComparison.OrdinalIgnoreCase))) return RequirementLevel.Preferred;
        return RequirementLevel.Unknown;
    }

    private static IEnumerable<ComponentPin> EndpointPins(EndpointDescriptor? endpoint)
    {
        if (endpoint?.Pin is not null) return new[] { endpoint.Pin };
        return endpoint?.Port?.Pins ?? Enumerable.Empty<ComponentPin>();
    }

    private static void AddConnectorMissing(EndpointDescriptor? endpoint, string side, List<string> missing)
    {
        if (endpoint is null || endpoint.TerminalPoint is not null || endpoint.Pin is not null) return;
        if (endpoint.Port?.Connector is null || string.IsNullOrWhiteSpace(endpoint.Port.Connector.Family))
            missing.Add($"{side} 端 Connector（接頭）未知");
    }

    private static string DescribeEndpoint(EndpointDescriptor? endpoint, string fallback)
    {
        if (endpoint is null) return fallback;
        if (endpoint.Pin is not null)
            return $"{endpoint.Component?.ReferenceDesignator ?? endpoint.Component?.ComponentInstanceId}.{endpoint.Port?.Name}.Pin{endpoint.Pin.PinNumber} {endpoint.Pin.Function ?? "?"}";
        if (endpoint.Port is not null)
            return $"{endpoint.Component?.ReferenceDesignator ?? endpoint.Component?.ComponentInstanceId}.{endpoint.Port.Name}";
        if (endpoint.TerminalPoint is not null)
            return $"{endpoint.TerminalBlock?.ReferenceDesignator}.{endpoint.TerminalPoint.PhysicalSide ?? endpoint.TerminalPoint.ConnectionPointId}";
        return fallback;
    }

    private static string? DescribeConnector(ConnectorDefinition? connector)
    {
        if (connector is null) return null;
        var values = new[]
        {
            connector.Family,
            connector.SeriesOrSize,
            connector.Coding,
            connector.PinCount is null ? null : $"{connector.PinCount}-pin",
            connector.Gender == ConnectorGender.Unknown ? null : connector.Gender.ToString()
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join(' ', values);
    }

    private static bool VoltageCompatible(VoltageSpecification left, VoltageSpecification right)
    {
        if (left.Type != VoltageType.Unknown && right.Type != VoltageType.Unknown && left.Type != right.Type) return false;
        var leftMin = left.MinVoltage ?? left.NominalVoltage;
        var leftMax = left.MaxVoltage ?? left.NominalVoltage;
        var rightMin = right.MinVoltage ?? right.NominalVoltage;
        var rightMax = right.MaxVoltage ?? right.NominalVoltage;
        if (leftMin is null || leftMax is null || rightMin is null || rightMax is null) return true;
        return leftMax.Value + 1e-9 >= rightMin.Value && rightMax.Value + 1e-9 >= leftMin.Value;
    }

    private static string VoltageKey(VoltageSpecification voltage) =>
        $"{voltage.Type}|{voltage.NominalVoltage}|{voltage.MinVoltage}|{voltage.MaxVoltage}";

    private static bool IsFiberConnector(string value) =>
        value.Contains("SFP", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value.Trim(), "LC", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value.Trim(), "SC", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("FIBER", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("FIBRE", StringComparison.OrdinalIgnoreCase);

    private sealed record EndpointDescriptor(
        ComponentInstance? Component,
        ComponentPort? Port,
        ComponentPin? Pin,
        TerminalBlock? TerminalBlock,
        TerminalConnectionPoint? TerminalPoint);

    private sealed record PowerVoltage(VoltageSpecification Voltage, PowerRole Role);
    private sealed record ProvidedLength(double? Value, CableLengthSource Source);
}
