using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Validation;

public sealed record ValidationResult
{
    public required string RuleId { get; init; }
    public required ValidationSeverity Severity { get; init; }
    public required string Message { get; init; }
    public List<string> SourceObjectIds { get; init; } = new();
    public bool RequiresConfirmation { get; init; }
    public bool RequiresPreExportReview { get; init; }
    public bool AffectsDrawingExport { get; init; }
    public string? ConfirmationStatus { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmationReason { get; set; }
}

public sealed record ValidationReport
{
    public required DrawingReadiness DrawingReadiness { get; init; }
    public required IReadOnlyList<ValidationResult> Results { get; init; }
}

public sealed class ElectricalProjectValidator
{
    public ValidationReport Validate(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var results = new List<ValidationResult>();
        var endpoints = BuildEndpointMap(project);
        var connectedEndpointIds = project.Connections
            .SelectMany(connection => new[] { connection.FromEndpointId, connection.ToEndpointId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ValidateReferences(project, results);
        ValidateConnections(project, endpoints, results);
        ValidateRequiredPins(project, connectedEndpointIds, results);
        ValidateRs485Pairs(project, connectedEndpointIds, results);
        ValidateTerminalCapacity(project, results);
        ValidateBusAddresses(project, results);

        var readiness = results.Any(result => result.Severity == ValidationSeverity.Block && result.AffectsDrawingExport)
            ? DrawingReadiness.Blocked
            : results.Any(result => result.RequiresPreExportReview || result.Severity == ValidationSeverity.Error)
                ? DrawingReadiness.ReviewRequired
                : DrawingReadiness.Ready;

        return new ValidationReport { DrawingReadiness = readiness, Results = results };
    }

    private static void ValidateReferences(ElectricalProject project, ICollection<ValidationResult> results)
    {
        foreach (var group in project.Components
                     .Where(component => !string.IsNullOrWhiteSpace(component.ReferenceDesignator))
                     .GroupBy(component => component.ReferenceDesignator!, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            results.Add(Block("RULE-NAME-002", $"Duplicate component reference designator '{group.Key}'.", group.Select(item => item.ComponentInstanceId)));
        }

        foreach (var group in project.TerminalBlocks
                     .GroupBy(block => block.ReferenceDesignator, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            results.Add(Block("RULE-NAME-TB-001", $"Duplicate terminal block reference designator '{group.Key}'.", group.Select(item => item.TerminalBlockId)));
        }
    }

    private static void ValidateConnections(
        ElectricalProject project,
        IReadOnlyDictionary<string, EndpointInfo> endpoints,
        ICollection<ValidationResult> results)
    {
        foreach (var connection in project.Connections)
        {
            if (!endpoints.TryGetValue(connection.FromEndpointId, out var from) ||
                !endpoints.TryGetValue(connection.ToEndpointId, out var to))
            {
                results.Add(Block("RULE-CONN-001", "Connection references an endpoint that does not exist.", new[] { connection.ConnectionId, connection.FromEndpointId, connection.ToEndpointId }));
                continue;
            }

            ValidateProtocol(connection, from, to, results);

            if (from.Pin is not null && to.Pin is not null)
            {
                ValidatePower(connection, from.Pin, to.Pin, results);
                ValidateDigital(connection, from.Pin, to.Pin, results);
                ValidateAnalog(connection, from.Pin, to.Pin, results);
            }

            if (connection.Kind == ConnectionKind.DirectMating && from.Port?.Connector is not null && to.Port?.Connector is not null)
                ValidateConnectorMating(connection, from.Port.Connector, to.Port.Connector, results);
        }
    }

    private static void ValidateProtocol(ElectricalConnection connection, EndpointInfo from, EndpointInfo to, ICollection<ValidationResult> results)
    {
        var fromProtocol = from.Pin?.Protocol ?? from.Port?.Protocol;
        var toProtocol = to.Pin?.Protocol ?? to.Port?.Protocol;
        if (string.IsNullOrWhiteSpace(fromProtocol) || string.IsNullOrWhiteSpace(toProtocol)) return;
        if (string.Equals(fromProtocol, toProtocol, StringComparison.OrdinalIgnoreCase)) return;

        results.Add(Block("RULE-PROTOCOL-001",
            $"Protocol mismatch: {fromProtocol} cannot be directly connected to {toProtocol}.",
            new[] { connection.ConnectionId, from.EndpointId, to.EndpointId }));
    }

    private static void ValidateConnectorMating(
        ElectricalConnection connection,
        ConnectorDefinition first,
        ConnectorDefinition second,
        ICollection<ValidationResult> results)
    {
        if (!string.Equals(first.Family, second.Family, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(Block("RULE-CONNECTOR-001", $"Connector family mismatch: {first.Family} vs {second.Family}.", new[] { connection.ConnectionId, first.ConnectorId, second.ConnectorId }));
            return;
        }

        if (!string.IsNullOrWhiteSpace(first.Coding) && !string.IsNullOrWhiteSpace(second.Coding) &&
            !string.Equals(first.Coding, second.Coding, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(Block("RULE-CONNECTOR-002", $"Connector coding mismatch: {first.Coding} vs {second.Coding}.", new[] { connection.ConnectionId, first.ConnectorId, second.ConnectorId }));
        }

        if (first.Gender != ConnectorGender.Unknown && second.Gender != ConnectorGender.Unknown &&
            !GenderCompatible(first.Gender, second.Gender))
        {
            results.Add(Block("RULE-CONNECTOR-003", $"Connector gender is not mate-compatible: {first.Gender} vs {second.Gender}.", new[] { connection.ConnectionId, first.ConnectorId, second.ConnectorId }));
        }
    }

    private static bool GenderCompatible(ConnectorGender first, ConnectorGender second) =>
        (first == ConnectorGender.Male && second == ConnectorGender.Female) ||
        (first == ConnectorGender.Female && second == ConnectorGender.Male) ||
        (first == ConnectorGender.Genderless && second == ConnectorGender.Genderless);

    private static void ValidatePower(ElectricalConnection connection, ComponentPin first, ComponentPin second, ICollection<ValidationResult> results)
    {
        if (first.Power is null || second.Power is null) return;

        if (first.Power.Role == PowerRole.Source && second.Power.Role == PowerRole.Source)
            results.Add(Block("RULE-PWR-004", "Two power sources are directly connected without an explicit parallel-source rule.", new[] { connection.ConnectionId, first.PinId, second.PinId }));

        var firstVoltage = first.Power.Voltage;
        var secondVoltage = second.Power.Voltage;
        if (firstVoltage is not null && secondVoltage is not null &&
            firstVoltage.Type != VoltageType.Unknown && secondVoltage.Type != VoltageType.Unknown &&
            firstVoltage.Type != secondVoltage.Type)
        {
            results.Add(Block("RULE-PWR-001", $"AC/DC mismatch: {firstVoltage.Type} vs {secondVoltage.Type}.", new[] { connection.ConnectionId, first.PinId, second.PinId }));
        }

        ValidateSourceVoltageAgainstInput(connection, first, second, results);
        ValidateSourceVoltageAgainstInput(connection, second, first, results);

        if (first.Power.Polarity is not (Polarity.Unknown or Polarity.None) &&
            second.Power.Polarity is not (Polarity.Unknown or Polarity.None) &&
            first.Power.Polarity != second.Power.Polarity)
        {
            results.Add(Block("RULE-PWR-003", $"Power polarity mismatch: {first.Power.Polarity} vs {second.Power.Polarity}.", new[] { connection.ConnectionId, first.PinId, second.PinId }));
        }
    }

    private static void ValidateSourceVoltageAgainstInput(ElectricalConnection connection, ComponentPin sourcePin, ComponentPin inputPin, ICollection<ValidationResult> results)
    {
        if (sourcePin.Power?.Role != PowerRole.Source || inputPin.Power?.Role != PowerRole.Input) return;
        var sourceVoltage = sourcePin.Power.Voltage?.NominalVoltage;
        var inputVoltage = inputPin.Power.Voltage;
        if (sourceVoltage is null || inputVoltage is null) return;

        if (inputVoltage.MinVoltage is double min && sourceVoltage.Value < min ||
            inputVoltage.MaxVoltage is double max && sourceVoltage.Value > max)
        {
            results.Add(Block("RULE-PWR-002",
                $"Source voltage {sourceVoltage.Value:g} V is outside the input range {inputVoltage.MinVoltage?.ToString("g") ?? "?"}..{inputVoltage.MaxVoltage?.ToString("g") ?? "?"} V.",
                new[] { connection.ConnectionId, sourcePin.PinId, inputPin.PinId }));
        }
    }

    private static void ValidateDigital(ElectricalConnection connection, ComponentPin first, ComponentPin second, ICollection<ValidationResult> results)
    {
        if (first.Digital is null || second.Digital is null) return;

        if (first.Digital.IoType == DigitalIoType.Do && second.Digital.IoType == DigitalIoType.Do)
            results.Add(Block("RULE-DIO-001", "Two digital outputs are directly connected.", new[] { connection.ConnectionId, first.PinId, second.PinId }));

        if (first.Digital.ElectricalBehavior == DigitalElectricalBehavior.Source && second.Digital.ElectricalBehavior == DigitalElectricalBehavior.Source)
            results.Add(Block("RULE-DIO-002", "Two sourcing digital interfaces are directly connected.", new[] { connection.ConnectionId, first.PinId, second.PinId }));

        ValidateDigitalCurrent(connection, first, second, results);
        ValidateDigitalCurrent(connection, second, first, results);
    }

    private static void ValidateDigitalCurrent(ElectricalConnection connection, ComponentPin output, ComponentPin input, ICollection<ValidationResult> results)
    {
        if (output.Digital?.IoType != DigitalIoType.Do || input.Digital is null) return;
        if (output.Digital.MaxOutputCurrentAmp is not double max || input.Digital.RequiredInputCurrentAmp is not double required) return;
        if (required <= max) return;

        results.Add(new ValidationResult
        {
            RuleId = "RULE-DIO-003",
            Severity = ValidationSeverity.Error,
            Message = $"Digital output capacity {max:g} A is below required load current {required:g} A.",
            SourceObjectIds = new List<string> { connection.ConnectionId, output.PinId, input.PinId },
            RequiresPreExportReview = true
        });
    }

    private static void ValidateAnalog(ElectricalConnection connection, ComponentPin first, ComponentPin second, ICollection<ValidationResult> results)
    {
        if (first.Analog is null || second.Analog is null) return;
        if (first.Analog.SignalStandard != AnalogSignalStandard.Unknown &&
            second.Analog.SignalStandard != AnalogSignalStandard.Unknown &&
            first.Analog.SignalStandard != second.Analog.SignalStandard)
        {
            results.Add(Block("RULE-AIO-002", $"Analog signal standard mismatch: {first.Analog.SignalStandard} vs {second.Analog.SignalStandard}.", new[] { connection.ConnectionId, first.PinId, second.PinId }));
        }

        if (first.Analog.SignalStandard == AnalogSignalStandard.Current4To20mA &&
            second.Analog.SignalStandard == AnalogSignalStandard.Current4To20mA)
        {
            var bothPassive = IsPassiveLoopRole(first.Analog.LoopRole) && IsPassiveLoopRole(second.Analog.LoopRole);
            var bothActive = IsActiveLoopRole(first.Analog.LoopRole) && IsActiveLoopRole(second.Analog.LoopRole);
            if (bothPassive || bothActive)
            {
                results.Add(new ValidationResult
                {
                    RuleId = "RULE-AIO-004",
                    Severity = ValidationSeverity.Error,
                    Message = bothPassive ? "4–20 mA loop has no active loop source." : "4–20 mA loop has conflicting active loop roles.",
                    SourceObjectIds = new List<string> { connection.ConnectionId, first.PinId, second.PinId },
                    RequiresPreExportReview = true
                });
            }
        }
    }

    private static bool IsPassiveLoopRole(LoopRole role) => role is LoopRole.PassiveDevice or LoopRole.PassiveInput;
    private static bool IsActiveLoopRole(LoopRole role) => role is LoopRole.ActiveSource or LoopRole.ActiveInput;

    private static void ValidateRequiredPins(ElectricalProject project, ISet<string> connectedEndpointIds, ICollection<ValidationResult> results)
    {
        var reviews = project.EndpointReviews.ToDictionary(review => review.EndpointId, StringComparer.OrdinalIgnoreCase);
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        foreach (var pin in port.Pins.Where(pin => pin.IsRequired && pin.Status is not (PinStatus.Nc or PinStatus.Reserved)))
        {
            if (connectedEndpointIds.Contains(pin.PinId)) continue;
            if (reviews.TryGetValue(pin.PinId, out var review) && IsResolvedDisposition(review.Disposition)) continue;

            var isPe = pin.GroundReferenceType == GroundReferenceType.ProtectiveEarth;
            results.Add(new ValidationResult
            {
                RuleId = isPe ? "RULE-GND-VAL-005" : "RULE-PREEXPORT-UNCONNECTED",
                Severity = isPe ? ValidationSeverity.Error : ValidationSeverity.Warning,
                Message = isPe ? $"Required PE endpoint '{pin.PinId}' is unconnected." : $"Required endpoint '{pin.PinId}' is unconnected and has no disposition.",
                SourceObjectIds = new List<string> { component.ComponentInstanceId, port.PortId, pin.PinId },
                RequiresPreExportReview = true
            });
        }
    }

    private static bool IsResolvedDisposition(EndpointDisposition disposition) => disposition is
        EndpointDisposition.IntentionallyUnused or EndpointDisposition.OutOfScope or EndpointDisposition.NotApplicable;

    private static void ValidateRs485Pairs(ElectricalProject project, ISet<string> connectedEndpointIds, ICollection<ValidationResult> results)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            var rs485Pins = port.Pins.Where(pin =>
                    string.Equals(pin.Protocol, "RS485", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(port.Protocol, "RS485", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rs485Pins.Count == 0) continue;

            var positive = rs485Pins.Where(pin => pin.DifferentialRole == DifferentialRole.Positive).ToList();
            var negative = rs485Pins.Where(pin => pin.DifferentialRole == DifferentialRole.Negative).ToList();
            if (positive.Count == 0 || negative.Count == 0)
            {
                results.Add(new ValidationResult
                {
                    RuleId = "RULE-RS485-009",
                    Severity = ValidationSeverity.Warning,
                    Message = $"RS485 port '{port.PortId}' does not have verified positive/negative differential pin roles.",
                    SourceObjectIds = new List<string> { component.ComponentInstanceId, port.PortId },
                    RequiresPreExportReview = true
                });
                continue;
            }

            var positiveConnected = positive.Any(pin => connectedEndpointIds.Contains(pin.PinId));
            var negativeConnected = negative.Any(pin => connectedEndpointIds.Contains(pin.PinId));
            if (positiveConnected == negativeConnected) continue;

            results.Add(new ValidationResult
            {
                RuleId = "RULE-RS485-008",
                Severity = ValidationSeverity.Warning,
                Message = $"RS485 differential pair on port '{port.PortId}' is incomplete: only one side of the A/B pair is connected.",
                SourceObjectIds = new List<string> { component.ComponentInstanceId, port.PortId },
                RequiresPreExportReview = true
            });
        }
    }

    private static void ValidateTerminalCapacity(ElectricalProject project, ICollection<ValidationResult> results)
    {
        var usage = project.Connections
            .SelectMany(connection => new[]
            {
                (EndpointId: connection.FromEndpointId, Connection: connection),
                (EndpointId: connection.ToEndpointId, Connection: connection)
            })
            .GroupBy(item => item.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Connection).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var block in project.TerminalBlocks)
        foreach (var position in block.Positions)
        foreach (var level in position.Levels)
        foreach (var point in level.ConnectionPoints.Where(point => point.Type is ConnectionPointType.ConductorEntry or ConnectionPointType.PeConnection))
        {
            if (!usage.TryGetValue(point.ConnectionPointId, out var connections)) continue;
            if (connections.Count > point.MaxConductors)
                results.Add(Block("RULE-TERM-006", $"Terminal connection point '{point.ConnectionPointId}' accepts at most {point.MaxConductors} conductor(s) but has {connections.Count}.", new[] { block.TerminalBlockId, point.ConnectionPointId }));

            foreach (var connection in connections.Where(connection => connection.ConductorAreaMm2.HasValue))
            {
                var area = connection.ConductorAreaMm2!.Value;
                if (point.MinWireAreaMm2 is double min && area < min || point.MaxWireAreaMm2 is double max && area > max)
                    results.Add(Block("RULE-TERM-007", $"Conductor area {area:g} mm² is outside terminal range {point.MinWireAreaMm2?.ToString("g") ?? "?"}..{point.MaxWireAreaMm2?.ToString("g") ?? "?"} mm².", new[] { block.TerminalBlockId, point.ConnectionPointId, connection.ConnectionId }));
            }
        }
    }

    private static void ValidateBusAddresses(ElectricalProject project, ICollection<ValidationResult> results)
    {
        foreach (var bus in project.Buses)
        foreach (var duplicate in bus.NodeAddresses.GroupBy(pair => pair.Value).Where(group => group.Count() > 1))
        {
            results.Add(new ValidationResult
            {
                RuleId = "RULE-PROTOCOL-ADDRESS-001",
                Severity = ValidationSeverity.Warning,
                Message = $"Bus '{bus.BusId}' has duplicate node address {duplicate.Key}.",
                SourceObjectIds = duplicate.Select(pair => pair.Key).Prepend(bus.BusId).ToList(),
                RequiresPreExportReview = true
            });
        }
    }

    private static Dictionary<string, EndpointInfo> BuildEndpointMap(ElectricalProject project)
    {
        var endpoints = new Dictionary<string, EndpointInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            endpoints[port.PortId] = new EndpointInfo(port.PortId, null, port, null);
            foreach (var pin in port.Pins)
                endpoints[pin.PinId] = new EndpointInfo(pin.PinId, pin, port, null);
        }

        foreach (var block in project.TerminalBlocks)
        foreach (var position in block.Positions)
        foreach (var level in position.Levels)
        foreach (var point in level.ConnectionPoints)
            endpoints[point.ConnectionPointId] = new EndpointInfo(point.ConnectionPointId, null, null, point);

        return endpoints;
    }

    private static ValidationResult Block(string ruleId, string message, IEnumerable<string> sourceIds) => new()
    {
        RuleId = ruleId,
        Severity = ValidationSeverity.Block,
        Message = message,
        SourceObjectIds = sourceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        AffectsDrawingExport = true
    };

    private sealed record EndpointInfo(
        string EndpointId,
        ComponentPin? Pin,
        ComponentPort? Port,
        TerminalConnectionPoint? TerminalPoint);
}
