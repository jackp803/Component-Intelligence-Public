using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Electrical.Export;

/// <summary>
/// Converts ElectricalProject topology into the LRDU v1 staging graph. Proven pin-level topology
/// becomes drawable routes; incomplete field evidence remains an explicit, non-drawable boundary.
/// It is an in-memory preparation boundary: it neither serializes a file nor authorizes a writer.
/// </summary>
public sealed class AutocadStagingGraphBuilder
{
    private readonly AutocadExportPreflightService _preflight = new();

    public AutocadStagingGraphPreparationResult Prepare(
        ElectricalProject project,
        IEnumerable<AutocadConnectionPointBinding> auditedBindings,
        AutocadEngineeringDrawingEvidence? drawingEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(auditedBindings);

        drawingEvidence ??= new AutocadEngineeringDrawingEvidence();
        var bindings = auditedBindings.ToArray();
        var bindingMap = bindings
            .GroupBy(binding => binding.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var request = new AutocadExportPreflightRequest();
        var resolutions = new Dictionary<string, StagingEndpoint?>(StringComparer.OrdinalIgnoreCase);
        var boundaryConnections = new Dictionary<string, FieldBoundaryReason>(StringComparer.OrdinalIgnoreCase);
        var netComponents = AutocadMachineNetIdentityResolver.Analyze(project);
        foreach (var component in netComponents.Where(component => component.IsAmbiguous))
        {
            request.AdditionalIssues.Add(Error(
                AutocadExportPreflightIssueCode.ConflictingExplicitNetIdentity,
                $"Connected endpoint component [{string.Join(", ", component.ConnectedEndpointIds)}] declares conflicting explicit net identities [{string.Join(", ", component.ExplicitNetIds)}]; one functional net identity is required.",
                component.ConnectedEndpointIds.Concat(component.ExplicitNetIds).ToArray()));
        }
        var netIdentityByConnection = netComponents
            .Where(component => !component.IsAmbiguous)
            .SelectMany(component => component.Connections.Select(connection => new
            {
                Connection = connection,
                NetIdentity = component.NetIdentity!
            }))
            .ToDictionary(item => item.Connection, item => item.NetIdentity);
        var resolvedConnections = project.Connections
            .Where(netIdentityByConnection.ContainsKey)
            .ToArray();
        var knownNetIdentities = netIdentityByConnection.Values.ToHashSet(StringComparer.Ordinal);
        var roleEvidence = ResolveRoleEvidence(project, drawingEvidence, request);
        var powerEvidence = ResolvePowerEvidence(drawingEvidence, request, knownNetIdentities);
        var cableOverrides = ResolveCableOverrides(project, drawingEvidence, request);
        ValidatePageArchetypeHint(drawingEvidence, request);
        ValidateCrossPageContinuations(project, drawingEvidence, request);
        var terminalContinuities = BuildTerminalContinuities(project, request);

        foreach (var duplicate in bindings.GroupBy(binding => binding.EndpointId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.DuplicateEndpointId,
                $"Audited ACADE binding for endpoint '{duplicate.Key}' is not unique.", duplicate.Key));
        }

        foreach (var duplicate in project.Connections.GroupBy(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.DuplicateEdgeId,
                $"Electrical connection ID '{duplicate.Key}' is missing or not unique.", duplicate.Key));
        }

        foreach (var connection in project.Connections)
        {
            if (!netIdentityByConnection.TryGetValue(connection, out var netIdentity)) continue;
            var from = Resolve(project, connection.FromEndpointId, resolutions);
            var to = Resolve(project, connection.ToEndpointId, resolutions);
            var crossesResponsibilityBoundary = IsOutOfScope(from) || IsOutOfScope(to);
            if (crossesResponsibilityBoundary)
            {
                boundaryConnections[connection.ConnectionId] = FieldBoundaryReason.Responsibility;
                request.OpenItems.Add(new AutocadExportPreflightOpenItem
                {
                    ItemId = $"responsibility:{connection.ConnectionId}",
                    Kind = AutocadExportPreflightOpenItemKind.PowerTeamResponsibilityBoundary
                });
            }

            var fieldBoundaryReason = crossesResponsibilityBoundary
                ? FieldBoundaryReason.Responsibility
                : ResolveFieldBoundaryReason(project, connection, from, to, request);
            if (fieldBoundaryReason is not null)
                boundaryConnections[connection.ConnectionId] = fieldBoundaryReason.Value;

            AddEndpointPreflight(request, connection.FromEndpointId, from, netIdentity, bindingMap,
                crossesResponsibilityBoundary,
                allowsFieldBoundary: fieldBoundaryReason is not null);
            AddEndpointPreflight(request, connection.ToEndpointId, to, netIdentity, bindingMap,
                crossesResponsibilityBoundary,
                allowsFieldBoundary: fieldBoundaryReason is not null);
            if (from?.IsShield == true || to?.IsShield == true)
                request.OpenItems.Add(new AutocadExportPreflightOpenItem
                {
                    ItemId = $"shield:{connection.ConnectionId}",
                    Kind = AutocadExportPreflightOpenItemKind.ShieldTerminationTbd
                });

            if (!crossesResponsibilityBoundary && fieldBoundaryReason is null)
            {
                request.ConfirmedEdges.Add(new AutocadExportPreflightEdge
                {
                    EdgeId = connection.ConnectionId,
                    FromEndpointId = connection.FromEndpointId,
                    ToEndpointId = connection.ToEndpointId,
                    IsContinuous = from is not null && to is not null && from.IsPinLevel && to.IsPinLevel
                });

                if (connection.Kind == ConnectionKind.Cable)
                {
                    var cable = FindCable(project, connection.CableInstanceId)!;
                    if (cable.ProvidedLengthMm is null)
                        request.OpenItems.Add(new AutocadExportPreflightOpenItem { ItemId = $"length:{connection.ConnectionId}", Kind = AutocadExportPreflightOpenItemKind.CableLengthTbd });
                    if (string.Equals(cable.CableDefinitionId, "UNRESOLVED-CABLE", StringComparison.OrdinalIgnoreCase))
                        request.OpenItems.Add(new AutocadExportPreflightOpenItem { ItemId = $"procurement:{connection.ConnectionId}", Kind = AutocadExportPreflightOpenItemKind.ProcurementTbd });
                }
            }
        }

