using System.Security.Cryptography;
using System.Text;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ProjectPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

namespace ComponentIntelligence.Electrical.Drawing;

// Presentation grouping from explicit catalog classification and exact project endpoints.
// This projection never writes electrical graph, net, bus or domain identities back.
internal static class DrawingGroupingEvidence
{
    public static DrawingPlanningInput Apply(ElectricalProject project, IReadOnlyList<ComponentIR>? catalog, DrawingPlanningInput input)
    {
        var endpoints = new Dictionary<string, (ComponentInstance Component, ProjectPort Port)>(StringComparer.Ordinal);
        var duplicateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            if (!endpoints.TryAdd(port.PortId, (component, port))) duplicateIds.Add(port.PortId);
            foreach (var pin in port.Pins)
                if (!endpoints.TryAdd(pin.PinId, (component, port))) duplicateIds.Add(pin.PinId);
        }
        if (duplicateIds.Count > 0)
            return input with { Issues = input.Issues.Concat(duplicateIds.Order(StringComparer.Ordinal).Select(id => new DrawingPlanningIssue
            {
                IssueId = $"ISSUE:{id}:grouping-duplicate-endpoint", Code = "DRAWING_GROUPING_ENDPOINT_AMBIGUOUS",
                Severity = DrawingPlanningIssueSeverity.Blocker, Message = "Duplicate endpoint identity prevents authoritative grouping.",
                TargetKind = "Endpoint", TargetId = id
            })).ToList() };
        var categories = project.Components.ToDictionary(c => c.ComponentInstanceId,
            c => catalog?.SingleOrDefault(d => d.Identity.ComponentId == c.ComponentDefinitionId)?.Classification.Category,
            StringComparer.Ordinal);
        var masters = categories.Where(p => p.Value == "IO-Link Master").Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var moduleCandidates = project.Components.ToDictionary(c => c.ComponentInstanceId, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var master in masters) moduleCandidates[master].Add(master);
        foreach (var connection in project.Connections)
        {
            if (!endpoints.TryGetValue(connection.FromEndpointId, out var a) || !endpoints.TryGetValue(connection.ToEndpointId, out var b)) continue;
            if (a.Port.Protocol != "IO-Link" || b.Port.Protocol != "IO-Link") continue;
            if (masters.Contains(a.Component.ComponentInstanceId)) moduleCandidates[b.Component.ComponentInstanceId].Add(a.Component.ComponentInstanceId);
            if (masters.Contains(b.Component.ComponentInstanceId)) moduleCandidates[a.Component.ComponentInstanceId].Add(b.Component.ComponentInstanceId);
        }
        var modules = moduleCandidates.Where(p => p.Value.Count == 1).ToDictionary(p => p.Key, p => p.Value.Single(), StringComparer.Ordinal);
        var issues = input.Issues.ToList();
        foreach (var conflict in moduleCandidates.Where(p => p.Value.Count > 1))
            issues.Add(new() { IssueId = $"ISSUE:{conflict.Key}:module-ownership", Code = "DRAWING_MODULE_OWNERSHIP_AMBIGUOUS",
                Severity = DrawingPlanningIssueSeverity.Warning, Message = "Multiple explicit master attachments; module ownership is not selected automatically.",
                TargetKind = "Component", TargetId = conflict.Key });

        string[] RepresentationIds(IEnumerable<string> endpointIds) => endpointIds.Where(endpoints.ContainsKey)
            .Select(e => $"REP:{endpoints[e].Component.ComponentInstanceId}:Schematic")
            .Where(id => input.Representations.Any(r => r.RepresentationId == id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var networks = input.Networks.Select(n => n with
        {
            RepresentationIds = RepresentationIds(project.Connections.Where(c => n.ConnectionIds.Contains(c.ConnectionId))
                .SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId }))
        }).ToList();
        var assignedNetworkConnections = networks.SelectMany(n => n.ConnectionIds).ToHashSet(StringComparer.Ordinal);

