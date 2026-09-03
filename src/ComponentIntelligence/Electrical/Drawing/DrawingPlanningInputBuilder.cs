using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingPlanningInputBuilder(RepresentationPolicy representationPolicy)
{
    private readonly RepresentationPolicy _representationPolicy = representationPolicy ?? throw new ArgumentNullException(nameof(representationPolicy));

    public DrawingPlanningInput Build(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var representations = new List<DrawingRepresentationDecision>();
        var issues = new List<DrawingPlanningIssue>();

        foreach (var component in project.Components.OrderBy(x => x.ComponentInstanceId, StringComparer.Ordinal))
        {
            var explicitBindings = component.Ports
                .Select(port => new DrawingPortBinding { EngineeringEndpointId = port.PortId, ConnectionPointId = $"PORT:{port.PortId}" })
                .Concat(component.Ports.SelectMany(port => port.Pins.Select(pin => new DrawingPortBinding
                {
                    EngineeringEndpointId = pin.PinId,
                    ConnectionPointId = $"PIN:{pin.PinId}"
                })))
                .GroupBy(x => x.EngineeringEndpointId, StringComparer.Ordinal)
                .Select(x => x.First())
                .OrderBy(x => x.EngineeringEndpointId, StringComparer.Ordinal)
                .ToArray();
            var result = _representationPolicy.Decide(new RepresentationRequest
            {
                RepresentationId = $"REP:{component.ComponentInstanceId}:Schematic",
                OwnerKind = DrawingRepresentationOwnerKind.Component,
                OwnerId = component.ComponentInstanceId,
                AssetComponentId = component.ComponentDefinitionId,
                Role = DrawingRepresentationRole.Schematic,
                PreferredFamily = DrawingRepresentationFamily.FunctionalGeneric,
                ControlState = DrawingRepresentationControlState.Auto,
                AllowedRotations = [0, 90],
                PortBindings = explicitBindings,
                RequiresExplicitEndpointEvidence = false,
                PhysicalInterfaceMeaning = false,
                ControllerId = null,
                PhysicalModuleId = null,
                FunctionKind = null,
                MachineZoneId = null,
                NetworkId = null,
                NetworkKind = null,
                SeriesChainId = null,
                HeavyDutyConnectorId = null,
                FieldDeviceClass = null
            });
            representations.Add(result.Decision);
            issues.AddRange(result.Issues);
        }

        var netToBus = project.Buses
            .SelectMany(bus => bus.NetIds.Select(netId => (netId, bus)))
            .GroupBy(x => x.netId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().bus, StringComparer.Ordinal);

        var connections = project.Connections.OrderBy(x => x.ConnectionId, StringComparer.Ordinal)
            .Select(connection => new DrawingConnectionPlanningItem
            {
                ConnectionId = connection.ConnectionId,
                FromEndpointId = connection.FromEndpointId,
                ToEndpointId = connection.ToEndpointId,
                NetId = connection.NetId,
                CableInstanceId = connection.CableInstanceId,
                NetworkId = connection.NetId is not null && netToBus.TryGetValue(connection.NetId, out var bus) ? bus.BusId : null,
                PhysicalInterfaceMeaning = false
            }).ToList();

        var connectionsByCable = project.Connections.Where(x => !string.IsNullOrWhiteSpace(x.CableInstanceId))
            .GroupBy(x => x.CableInstanceId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(c => c.ConnectionId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        var cables = project.Cables.OrderBy(x => x.CableInstanceId, StringComparer.Ordinal).Select(cable =>
        {
            connectionsByCable.TryGetValue(cable.CableInstanceId, out var cableConnections);
            var first = cableConnections?.FirstOrDefault();
            var endAId = first?.FromEndpointId ?? $"UNRESOLVED:{cable.CableInstanceId}:A";
            var endBId = first?.ToEndpointId ?? $"UNRESOLVED:{cable.CableInstanceId}:B";
            var mappings = cable.CoreAssignments
                .Where(x => !string.IsNullOrWhiteSpace(x.FromEndpointId) || !string.IsNullOrWhiteSpace(x.ToEndpointId))
                .OrderBy(x => x.CoreId, StringComparer.Ordinal)
                .Select(x => new DrawingPinCoreMapping
                {
                    MappingId = $"MAP:{cable.CableInstanceId}:{x.CoreId}",
                    CoreId = x.CoreId,
                    EndAContactId = x.FromEndpointId,
                    EndBContactId = x.ToEndpointId,
                    Function = x.Signal
                }).ToArray();
            return new DrawingCablePlanningItem
            {
                CableInstanceId = cable.CableInstanceId,
                ConstructionType = cable.CableConstructionType.ToString(),
                EndA = BuildEndpoint(project, endAId),
                EndB = BuildEndpoint(project, endBId),
                PinCoreMappings = mappings,
                Shield = null,
                Length = cable.ProvidedLengthMm,
                SourceControllerId = null,
                SourcePhysicalModuleId = null
            };
        }).ToList();

        foreach (var cable in cables)
        {
            representations.Add(new DrawingRepresentationDecision
            {
                RepresentationId = $"REP:{cable.CableInstanceId}:CableFunctional",
                OwnerKind = DrawingRepresentationOwnerKind.CableInstance,
                OwnerId = cable.CableInstanceId,
                Role = DrawingRepresentationRole.CableFunctional,
                Family = DrawingRepresentationFamily.CableFunctional,
                ControlState = DrawingRepresentationControlState.Auto,
                AllowedRotations = [0],
                PortBindings = [],
                PhysicalInterfaceMeaning = false
            });
            if (string.Equals(cable.ConstructionType, "Custom", StringComparison.Ordinal))
            {
                representations.Add(new DrawingRepresentationDecision
                {
                    RepresentationId = $"REP:{cable.CableInstanceId}:CableDetail",
                    OwnerKind = DrawingRepresentationOwnerKind.CableInstance,
                    OwnerId = cable.CableInstanceId,
                    Role = DrawingRepresentationRole.CableDetail,
                    Family = DrawingRepresentationFamily.CableDetail,
                    ControlState = DrawingRepresentationControlState.Auto,
                    AllowedRotations = [0],
                    PortBindings = [],
                    PhysicalInterfaceMeaning = false
                });
                if (cable.PinCoreMappings.Count == 0)
                {
                    issues.Add(new DrawingPlanningIssue
                    {
                        IssueId = $"ISSUE:{cable.CableInstanceId}:mapping",
                        Severity = DrawingPlanningIssueSeverity.Blocker,
                        Code = "DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING",
                        Message = "Custom cable detail requires explicit Pin/Core mapping evidence.",
                        TargetKind = "Cable",
                        TargetId = cable.CableInstanceId
                    });
                }
            }
        }

        var networks = project.Buses.OrderBy(x => x.BusId, StringComparer.Ordinal).Select(bus => new DrawingNetworkItem
        {
            NetworkId = bus.BusId,
            NetworkKind = bus.Protocol,
            RepresentationIds = [],
            ConnectionIds = project.Connections
                .Where(c => c.NetId is not null && bus.NetIds.Contains(c.NetId, StringComparer.Ordinal))
                .Select(c => c.ConnectionId).OrderBy(x => x, StringComparer.Ordinal).ToArray()
        }).ToList();

        var powerDomains = project.Components.SelectMany(c => c.Ports)
            .SelectMany(p => new[] { p.PowerDomainId }.Concat(p.Pins.Select(pin => pin.PowerDomainId)))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(id => new DrawingPowerDomainItem { PowerDomainId = id, RepresentationIds = [] }).ToList();

        var input = new DrawingPlanningInput
        {
            ProjectId = project.ProjectId,
            Representations = representations.OrderBy(x => x.RepresentationId, StringComparer.Ordinal).ToList(),
            Connections = connections,
            Cables = cables,
            ControllerModules = [],
            Networks = networks,
            SeriesChains = [],
            HeavyDutyConnectors = [],
            PowerDomains = powerDomains,
            WiringRules = [],
            Issues = issues.OrderBy(x => x.IssueId, StringComparer.Ordinal).ToList()
        };
        return DrawingPlanningJson.Deserialize(DrawingPlanningJson.Serialize(input));
    }

    private static DrawingCableEndpoint BuildEndpoint(ElectricalProject project, string endpointId)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.Ordinal)) return FromPort(endpointId, port);
            if (port.Pins.Any(p => string.Equals(p.PinId, endpointId, StringComparison.Ordinal))) return FromPort(endpointId, port);
        }
        return new DrawingCableEndpoint { EndpointId = endpointId, InterfaceLayoutFamily = DrawingInterfaceLayoutFamily.Other };
    }

    private static DrawingCableEndpoint FromPort(string endpointId, ComponentPort port)
    {
        var connector = port.Connector;
        return new DrawingCableEndpoint
        {
            EndpointId = endpointId,
            ConnectorId = connector?.ConnectorId,
            InterfaceLayoutFamily = connector?.Family?.Trim().ToUpperInvariant() switch
            {
                "M12" => DrawingInterfaceLayoutFamily.M12,
                "RJ45" => DrawingInterfaceLayoutFamily.RJ45,
                _ => DrawingInterfaceLayoutFamily.Other
            },
            ConnectorFamily = connector?.Family,
            Coding = connector?.Coding,
            PinCount = connector?.PinCount,
            ContactIds = port.Pins.Where(p => !string.IsNullOrWhiteSpace(p.PinId)).Select(p => p.PinId).OrderBy(x => x, StringComparer.Ordinal).ToArray()
        };
    }
}