        foreach (var group in resolvedConnections.GroupBy(
                     connection => netIdentityByConnection[connection], StringComparer.Ordinal))
        {
            if (!IsPowerRoute(project, group)) continue;
            if (!powerEvidence.TryGetValue(group.Key, out var orientation) || !IsConfirmed(orientation, group))
            {
                var issue = orientation?.Status == DrawingEvidenceStatus.Confirmed
                    ? Error(AutocadExportPreflightIssueCode.InvalidPowerFlowOrientationEvidence,
                        $"Confirmed power-flow evidence for net '{group.Key}' does not identify two route endpoints and a known orientation.", group.Key)
                    : Error(AutocadExportPreflightIssueCode.PowerFlowOrientationUnknown,
                        $"Power-flow orientation for net '{group.Key}' is not engineer-confirmed; endpoint order is not treated as direction evidence.", group.Key);
                request.AdditionalIssues.Add(issue);
            }
        }

        var preflight = _preflight.Evaluate(request);
        if (!preflight.CanStageForReview)
            return new AutocadStagingGraphPreparationResult { Preflight = preflight };

        var routes = new List<AutocadStagingRoute>();
        var interventions = new List<AutocadStagingIntervention>();
        AddRoleInterventions(project, roleEvidence, interventions);
        var familyByCableId = project.Cables
            .GroupBy(cable => cable.CableInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => CableFamilySignature.Create(project, group.First()), StringComparer.OrdinalIgnoreCase);
        foreach (var group in resolvedConnections.GroupBy(connection => netIdentityByConnection[connection], StringComparer.Ordinal))
        {
            var visibleLabel = preflight.NetLabels.Single(label =>
                string.Equals(label.MachineNetIdentity, group.Key, StringComparison.Ordinal)).VisibleLabel;
            routes.Add(BuildRoute(project, group.Key, visibleLabel, group.ToArray(), resolutions, bindingMap,
                boundaryConnections, roleEvidence, powerEvidence, familyByCableId, interventions));
        }


        var cableFamilies = familyByCableId.Values
            .GroupBy(family => family.CableFamilyId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(family => family.CableFamilyId, StringComparer.Ordinal)
            .ToArray();
        var cableInstances = project.Cables.OrderBy(cable => cable.CableInstanceId, StringComparer.Ordinal)
            .Select(cable =>
            {
                cableOverrides.TryGetValue(cable.CableInstanceId, out var instanceOverride);
                return new AutocadStagingCableInstance
                {
                    CableInstanceId = cable.CableInstanceId,
                    CableFamilyId = familyByCableId[cable.CableInstanceId].CableFamilyId,
                    CableDefinitionId = cable.CableDefinitionId,
                    ProvidedLengthMm = cable.ProvidedLengthMm,
                    LengthSource = cable.LengthSource,
                    SpecificationOverride = instanceOverride?.SpecificationOverride,
                    CatalogOverride = instanceOverride?.CatalogOverride
                };
            }).ToArray();

        return new AutocadStagingGraphPreparationResult
        {
            Preflight = preflight,
            Graph = new AutocadStagingGraphContract
            {
                ProjectId = project.ProjectId,
                PageArchetypeHint = ResolvePageArchetypeHint(drawingEvidence.PageArchetypeHint),
                Routes = routes,
                CableFamilies = cableFamilies,
                CableInstances = cableInstances,
                TerminalContinuities = terminalContinuities,
                CrossPageContinuations = drawingEvidence.CrossPageContinuations
                    .OrderBy(item => item.PairIdentity, StringComparer.Ordinal)
                    .Select(ToContract).ToArray(),
                Interventions = interventions
            }
        };
    }

    private static AutocadStagingRoute BuildRoute(
        ElectricalProject project,
        string netIdentity,
        string visibleLabel,
        IReadOnlyList<ElectricalConnection> connections,
        IReadOnlyDictionary<string, StagingEndpoint?> resolutions,
        IReadOnlyDictionary<string, AutocadConnectionPointBinding> bindings,
        IReadOnlyDictionary<string, FieldBoundaryReason> boundaryConnections,
        IReadOnlyDictionary<string, AutocadComponentDrawingRoleEvidence> roleEvidence,
        IReadOnlyDictionary<string, AutocadPowerFlowOrientationEvidence> powerEvidence,
        IReadOnlyDictionary<string, CableFamilySignature> familyByCableId,
        ICollection<AutocadStagingIntervention> interventions)
    {
        var nodes = new Dictionary<string, AutocadStagingNode>(StringComparer.OrdinalIgnoreCase);
        var segments = new List<AutocadStagingSegment>();
        var shieldSegmentIds = new List<string>();
        var hasBoundary = false;

        foreach (var connection in connections)
        {
            var from = resolutions[connection.FromEndpointId]!;
            var to = resolutions[connection.ToEndpointId]!;
            if (boundaryConnections.TryGetValue(connection.ConnectionId, out var boundaryReason))
            {
                hasBoundary = true;
                var signalCode = $"{visibleLabel}-TBD";
                if (boundaryReason == FieldBoundaryReason.Responsibility &&
                    AddResponsibilityBoundaryRoute(nodes, segments, connection, from, to, bindings, roleEvidence, signalCode, interventions))
                {
                    if (from.IsShield || to.IsShield) shieldSegmentIds.Add($"segment:{connection.ConnectionId}:unknown");
                    continue;
                }

                var interventionId = $"{BoundaryCategory(boundaryReason)}:{connection.ConnectionId}";
                var fromBoundary = AddConnectionBoundaryNode(nodes, connection.ConnectionId, "from", connection.FromEndpointId, signalCode,
                    $"{BoundaryMessage(boundaryReason)} Source endpoint '{connection.FromEndpointId}' is retained only as a field boundary.");
                var toBoundary = AddConnectionBoundaryNode(nodes, connection.ConnectionId, "to", connection.ToEndpointId, signalCode,
                    $"{BoundaryMessage(boundaryReason)} Destination endpoint '{connection.ToEndpointId}' is retained only as a field boundary.");
                interventions.Add(new AutocadStagingIntervention
                {
                    InterventionId = interventionId,
                    Category = BoundaryCategory(boundaryReason),
                    Message = $"{BoundaryMessage(boundaryReason)} No pin, core, or direct conductor was inferred.",
                    DrawingMayContinue = true
                });
                segments.Add(new AutocadStagingSegment
                {
                    SegmentId = $"segment:{connection.ConnectionId}:unknown",
                    Kind = "UnknownFieldSegment",
                    FromNodeId = fromBoundary.NodeId,
                    ToNodeId = toBoundary.NodeId,
                    TopologyStatus = "Unknown",
                    ProcurementStatus = "NotApplicable",
                    DrawingRepresentation = "NotDrawn",
                    SignalCode = signalCode,
                    BomRequired = false,
                    InstalledLengthStatus = "NotApplicable",
                    InterventionId = interventionId
                });
                if (from.IsShield || to.IsShield) shieldSegmentIds.Add($"segment:{connection.ConnectionId}:unknown");
                continue;
            }

            var fromNode = AddNode(nodes, from, bindings, roleEvidence);
            var toNode = AddNode(nodes, to, bindings, roleEvidence);
            var cable = connection.Kind == ConnectionKind.Cable;
            var shield = from.IsShield || to.IsShield;
            var cableInstance = cable ? project.Cables.Single(item =>
                string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase)) : null;
            var procurementConfirmed = cable && !string.Equals(cableInstance!.CableDefinitionId, "UNRESOLVED-CABLE", StringComparison.OrdinalIgnoreCase);
            var procurementTbd = cable && !procurementConfirmed;
            var lengthTbd = cable && cableInstance!.ProvidedLengthMm is null;
            var procurementInterventionId = procurementTbd || lengthTbd
                ? $"procurement:{connection.ConnectionId}"
                : null;
            if (procurementInterventionId is not null)
                interventions.Add(new AutocadStagingIntervention
                {
                    InterventionId = procurementInterventionId,
                    Category = "procurement",
                    Message = procurementTbd && lengthTbd
                        ? $"Cable catalog and installed length for '{connection.ConnectionId}' are TBD."
                        : procurementTbd
                            ? $"Cable catalog for '{connection.ConnectionId}' is TBD."
                            : $"Cable installed length for '{connection.ConnectionId}' is TBD.",
                    DrawingMayContinue = true
                });

            segments.Add(new AutocadStagingSegment
            {
                SegmentId = $"segment:{connection.ConnectionId}",
                Kind = shield ? "Shield" : connection.Kind switch
                {
                    ConnectionKind.DirectMating => "ConnectorMating",
                    ConnectionKind.Cable => "Cable",
                    _ => "InternalWire"
                },
                FromNodeId = fromNode.NodeId,
                ToNodeId = toNode.NodeId,
                TopologyStatus = "Confirmed",
                ProcurementStatus = cable && !procurementConfirmed ? "TBD" : cable ? "Confirmed" : "NotApplicable",
                DrawingRepresentation = shield ? "NotDrawn" : connection.Kind == ConnectionKind.DirectMating ? "ConnectorMating" : "DirectWire",
                SignalCode = visibleLabel,
                BomRequired = cable,
                BomItemId = procurementConfirmed ? cableInstance!.CableDefinitionId : null,
                InstalledLengthStatus = cable ? cableInstance!.ProvidedLengthMm is null ? "TBD" : "Confirmed" : "NotApplicable",
                CableInstanceId = cable ? cableInstance!.CableInstanceId : null,
                CableFamilyId = cable ? familyByCableId[cableInstance!.CableInstanceId].CableFamilyId : null,
                InterventionId = procurementInterventionId
            });
            if (shield) shieldSegmentIds.Add($"segment:{connection.ConnectionId}");
        }