        // No electrical Bus exists in many legacy projects. These are explicitly named
        // layout groups of already-connected, same-protocol interfaces, not new buses.
        var protocolEdges = project.Connections.Where(c => !assignedNetworkConnections.Contains(c.ConnectionId))
            .Where(c => endpoints.ContainsKey(c.FromEndpointId) && endpoints.ContainsKey(c.ToEndpointId))
            .Where(c => !string.IsNullOrWhiteSpace(endpoints[c.FromEndpointId].Port.Protocol)
                && endpoints[c.FromEndpointId].Port.Protocol == endpoints[c.ToEndpointId].Port.Protocol)
            .GroupBy(c => endpoints[c.FromEndpointId].Port.Protocol!, StringComparer.Ordinal);
        foreach (var protocol in protocolEdges.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string Vertex(string endpoint)
            {
                var value = endpoints[endpoint];
                var category = categories[value.Component.ComponentInstanceId];
                var isHub = (protocol.Key == "Ethernet" && category == "Industrial Ethernet Switch")
                    || (protocol.Key == "IO-Link" && category == "IO-Link Master");
                return isHub ? "COMPONENT:" + value.Component.ComponentInstanceId : "PORT:" + value.Port.PortId;
            }
            var remaining = protocol.OrderBy(c => c.ConnectionId, StringComparer.Ordinal).ToList();
            while (remaining.Count > 0)
            {
                var group = new List<ElectricalConnection> { remaining[0] }; remaining.RemoveAt(0);
                var componentIds = group.SelectMany(c => new[] { Vertex(c.FromEndpointId), Vertex(c.ToEndpointId) }).ToHashSet(StringComparer.Ordinal);
                bool changed;
                do
                {
                    changed = false;
                    foreach (var c in remaining.ToArray())
                    {
                        var a = Vertex(c.FromEndpointId);
                        var b = Vertex(c.ToEndpointId);
                        if (!componentIds.Contains(a) && !componentIds.Contains(b)) continue;
                        group.Add(c); remaining.Remove(c); componentIds.Add(a); componentIds.Add(b); changed = true;
                    }
                } while (changed);
                var ids = group.Select(c => c.ConnectionId).Order(StringComparer.Ordinal).ToArray();
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(ids))));
                networks.Add(new() { NetworkId = "LAYOUT-NETWORK:" + hash, NetworkKind = protocol.Key, ConnectionIds = ids,
                    RepresentationIds = RepresentationIds(group.SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId })) });
            }
        }
        var networkByConnection = networks.SelectMany(n => n.ConnectionIds.Select(c => (c, n)))
            .GroupBy(p => p.c, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().n, StringComparer.Ordinal);
        var cableModules = project.Connections.Where(c => c.CableInstanceId is not null)
            .GroupBy(c => c.CableInstanceId!, StringComparer.Ordinal).Select(g => (g.Key, Owners: g
                .SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId }).Where(endpoints.ContainsKey)
                .Select(e => endpoints[e].Component.ComponentInstanceId).Where(modules.ContainsKey).Select(id => modules[id])
                .Distinct(StringComparer.Ordinal).ToArray())).Where(p => p.Owners.Length == 1)
            .ToDictionary(p => p.Key, p => p.Owners[0], StringComparer.Ordinal);
        var reps = input.Representations.Select(r =>
        {
            if (r.OwnerKind == DrawingRepresentationOwnerKind.CableInstance)
                return cableModules.TryGetValue(r.OwnerId, out var cableModule)
                    ? r with { ControllerId = cableModule, PhysicalModuleId = cableModule, FunctionKind = "IO" } : r;
            if (r.OwnerKind != DrawingRepresentationOwnerKind.Component) return r;
            var category = categories.GetValueOrDefault(r.OwnerId);
            var matches = networks.Where(n => n.RepresentationIds.Contains(r.RepresentationId)).ToArray();
            var module = modules.GetValueOrDefault(r.OwnerId);
            var network = module is null && matches.Length == 1 ? matches[0] : null;
            var function = category switch
            {
                "Redundant Power Supply / CRPS" or "Power Supply" => "PowerSource",
                "DC-DC Converter" => "PowerConversion",
                "Contactor" or "Selector Switch" or "Emergency Stop Push Button" => "PowerControl",
                "Industrial Ethernet Switch" => "NetworkHub",
                _ => module is not null ? "IO" : r.FunctionKind
            };
            return r with { ControllerId = module, PhysicalModuleId = module, FunctionKind = function,
                NetworkId = network?.NetworkId, NetworkKind = network?.NetworkKind, FieldDeviceClass = category,
                HeavyDutyConnectorId = category == "Heavy Duty Connector" ? r.OwnerId : r.HeavyDutyConnectorId,
                PhysicalInterfaceMeaning = r.PhysicalInterfaceMeaning || category == "Heavy Duty Connector" };
        }).ToList();
        var power = input.PowerDomains.Select(d => d with
        {
            RepresentationIds = project.Components.Where(c => c.Ports.Any(p => p.PowerDomainId == d.PowerDomainId
                || p.Pins.Any(pin => pin.PowerDomainId == d.PowerDomainId)))
                .Select(c => $"REP:{c.ComponentInstanceId}:Schematic").Where(id => reps.Any(r => r.RepresentationId == id)).Order(StringComparer.Ordinal).ToArray()
        }).ToList();
        var moduleItems = masters.Order(StringComparer.Ordinal).Select(m => new DrawingControllerModuleItem
        {
            ControllerModuleId = m, ControllerId = m, PhysicalModuleId = m, FunctionKind = "IO",
            RepresentationIds = reps.Where(r => r.PhysicalModuleId == m && r.Role != DrawingRepresentationRole.CableDetail)
                .Select(r => r.RepresentationId).Order(StringComparer.Ordinal).ToArray()
        }).ToList();
        var connections = input.Connections.Select(c =>
        {
            var owners = new[] { c.FromEndpointId, c.ToEndpointId }.Where(endpoints.ContainsKey)
                .Select(e => endpoints[e].Component.ComponentInstanceId).Where(modules.ContainsKey)
                .Select(id => modules[id]).Distinct(StringComparer.Ordinal).ToArray();
            var module = owners.Length == 1 ? owners[0] : null;
            return c with { ControllerId = module, PhysicalModuleId = module, FunctionKind = module is null ? c.FunctionKind : "IO",
                NetworkId = networkByConnection.GetValueOrDefault(c.ConnectionId)?.NetworkId ?? c.NetworkId,
                PhysicalInterfaceMeaning = c.PhysicalInterfaceMeaning || project.Connections.Single(p => p.ConnectionId == c.ConnectionId).Kind == ConnectionKind.DirectMating };
        }).ToList();
        var heavy = reps.Where(r => r.HeavyDutyConnectorId is not null).Select(r => new DrawingHeavyDutyItem
        {
            HeavyDutyConnectorId = r.HeavyDutyConnectorId!, RepresentationIds = [r.RepresentationId],
            ContactIds = project.Components.Single(c => c.ComponentInstanceId == r.OwnerId).Ports.SelectMany(p => p.Pins)
                .Select(p => p.PinId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            // RowsPerPage is the existing generated-preview profile default, not an approved company block capacity.
        }).ToList();
        var powerEvidence = project.Components.SelectMany(c => c.Ports.SelectMany(p => p.Pins))
            .Where(p => p.Power is not null && p.Power.Role != PowerRole.Unknown)
            .OrderBy(p => p.PinId, StringComparer.Ordinal)
            .Select(p => new { endpointId = p.PinId, role = p.Power!.Role.ToString(), powerDomainId = p.PowerDomainId }).ToArray();
        var rules = input.WiringRules.ToList();
        var contactOrder = heavy.SelectMany(h => project.Components.Single(c => c.ComponentInstanceId == h.HeavyDutyConnectorId)
                .Ports.OrderBy(p => p.PortId, StringComparer.Ordinal).SelectMany(p => p.Pins
                    .OrderBy(pin => pin.PinNumber, DrawingContactDisplayOrder.NumberComparer)
                    .ThenBy(pin => pin.PinId, StringComparer.Ordinal)))
            .Select((pin, order) => new { endpointId = pin.PinId, order }).ToArray();
        if (contactOrder.Length > 0) rules.Add(new() { WiringRuleId = "PRESENTATION:CONTACT-DISPLAY-ORDER", RuleKind = "ContactDisplayOrder",
            Source = "ElectricalProject.ComponentPin.PinNumber", Value = contactOrder });
        if (powerEvidence.Length > 0) rules.Add(new() { WiringRuleId = "WIRING:EXPLICIT-ENDPOINT-POWER", RuleKind = "EndpointPowerEvidence",
            Source = "ElectricalProject.ComponentPin.Power", Value = powerEvidence });
        return input with { Representations = reps, Connections = connections, ControllerModules = moduleItems,
            Networks = networks.OrderBy(n => n.NetworkId, StringComparer.Ordinal).ToList(), PowerDomains = power, Issues = issues,
            HeavyDutyConnectors = heavy, WiringRules = rules,
            Cables = input.Cables.Select(c => c with { SourceControllerId = cableModules.GetValueOrDefault(c.CableInstanceId),
                SourcePhysicalModuleId = cableModules.GetValueOrDefault(c.CableInstanceId) }).ToList() };
    }
}
