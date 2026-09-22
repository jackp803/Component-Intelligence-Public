using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Contracts;
using System.Text.Json;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed class DrawingPlanningInputBuilder(RepresentationPolicy representationPolicy, IReadOnlyList<ComponentIR>? catalog = null, EngineeringReviewService? engineeringReview = null)
{
    private readonly RepresentationPolicy _representationPolicy = representationPolicy ?? throw new ArgumentNullException(nameof(representationPolicy));

    public DrawingPlanningInput Build(ElectricalProject project, DrawingProjectMetadata? projectMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var representations = new List<DrawingRepresentationDecision>();
        var issues = new List<DrawingPlanningIssue>();
        if (engineeringReview is not null)
        {
            // This synchronous planning boundary must not await file hashing on the UI context.
            var coverage = Task.Run(() => engineeringReview.InspectAsync(project)).GetAwaiter().GetResult();
            foreach (var component in coverage.Components.Where(c => c.Classification == "UNSAFE_UNRESOLVED"))
                issues.Add(new DrawingPlanningIssue { IssueId = $"ISSUE:{component.InstanceId}:engineering-review",
                    Code = "ENGINEERING_COMPONENT_REVIEW_REQUIRED", Severity = DrawingPlanningIssueSeverity.Blocker,
                    Message = $"{component.Label}：{component.Reason} 請開啟工程確認。", TargetKind = "Component", TargetId = component.InstanceId });
            foreach (var cable in coverage.Cables.Where(c => c.Classification == "UNSAFE_UNRESOLVED"))
                issues.Add(new DrawingPlanningIssue { IssueId = $"ISSUE:{cable.CableId}:engineering-review",
                    Code = "ENGINEERING_CABLE_REVIEW_REQUIRED", Severity = DrawingPlanningIssueSeverity.Blocker,
                    Message = $"{cable.Label}：請在工程確認中明選 Purchased / Custom。", TargetKind = "CableInstance", TargetId = cable.CableId });
        }
        if (catalog is not null)
        {
            // Planning remains pure; explicit lineage restoration is confined to this projection copy.
            project = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(project))!;
            foreach (var instance in project.Components)
            {
                var result = new ComponentSourceIdentityRestorer().Restore(instance,
                    catalog.SingleOrDefault(c => string.Equals(c.Identity.ComponentId, instance.ComponentDefinitionId, StringComparison.Ordinal)));
                if (result.Status == SourceIdentityStatus.CONFLICT)
                    issues.Add(new DrawingPlanningIssue { IssueId = $"ISSUE:{instance.ComponentInstanceId}:source-identity", Severity = DrawingPlanningIssueSeverity.Blocker,
                        Code = "DRAWING_SOURCE_IDENTITY_CONFLICT", Message = "Component source identity conflicts with authoritative catalog; reconcile explicit source IDs.", TargetKind = "Component", TargetId = instance.ComponentInstanceId });
            }
        }

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
            if (InlineInterfaceRepresentation.IsRecognized(component.ComponentDefinitionId))
            {
                var blocking = InlineInterfaceRepresentation.BlockingReason(project, component);
                if (blocking is not null)
                    issues.Add(new DrawingPlanningIssue { IssueId = $"ISSUE:{component.ComponentInstanceId}:inline-interface",
                        Code = "INLINE_INTERFACE_EVIDENCE_REQUIRED", Severity = DrawingPlanningIssueSeverity.Blocker,
                        Message = blocking, TargetKind = "Component", TargetId = component.ComponentInstanceId });
                else
                    representations.Add(new DrawingRepresentationDecision
                    {
                        RepresentationId = $"REP:{component.ComponentInstanceId}:Schematic",
                        OwnerKind = DrawingRepresentationOwnerKind.Component, OwnerId = component.ComponentInstanceId,
                        Role = DrawingRepresentationRole.Schematic, Family = DrawingRepresentationFamily.FunctionalGeneric,
                        SourceType = "ProjectInlineInterface", ControlState = DrawingRepresentationControlState.Auto,
                        AllowedRotations = [0, 90], PortBindings = explicitBindings, PhysicalInterfaceMeaning = true
                    });
                continue;
            }
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
                SourceInstance = component,
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
            var assembly = project.CableAssemblies.SingleOrDefault(a => a.PhysicalTopology?.CableInstanceId == cable.CableInstanceId);
            DrawingMultiEndCable? multiEnd = null;
            if (assembly?.PhysicalTopology is { } physical)
            {
                var editor = new MultiEndCableEditorService();
                var draft = editor.PrepareExisting(project, assembly.CableAssemblyId);
                editor.Validate(project, draft);
                var labels = editor.GetEnds(project, draft).ToDictionary(e => e.PortId, e => e.Label, StringComparer.Ordinal);
                multiEnd = new DrawingMultiEndCable
                {
                    AssemblyId = assembly.CableAssemblyId, Reference = assembly.ReferenceDesignator,
                    CommonEnd = BuildEndpoint(project, physical.CommonPortId) with { DisplayLabel = labels[physical.CommonPortId] }, TrunkLengthMm = physical.TrunkLengthMm,
                    Branches = physical.Branches.OrderBy(b => b.Index).Select(b => new DrawingMultiEndBranch
                    { Index = b.Index, End = BuildEndpoint(project, b.PortId) with { DisplayLabel = labels[b.PortId] }, LengthMm = b.LengthMm }).ToArray(),
                    ElectricalMappings = physical.ConnectionIds.Order(StringComparer.Ordinal).Select(id =>
                    {
                        var c = project.Connections.Single(c => c.ConnectionId == id);
                        return new DrawingMultiEndElectricalMapping { ConnectionId = id, FromEndpointId = c.FromEndpointId,
                            ToEndpointId = c.ToEndpointId, NetId = c.NetId, CoreId = c.CableCoreId,
                            FromLabel = MappingEndpointLabel(project, c.FromEndpointId), ToLabel = MappingEndpointLabel(project, c.ToEndpointId),
                            OriginalCableInstanceId = physical.ConductorEvidence.SingleOrDefault(e => e.ConnectionId == id)?.OriginalCableInstanceId };
                    }).ToArray()
                };
            }
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
                EndA = multiEnd is null ? BuildEndpoint(project, endAId) : null,
                EndB = multiEnd is null ? BuildEndpoint(project, endBId) : null,
                MultiEnd = multiEnd,
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
                if (cable.PinCoreMappings.Count == 0 && cable.MultiEnd is null)
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
            ProjectMetadata = projectMetadata ?? DrawingProjectMetadata.Empty,
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

    private static string MappingEndpointLabel(ElectricalProject project, string endpointId)
    {
        var match = project.Components.SelectMany(c => c.Ports.SelectMany(p => p.Pins.Select(pin => (c, p, pin))))
            .Single(x => x.pin.PinId == endpointId);
        return $"{match.c.ReferenceDesignator ?? match.c.DisplayName ?? "?"} / {match.p.Name} / Pin {match.pin.PinNumber}";
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

    private static DrawingCableEndpoint FromPort(string endpointId, ComponentIntelligence.Electrical.Domain.ComponentPort port)
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