        var shieldRoute = shieldSegmentIds.Count == 0
            ? new AutocadStagingShieldRoute { Status = "NotApplicable" }
            : new AutocadStagingShieldRoute
            {
                Status = "Unknown",
                PathSegmentIds = shieldSegmentIds,
                InterventionId = $"shield:{netIdentity}"
            };
        if (shieldSegmentIds.Count > 0)
            interventions.Add(new AutocadStagingIntervention
            {
                InterventionId = $"shield:{netIdentity}",
                Category = "shield",
                Message = "Shield path is explicit but termination and grounding strategy are TBD; no bonding was derived.",
                DrawingMayContinue = true
            });

        AutocadStagingPowerFlowOrientation? powerFlow = null;
        if (IsPowerRoute(project, connections))
        {
            powerEvidence.TryGetValue(netIdentity, out var orientation);
            var confirmed = orientation is not null && IsConfirmed(orientation, connections);
            var interventionId = confirmed ? null : $"power-flow:{netIdentity}";
            if (interventionId is not null)
                interventions.Add(new AutocadStagingIntervention
                {
                    InterventionId = interventionId,
                    Category = "power-flow",
                    Message = $"Power-flow orientation for net '{netIdentity}' is not confirmed; connection order was not promoted to direction evidence.",
                    DrawingMayContinue = false
                });
            powerFlow = new AutocadStagingPowerFlowOrientation
            {
                Orientation = orientation?.Orientation ?? PowerFlowOrientation.Unknown,
                EvidenceStatus = orientation?.Orientation == PowerFlowOrientation.Unknown
                    ? DrawingEvidenceStatus.Unknown
                    : orientation?.Status ?? DrawingEvidenceStatus.Unknown,
                SourceEndpointId = orientation?.SourceEndpointId,
                DestinationEndpointId = orientation?.DestinationEndpointId,
                EvidenceSource = orientation?.EvidenceSource,
                InterventionId = interventionId
            };
        }

        return new AutocadStagingRoute
        {
            RouteId = $"route:{netIdentity}",
            NetIdentity = netIdentity,
            VisibleLabel = visibleLabel,
            TopologyStatus = hasBoundary ? "FieldBoundary" : "Confirmed",
            Responsibility = new AutocadStagingResponsibility
            {
                Owner = hasBoundary ? "Other" : "LRDU",
                Note = hasBoundary ? "Out-of-scope owner is not identified by ElectricalProject." : null
            },
            Nodes = nodes.Values.OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray(),
            Segments = segments.OrderBy(segment => segment.SegmentId, StringComparer.Ordinal).ToArray(),
            Shield = shieldRoute,
            PowerFlowOrientation = powerFlow
        };
    }

    private static AutocadStagingNode AddNode(
        IDictionary<string, AutocadStagingNode> nodes,
        StagingEndpoint endpoint,
        IReadOnlyDictionary<string, AutocadConnectionPointBinding> bindings,
        IReadOnlyDictionary<string, AutocadComponentDrawingRoleEvidence> roleEvidence)
    {
        var nodeId = $"node:{endpoint.EndpointId}";
        if (nodes.TryGetValue(nodeId, out var existing)) return existing;
        roleEvidence.TryGetValue(endpoint.ComponentInstanceId ?? string.Empty, out var explicitRole);
        var role = endpoint.IsTerminal ? ComponentDrawingRole.TransparentTerminal : explicitRole?.Role ?? ComponentDrawingRole.Unknown;
        var roleStatus = endpoint.IsTerminal
            ? DrawingEvidenceStatus.Confirmed
            : role == ComponentDrawingRole.Unknown
                ? DrawingEvidenceStatus.Unknown
                : explicitRole?.Status ?? DrawingEvidenceStatus.Unknown;
        return nodes[nodeId] = new AutocadStagingNode
        {
            NodeId = nodeId,
            Kind = endpoint.IsConnectorPin ? "ConnectorPin" : endpoint.IsTerminal ? "Terminal" : "ComponentPin",
            ComponentInstanceId = endpoint.ComponentInstanceId,
            ComponentDefinitionId = endpoint.ComponentDefinitionId,
            ComponentTypeKey = endpoint.ComponentTypeKey,
            ComponentDisplayName = endpoint.ComponentDisplayName,
            DrawingRole = role,
            DrawingRoleEvidenceStatus = roleStatus,
            DrawingRoleEvidenceSource = endpoint.IsTerminal ? "ElectricalProject.TerminalBlock" : explicitRole?.EvidenceSource,
            RoleInterventionId = roleStatus == DrawingEvidenceStatus.Confirmed ? null : $"component-role:{endpoint.ComponentInstanceId}",
            PinId = endpoint.EndpointId,
            PinNumber = endpoint.Pin?.PinNumber,
            PinName = endpoint.Pin?.PinName,
            PortName = endpoint.Port?.Name,
            ConnectionPoint = bindings.TryGetValue(endpoint.EndpointId, out var binding) &&
                              !string.IsNullOrWhiteSpace(binding.SymbolKey) &&
                              !string.IsNullOrWhiteSpace(binding.ConnectionPointId)
                ? new AutocadStagingConnectionPoint
                {
                    SymbolKey = binding.SymbolKey,
                    ConnectionPointId = binding.ConnectionPointId
                }
                : null,
            SignalCode = endpoint.TopologySignal
        };
    }

    private static bool AddResponsibilityBoundaryRoute(
        IDictionary<string, AutocadStagingNode> nodes,
        ICollection<AutocadStagingSegment> segments,
        ElectricalConnection connection,
        StagingEndpoint from,
        StagingEndpoint to,
        IReadOnlyDictionary<string, AutocadConnectionPointBinding> bindings,
        IReadOnlyDictionary<string, AutocadComponentDrawingRoleEvidence> roleEvidence,
        string signalCode,
        ICollection<AutocadStagingIntervention> interventions)
    {
        var inScope = IsOutOfScope(from) ? to : from;
        var boundary = IsOutOfScope(from) ? from : to;
        if (!CanDrawFromEndpoint(inScope, bindings)) return false;

        var inScopeNode = AddNode(nodes, inScope, bindings, roleEvidence);
        var boundaryNode = AddBoundaryNode(nodes, boundary, signalCode);
        var unknownNode = AddUnknownBoundaryNode(nodes, connection.ConnectionId, signalCode);
        var interventionId = $"responsibility:{connection.ConnectionId}";
        interventions.Add(new AutocadStagingIntervention
        {
            InterventionId = interventionId,
            Category = "responsibility",
            Message = $"Topology stops at out-of-scope endpoint '{boundary.EndpointId}'; no downstream wiring was derived.",
            DrawingMayContinue = true
        });
        segments.Add(new AutocadStagingSegment
        {
            SegmentId = $"segment:{connection.ConnectionId}:confirmed",
            Kind = "InternalWire",
            FromNodeId = inScopeNode.NodeId,
            ToNodeId = boundaryNode.NodeId,
            TopologyStatus = "Confirmed",
            ProcurementStatus = "NotApplicable",
            DrawingRepresentation = "SourceArrow",
            SignalCode = signalCode,
            BomRequired = false,
            InstalledLengthStatus = "NotApplicable"
        });
        segments.Add(new AutocadStagingSegment
        {
            SegmentId = $"segment:{connection.ConnectionId}:unknown",
            Kind = "UnknownFieldSegment",
            FromNodeId = boundaryNode.NodeId,
            ToNodeId = unknownNode.NodeId,
            TopologyStatus = "Unknown",
            ProcurementStatus = "NotApplicable",
            DrawingRepresentation = "NotDrawn",
            BomRequired = false,
            InstalledLengthStatus = "NotApplicable",
            InterventionId = interventionId
        });
        return true;
    }

    private static AutocadStagingNode AddConnectionBoundaryNode(
        IDictionary<string, AutocadStagingNode> nodes,
        string connectionId,
        string side,
        string endpointId,
        string signalCode,
        string description)
    {
        var nodeId = $"boundary:{connectionId}:{side}";
        if (nodes.TryGetValue(nodeId, out var existing)) return existing;
        return nodes[nodeId] = new AutocadStagingNode
        {
            NodeId = nodeId,
            Kind = "FieldBoundary",
            SignalCode = signalCode,
            Description = description
        };
    }

    private static AutocadStagingNode AddBoundaryNode(
        IDictionary<string, AutocadStagingNode> nodes,
        StagingEndpoint endpoint,
        string signalCode)
    {
        var nodeId = $"boundary:{endpoint.EndpointId}";
        if (nodes.TryGetValue(nodeId, out var existing)) return existing;
        return nodes[nodeId] = new AutocadStagingNode
        {
            NodeId = nodeId,
            Kind = "FieldBoundary",
            SignalCode = signalCode,
            Description = $"Out-of-scope boundary at '{endpoint.EndpointId}'."
        };
    }

    private static AutocadStagingNode AddUnknownBoundaryNode(
        IDictionary<string, AutocadStagingNode> nodes,
        string connectionId,
        string signalCode)
    {
        var nodeId = $"boundary:unknown:{connectionId}";
        if (nodes.TryGetValue(nodeId, out var existing)) return existing;
        return nodes[nodeId] = new AutocadStagingNode
        {
            NodeId = nodeId,
            Kind = "FieldBoundary",
            SignalCode = $"{signalCode}-UNKNOWN",
            Description = "Undrawn unknown field path beyond the responsibility boundary."
        };
    }

    private static IReadOnlyDictionary<string, AutocadComponentDrawingRoleEvidence> ResolveRoleEvidence(
        ElectricalProject project,
        AutocadEngineeringDrawingEvidence evidence,
        AutocadExportPreflightRequest request)
    {
        var result = new Dictionary<string, AutocadComponentDrawingRoleEvidence>(StringComparer.OrdinalIgnoreCase);
        var componentIds = project.Components.Select(component => component.ComponentInstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in evidence.ComponentRoles.GroupBy(item => item.ComponentInstanceId, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.DuplicateComponentDrawingRole,
                    $"Component drawing role identity '{group.Key}' is missing or not unique.", group.Key));
                continue;
            }
            if (!componentIds.Contains(group.Key))
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.UnknownComponentDrawingRole,
                    $"Drawing role evidence references unknown component '{group.Key}'.", group.Key));
                continue;
            }

            var item = group.Single();
            result[group.Key] = item.Role == ComponentDrawingRole.Unknown
                ? item with { Status = DrawingEvidenceStatus.Unknown }
                : item;
        }

        foreach (var component in project.Components)
        {
            if (!result.TryGetValue(component.ComponentInstanceId, out var item) ||
                item.Role == ComponentDrawingRole.Unknown || item.Status != DrawingEvidenceStatus.Confirmed)
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.UnknownComponentDrawingRole,
                    $"Component '{component.ComponentInstanceId}' has no confirmed drawing role; TypeKey '{component.TypeKey}' was not used as role evidence.",
                    component.ComponentInstanceId));
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, AutocadPowerFlowOrientationEvidence> ResolvePowerEvidence(
        AutocadEngineeringDrawingEvidence evidence,
        AutocadExportPreflightRequest request,
        IReadOnlySet<string> netIdentities)
    {
        var result = new Dictionary<string, AutocadPowerFlowOrientationEvidence>(StringComparer.Ordinal);
        foreach (var group in evidence.PowerFlowOrientations.GroupBy(item => item.NetIdentity, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidPowerFlowOrientationEvidence,
                    $"Power-flow evidence identity '{group.Key}' is missing or not unique.", group.Key));
                continue;
            }
            if (!netIdentities.Contains(group.Key))
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidPowerFlowOrientationEvidence,
                    $"Power-flow evidence references unknown net identity '{group.Key}'.", group.Key));
                continue;
            }
            result[group.Key] = group.Single();
        }
        return result;
    }

    private static IReadOnlyDictionary<string, AutocadCableInstanceOverride> ResolveCableOverrides(
        ElectricalProject project,
        AutocadEngineeringDrawingEvidence evidence,
        AutocadExportPreflightRequest request)
    {
        var result = new Dictionary<string, AutocadCableInstanceOverride>(StringComparer.OrdinalIgnoreCase);
        var cableIds = project.Cables.Select(cable => cable.CableInstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in evidence.CableInstanceOverrides.GroupBy(item => item.CableInstanceId, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.DuplicateCableInstanceOverride,
                    $"Cable instance override identity '{group.Key}' is missing or not unique.", group.Key));
                continue;
            }
            if (!cableIds.Contains(group.Key))
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidCableInstanceOverride,
                    $"Cable instance override references unknown cable '{group.Key}'.", group.Key));
                continue;
            }
            result[group.Key] = group.Single();
        }
        return result;
    }

    private static void ValidateCrossPageContinuations(
        ElectricalProject project,
        AutocadEngineeringDrawingEvidence evidence,
        AutocadExportPreflightRequest request)
    {
        var endpointIds = project.Components.SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins.Select(pin => pin.PinId).Append(port.PortId))
            .Concat(project.TerminalBlocks.SelectMany(block => block.Positions)
                .SelectMany(position => position.Levels)
                .SelectMany(level => level.ConnectionPoints)
                .Select(point => point.ConnectionPointId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in evidence.CrossPageContinuations.GroupBy(item => item.PairIdentity, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidCrossPageContinuation,
                    $"Cross-page continuation pair identity '{group.Key}' is missing or not unique.", group.Key));
                continue;
            }
            var item = group.Single();
            if (item.Status != DrawingEvidenceStatus.Confirmed ||
                string.IsNullOrWhiteSpace(item.SourcePageId) || string.IsNullOrWhiteSpace(item.DestinationPageId) ||
                string.IsNullOrWhiteSpace(item.SourceEndpointId) || string.IsNullOrWhiteSpace(item.DestinationEndpointId) ||
                string.Equals(item.SourcePageId, item.DestinationPageId, StringComparison.OrdinalIgnoreCase))
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidCrossPageContinuation,
                    $"Cross-page continuation '{item.PairIdentity}' must explicitly identify distinct source/destination pages and endpoints.",
                    item.PairIdentity));
            }
            else if (!endpointIds.Contains(item.SourceEndpointId) || !endpointIds.Contains(item.DestinationEndpointId))
            {
                request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidCrossPageContinuation,
                    $"Cross-page continuation '{item.PairIdentity}' references an unknown source or destination endpoint.",
                    item.PairIdentity, item.SourceEndpointId, item.DestinationEndpointId));
            }
        }
    }

    private static void ValidatePageArchetypeHint(
        AutocadEngineeringDrawingEvidence evidence,
        AutocadExportPreflightRequest request)
    {
        var hint = evidence.PageArchetypeHint;
        if (hint is null) return;
        if (hint.Archetype == DrawingPageArchetype.Unknown || hint.Status != DrawingEvidenceStatus.Confirmed)
            request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.InvalidPageArchetypeEvidence,
                "Page archetype evidence must identify a known archetype with engineer-confirmed status."));
    }

    private static IReadOnlyList<AutocadStagingTerminalContinuity> BuildTerminalContinuities(
        ElectricalProject project,
        AutocadExportPreflightRequest request)
    {
        var result = new List<AutocadStagingTerminalContinuity>();
        foreach (var block in project.TerminalBlocks)
        foreach (var position in block.Positions)
        foreach (var level in position.Levels)
        {
            var pointIds = level.ConnectionPoints.Select(point => point.ConnectionPointId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var continuity in level.InternalConnections)
            {
                if (!pointIds.Contains(continuity.FromConnectionPointId) || !pointIds.Contains(continuity.ToConnectionPointId))
                {
                    request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.TopologyDiscontinuity,
                        $"Terminal continuity in '{block.TerminalBlockId}/{level.LevelId}' references a missing connection point.",
                        block.TerminalBlockId, continuity.FromConnectionPointId, continuity.ToConnectionPointId));
                    continue;
                }
                var pair = new[] { continuity.FromConnectionPointId, continuity.ToConnectionPointId }
                    .OrderBy(id => id, StringComparer.Ordinal).ToArray();
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    string.Join("|", block.TerminalBlockId, position.TerminalPositionId, level.LevelId, pair[0], pair[1]))))[..16];
                result.Add(new AutocadStagingTerminalContinuity
                {
                    ContinuityId = $"terminal-continuity:{hash}",
                    TerminalBlockId = block.TerminalBlockId,
                    TerminalPositionId = position.TerminalPositionId,
                    LevelId = level.LevelId,
                    FromConnectionPointId = continuity.FromConnectionPointId,
                    ToConnectionPointId = continuity.ToConnectionPointId
                });
            }
        }
        return result.OrderBy(item => item.ContinuityId, StringComparer.Ordinal).ToArray();
    }

    private static void AddRoleInterventions(
        ElectricalProject project,
        IReadOnlyDictionary<string, AutocadComponentDrawingRoleEvidence> roleEvidence,
        ICollection<AutocadStagingIntervention> interventions)
    {
        foreach (var component in project.Components)
        {
            if (roleEvidence.TryGetValue(component.ComponentInstanceId, out var item) &&
                item.Role != ComponentDrawingRole.Unknown && item.Status == DrawingEvidenceStatus.Confirmed) continue;
            interventions.Add(new AutocadStagingIntervention
            {
                InterventionId = $"component-role:{component.ComponentInstanceId}",
                Category = "component-role",
                Message = $"Drawing role for component '{component.ComponentInstanceId}' requires explicit engineering confirmation; TypeKey was not used.",
                DrawingMayContinue = false
            });
        }
    }

    private static AutocadStagingPageArchetypeHint ResolvePageArchetypeHint(AutocadPageArchetypeHint? hint)
    {
        var confirmed = hint is not null && hint.Archetype != DrawingPageArchetype.Unknown &&
                        hint.Status == DrawingEvidenceStatus.Confirmed;
        return new AutocadStagingPageArchetypeHint
        {
            Archetype = hint?.Archetype ?? DrawingPageArchetype.Unknown,
            EvidenceStatus = confirmed
                ? DrawingEvidenceStatus.Confirmed
                : hint?.Archetype == DrawingPageArchetype.Unknown
                    ? DrawingEvidenceStatus.Unknown
                    : hint?.Status ?? DrawingEvidenceStatus.Unknown,
            EvidenceSource = hint?.EvidenceSource
        };
    }

    private static AutocadStagingCrossPageContinuation ToContract(AutocadCrossPageContinuationEvidence item) => new()
    {
        PairIdentity = item.PairIdentity,
        SourceEndpointId = item.SourceEndpointId,
        DestinationEndpointId = item.DestinationEndpointId,
        SourcePageId = item.SourcePageId,
        DestinationPageId = item.DestinationPageId,
        EvidenceStatus = item.Status,
        EvidenceSource = item.EvidenceSource
    };

    private static bool IsPowerRoute(ElectricalProject project, IEnumerable<ElectricalConnection> connections)
    {
        foreach (var connection in connections)
        {
            if (project.Nets.FirstOrDefault(net => string.Equals(net.NetId, connection.NetId, StringComparison.OrdinalIgnoreCase))?.Layer == ElectricalLayer.Power)
                return true;
            var from = Resolve(project, connection.FromEndpointId, new Dictionary<string, StagingEndpoint?>(StringComparer.OrdinalIgnoreCase));
            var to = Resolve(project, connection.ToEndpointId, new Dictionary<string, StagingEndpoint?>(StringComparer.OrdinalIgnoreCase));
            if (from?.Pin?.Layer == ElectricalLayer.Power || to?.Pin?.Layer == ElectricalLayer.Power ||
                from?.Pin?.Power is not null || to?.Pin?.Power is not null) return true;
        }
        return false;
    }

    private static bool IsConfirmed(
        AutocadPowerFlowOrientationEvidence evidence,
        IEnumerable<ElectricalConnection>? connections = null)
    {
        if (evidence.Status != DrawingEvidenceStatus.Confirmed || evidence.Orientation == PowerFlowOrientation.Unknown ||
            string.IsNullOrWhiteSpace(evidence.SourceEndpointId) || string.IsNullOrWhiteSpace(evidence.DestinationEndpointId) ||
            string.Equals(evidence.SourceEndpointId, evidence.DestinationEndpointId, StringComparison.OrdinalIgnoreCase)) return false;
        if (connections is null) return true;
        var endpoints = connections.SelectMany(connection => new[] { connection.FromEndpointId, connection.ToEndpointId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return endpoints.Contains(evidence.SourceEndpointId) && endpoints.Contains(evidence.DestinationEndpointId);
    }

    private static void AddEndpointPreflight(
        AutocadExportPreflightRequest request,
        string sourceEndpointId,
        StagingEndpoint? endpoint,
        string netIdentity,
        IReadOnlyDictionary<string, AutocadConnectionPointBinding> bindings,
        bool crossesResponsibilityBoundary,
        bool allowsFieldBoundary)
    {
        if (request.ConfirmedEndpoints.Any(item => string.Equals(item.EndpointId, sourceEndpointId, StringComparison.OrdinalIgnoreCase))) return;
        if (endpoint is null)
        {
            request.ConfirmedEndpoints.Add(new AutocadExportPreflightEndpoint
            {
                EndpointId = sourceEndpointId,
                MachineNetIdentity = netIdentity,
                IsResolved = false,
                HasSymbolConnectionPoint = false
            });
            return;
        }

        if (crossesResponsibilityBoundary && IsOutOfScope(endpoint)) return;
        var hasBinding = bindings.TryGetValue(endpoint.EndpointId, out var binding) &&
                         !string.IsNullOrWhiteSpace(binding.SymbolKey) &&
                         !string.IsNullOrWhiteSpace(binding.ConnectionPointId);
        request.ConfirmedEndpoints.Add(new AutocadExportPreflightEndpoint
        {
            EndpointId = endpoint.EndpointId,
            MachineNetIdentity = netIdentity,
            TopologySignal = endpoint.TopologySignal,
            TopologyPotential = endpoint.TopologyPotential,
            IsResolved = endpoint.IsPinLevel,
            HasSymbolConnectionPoint = hasBinding,
            AllowsFieldBoundary = allowsFieldBoundary
        });
        if (endpoint.IsPortLevel)
        {
            request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.PortLevelEndpoint,
                $"Endpoint '{endpoint.EndpointId}' is port-level; no pin or core was selected automatically.", endpoint.EndpointId));
        }
    }

    private static FieldBoundaryReason? ResolveFieldBoundaryReason(
        ElectricalProject project,
        ElectricalConnection connection,
        StagingEndpoint? from,
        StagingEndpoint? to,
        AutocadExportPreflightRequest request)
    {
        if (from?.IsPortLevel == true || to?.IsPortLevel == true) return FieldBoundaryReason.PortLevel;
        if (connection.Kind != ConnectionKind.Cable) return null;

        var coreEvidence = GetCableCoreEvidence(project, connection);
        if (coreEvidence == CableCoreEvidence.Proven) return null;
        if (coreEvidence == CableCoreEvidence.Contradictory)
        {
            request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.TopologyDiscontinuity,
                $"Cable connection '{connection.ConnectionId}' has contradictory cable/core evidence and cannot be represented.", connection.ConnectionId));
            return null;
        }
        if (coreEvidence == CableCoreEvidence.MissingCableInstance)
        {
            request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.TopologyDiscontinuity,
                $"Cable connection '{connection.ConnectionId}' references a missing cable instance '{connection.CableInstanceId}'.", connection.ConnectionId));
            return null;
        }

        request.AdditionalIssues.Add(Error(AutocadExportPreflightIssueCode.UnprovenPhysicalSegment,
            $"Cable connection '{connection.ConnectionId}' has no exact cable/core endpoint evidence; it is retained as an undrawn field boundary.",
            connection.ConnectionId));
        return FieldBoundaryReason.UnprovenCableCore;
    }

    private static bool CanDrawFromEndpoint(
        StagingEndpoint? endpoint,
        IReadOnlyDictionary<string, AutocadConnectionPointBinding> bindings) => endpoint is not null && endpoint.IsPinLevel &&
            bindings.TryGetValue(endpoint.EndpointId, out var binding) &&
            !string.IsNullOrWhiteSpace(binding.SymbolKey) && !string.IsNullOrWhiteSpace(binding.ConnectionPointId);

    private static CableInstance? FindCable(ElectricalProject project, string? cableInstanceId) => project.Cables
        .SingleOrDefault(item => string.Equals(item.CableInstanceId, cableInstanceId, StringComparison.OrdinalIgnoreCase));

    private static CableCoreEvidence GetCableCoreEvidence(ElectricalProject project, ElectricalConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.CableInstanceId) || string.IsNullOrWhiteSpace(connection.CableCoreId))
            return CableCoreEvidence.Unproven;
        var cables = project.Cables.Where(item => string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cables.Length == 0) return CableCoreEvidence.MissingCableInstance;
        if (cables.Length > 1) return CableCoreEvidence.Contradictory;
        var cores = cables[0].CoreAssignments.Where(item => string.Equals(item.CoreId, connection.CableCoreId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cores.Length > 1) return CableCoreEvidence.Contradictory;
        var core = cores.SingleOrDefault();
        return core is not null && EndpointsMatch(core.FromEndpointId, core.ToEndpointId, connection.FromEndpointId, connection.ToEndpointId) &&
               !string.Equals(core.Status, "UNUSED", StringComparison.OrdinalIgnoreCase)
            ? CableCoreEvidence.Proven
            : CableCoreEvidence.Unproven;
    }

    private static string BoundaryCategory(FieldBoundaryReason reason) => reason switch
    {
        FieldBoundaryReason.PortLevel => "port-level",
        FieldBoundaryReason.UnprovenCableCore => "cable-core",
        FieldBoundaryReason.Responsibility => "responsibility",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    private static string BoundaryMessage(FieldBoundaryReason reason) => reason switch
    {
        FieldBoundaryReason.PortLevel => "Port-level topology has no selected pin or core.",
        FieldBoundaryReason.UnprovenCableCore => "Cable core mapping is not proven.",
        FieldBoundaryReason.Responsibility => "The route crosses an out-of-scope responsibility boundary.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    private static bool EndpointsMatch(string? firstFrom, string? firstTo, string secondFrom, string secondTo) =>
        string.Equals(firstFrom, secondFrom, StringComparison.OrdinalIgnoreCase) && string.Equals(firstTo, secondTo, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(firstFrom, secondTo, StringComparison.OrdinalIgnoreCase) && string.Equals(firstTo, secondFrom, StringComparison.OrdinalIgnoreCase);

    private static StagingEndpoint? Resolve(ElectricalProject project, string endpointId, IDictionary<string, StagingEndpoint?> cache)
    {
        if (cache.TryGetValue(endpointId, out var cached)) return cached;
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return cache[endpointId] = new StagingEndpoint(endpointId, component.ComponentInstanceId,
                    component.ComponentDefinitionId, component.TypeKey, component.DisplayName, port, null, component.ResponsibilityScope);
            var pin = port.Pins.FirstOrDefault(item => string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null)
                return cache[endpointId] = new StagingEndpoint(endpointId, component.ComponentInstanceId,
                    component.ComponentDefinitionId, component.TypeKey, component.DisplayName, port, pin, component.ResponsibilityScope);
        }
        foreach (var block in project.TerminalBlocks)
        foreach (var point in block.Positions.SelectMany(position => position.Levels).SelectMany(level => level.ConnectionPoints))
            if (string.Equals(point.ConnectionPointId, endpointId, StringComparison.OrdinalIgnoreCase))
                return cache[endpointId] = new StagingEndpoint(endpointId, block.TerminalBlockId,
                    null, null, null, null, null, ResponsibilityScope.InScope, true);
        return cache[endpointId] = null;
    }

    private static bool IsOutOfScope(StagingEndpoint? endpoint) => endpoint?.ResponsibilityScope == ResponsibilityScope.OutOfScope;

    private static AutocadExportPreflightIssue Error(AutocadExportPreflightIssueCode code, string message, params string[] ids) => new()
    {
        Code = code,
        Severity = AutocadExportPreflightSeverity.Error,
        Message = message,
        SourceObjectIds = ids
    };

    private enum FieldBoundaryReason
    {
        PortLevel,
        UnprovenCableCore,
        Responsibility
    }

    private enum CableCoreEvidence
    {
        Proven,
        Unproven,
        MissingCableInstance,
        Contradictory
    }

    private sealed record StagingEndpoint(
        string EndpointId,
        string? ComponentInstanceId,
        string? ComponentDefinitionId,
        string? ComponentTypeKey,
        string? ComponentDisplayName,
        ComponentPort? Port,
        ComponentPin? Pin,
        ResponsibilityScope ResponsibilityScope,
        bool IsTerminal = false)
    {
        public bool IsPortLevel => Port is not null && Pin is null;
        public bool IsPinLevel => Pin is not null || IsTerminal;
        public bool IsConnectorPin => Port?.Connector is not null && Pin is not null;
        public bool IsShield => Pin?.GroundReferenceType == GroundReferenceType.Shield;
        public string? TopologySignal => Pin?.Function ?? Pin?.PinName ?? Pin?.SignalStandardRaw;
        public string? TopologyPotential => Pin?.Power?.Polarity switch
        {
            Polarity.Positive => "+V",
            Polarity.Negative or Polarity.Return => "0V",
            _ => null
        };
    }
}

public sealed record AutocadConnectionPointBinding
{
    public required string EndpointId { get; init; }
    public required string SymbolKey { get; init; }
    public required string ConnectionPointId { get; init; }
}

public sealed record AutocadStagingGraphPreparationResult
{
    public required AutocadExportPreflightReport Preflight { get; init; }
    public AutocadStagingGraphContract? Graph { get; init; }
}

public sealed record AutocadStagingGraphContract
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = "lrdu-staging-route.v1";
    [JsonPropertyName("projectId")] public required string ProjectId { get; init; }
    [JsonPropertyName("exportMode")] public string ExportMode { get; init; } = "ValidateOnly";
    [JsonPropertyName("pageArchetypeHint")] public AutocadStagingPageArchetypeHint PageArchetypeHint { get; init; } = new();
    [JsonPropertyName("routes")] public required IReadOnlyList<AutocadStagingRoute> Routes { get; init; }
    [JsonPropertyName("cableFamilies")] public IReadOnlyList<CableFamilySignature> CableFamilies { get; init; } = [];
    [JsonPropertyName("cableInstances")] public IReadOnlyList<AutocadStagingCableInstance> CableInstances { get; init; } = [];
    [JsonPropertyName("terminalContinuities")] public IReadOnlyList<AutocadStagingTerminalContinuity> TerminalContinuities { get; init; } = [];
    [JsonPropertyName("crossPageContinuations")] public IReadOnlyList<AutocadStagingCrossPageContinuation> CrossPageContinuations { get; init; } = [];
    [JsonPropertyName("interventions")] public required IReadOnlyList<AutocadStagingIntervention> Interventions { get; init; }
    [JsonPropertyName("writerInterface")] public AutocadStagingWriterInterface WriterInterface { get; init; } = new();
}

public sealed record AutocadStagingRoute
{
    [JsonPropertyName("routeId")] public required string RouteId { get; init; }
    [JsonPropertyName("netIdentity")] public required string NetIdentity { get; init; }
    [JsonPropertyName("visibleLabel")] public required string VisibleLabel { get; init; }
    [JsonPropertyName("topologyStatus")] public required string TopologyStatus { get; init; }
    [JsonPropertyName("responsibility")] public required AutocadStagingResponsibility Responsibility { get; init; }
    [JsonPropertyName("nodes")] public required IReadOnlyList<AutocadStagingNode> Nodes { get; init; }
    [JsonPropertyName("segments")] public required IReadOnlyList<AutocadStagingSegment> Segments { get; init; }
    [JsonPropertyName("shield")] public required AutocadStagingShieldRoute Shield { get; init; }
    [JsonPropertyName("powerFlowOrientation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AutocadStagingPowerFlowOrientation? PowerFlowOrientation { get; init; }
}

public sealed record AutocadStagingNode
{
    [JsonPropertyName("nodeId")] public required string NodeId { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("componentInstanceId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ComponentInstanceId { get; init; }
    [JsonPropertyName("componentDefinitionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ComponentDefinitionId { get; init; }
    [JsonPropertyName("componentTypeKey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ComponentTypeKey { get; init; }
    [JsonPropertyName("componentDisplayName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ComponentDisplayName { get; init; }
    [JsonPropertyName("drawingRole")] public ComponentDrawingRole DrawingRole { get; init; } = ComponentDrawingRole.Unknown;
    [JsonPropertyName("drawingRoleEvidenceStatus")] public DrawingEvidenceStatus DrawingRoleEvidenceStatus { get; init; } = DrawingEvidenceStatus.Unknown;
    [JsonPropertyName("drawingRoleEvidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DrawingRoleEvidenceSource { get; init; }
    [JsonPropertyName("roleInterventionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RoleInterventionId { get; init; }
    [JsonPropertyName("pinId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PinId { get; init; }
    [JsonPropertyName("pinNumber"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PinNumber { get; init; }
    [JsonPropertyName("pinName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PinName { get; init; }
    [JsonPropertyName("portName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PortName { get; init; }
    [JsonPropertyName("connectionPoint"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AutocadStagingConnectionPoint? ConnectionPoint { get; init; }
    [JsonPropertyName("signalCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SignalCode { get; init; }
    [JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
}

public sealed record AutocadStagingConnectionPoint
{
    [JsonPropertyName("symbolKey")] public required string SymbolKey { get; init; }
    [JsonPropertyName("connectionPointId")] public required string ConnectionPointId { get; init; }
}

public sealed record AutocadStagingSegment
{
    [JsonPropertyName("segmentId")] public required string SegmentId { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("fromNodeId")] public required string FromNodeId { get; init; }
    [JsonPropertyName("toNodeId")] public required string ToNodeId { get; init; }
    [JsonPropertyName("topologyStatus")] public required string TopologyStatus { get; init; }
    [JsonPropertyName("procurementStatus")] public required string ProcurementStatus { get; init; }
    [JsonPropertyName("drawingRepresentation")] public required string DrawingRepresentation { get; init; }
    [JsonPropertyName("signalCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SignalCode { get; init; }
    [JsonPropertyName("bomRequired")] public required bool BomRequired { get; init; }
    [JsonPropertyName("bomItemId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BomItemId { get; init; }
    [JsonPropertyName("installedLengthStatus"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InstalledLengthStatus { get; init; }
    [JsonPropertyName("cableInstanceId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CableInstanceId { get; init; }
    [JsonPropertyName("cableFamilyId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CableFamilyId { get; init; }
    [JsonPropertyName("interventionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InterventionId { get; init; }
}

public sealed record AutocadStagingResponsibility
{
    [JsonPropertyName("owner")] public required string Owner { get; init; }
    [JsonPropertyName("note"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }
}

public sealed record AutocadStagingShieldRoute
{
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("originNodeId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OriginNodeId { get; init; }
    [JsonPropertyName("pathSegmentIds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? PathSegmentIds { get; init; }
    [JsonPropertyName("terminationNodeId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TerminationNodeId { get; init; }
    [JsonPropertyName("groundingStrategy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GroundingStrategy { get; init; }
    [JsonPropertyName("interventionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InterventionId { get; init; }
}

public sealed record AutocadStagingIntervention
{
    [JsonPropertyName("interventionId")] public required string InterventionId { get; init; }
    [JsonPropertyName("category")] public required string Category { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("drawingMayContinue")] public required bool DrawingMayContinue { get; init; }
}

public sealed record AutocadStagingWriterInterface
{
    [JsonPropertyName("execution")] public string Execution { get; init; } = "NotRun";
    [JsonPropertyName("formalWriterAuthorized")] public bool FormalWriterAuthorized { get; init; }
    [JsonPropertyName("commandContract")] public string CommandContract { get; init; } = "LRDU staging graph is validate-only; no writer invocation is authorized.";
}
