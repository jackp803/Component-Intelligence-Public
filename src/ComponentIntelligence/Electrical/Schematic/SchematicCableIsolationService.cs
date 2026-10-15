using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed class SchematicCableIsolationService
{
    public ElectricalProject Separate(ElectricalProject project)
    {
        var groups = project.Schematic?.Symbols.Where(s => s.CableInstanceId is not null &&
                project.Cables.Any(c => c.CableInstanceId == s.CableInstanceId && c.ArchivedCable is not null))
            .GroupBy(s => s.CableInstanceId!, StringComparer.Ordinal).Where(g => g.Count() > 1).ToArray();
        if (groups is null || groups.Length == 0) return project;
        SchematicAuthoringService.Validate(project);
        var next = EngineeringReviewService.Clone(project); var doc = next.Schematic!;
        var symbols = project.Schematic!.Symbols.ToDictionary(s => s.SymbolId, StringComparer.Ordinal);
        var maps = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var original = next.Cables.Single(c => c.CableInstanceId == group.Key);
            foreach (var (source, index) in group.Select((s, i) => (s, i)))
            {
                var cable = original;
                if (index > 0)
                {
                    cable = ArchivedCableInstanceFactory.Duplicate(original);
                    cable.ReferenceDesignator = original.ReferenceDesignator; cable.ProvidedLengthMm = original.ProvidedLengthMm;
                    cable.LengthSource = original.LengthSource; cable.Notes = original.Notes;
                    next.Cables.Add(cable);
                }
                var ids = SchematicCableInstanceIdentity.EndpointMap(original, cable); maps.Add(source.SymbolId, ids);
                foreach (var id in ids.Keys)
                {
                    if (!owners.TryGetValue(id, out var candidates)) owners[id] = candidates = [];
                    candidates.Add(source.SymbolId);
                }
                var at = doc.Symbols.FindIndex(s => s.SymbolId == source.SymbolId);
                doc.Symbols[at] = SchematicCableInstanceIdentity.Rebind(doc.Symbols[at], cable.CableInstanceId, ids);
            }
        }
        Dictionary<string, string>? Owner(ElectricalConnection connection, string endpoint)
        {
            if (!owners.TryGetValue(endpoint, out var candidates)) return null;
            var attached = project.Schematic.Wires.Where(w => w.ConnectionId == connection.ConnectionId)
                .SelectMany(w => new[] { w.Start, w.End }).Where(a => a.EndpointId == endpoint && a.SymbolId is not null)
                .Select(a => a.SymbolId!).Distinct(StringComparer.Ordinal).ToArray();
            if (attached.Length == 0)
                attached = candidates.Where(id => symbols[id].Anchors.Any(a => a.EndpointId == endpoint) ||
                    symbols[id].CadPortBindings.Any(b => b.PortId == endpoint)).ToArray();
            if (attached.Length != 1 || !maps.TryGetValue(attached[0], out var map))
                throw new InvalidOperationException($"連線 {connection.ConnectionId} 的接點歸屬不明；未拆分、未刪除外部接線，請先在圖面確認它屬於哪條線材。");
            return map;
        }
        for (var i = 0; i < next.Connections.Count; i++)
        {
            var connection = next.Connections[i];
            if (connection.CableInstanceId is { } cableId && groups.Any(g => g.Key == cableId))
                throw new InvalidOperationException($"連線 {connection.ConnectionId} 的實體線材歸屬需先確認；未拆分、未更改原有接線。");
            var from = Owner(connection, connection.FromEndpointId); var to = Owner(connection, connection.ToEndpointId);
            if (from is null && to is null) continue;
            var node = JsonSerializer.SerializeToNode(connection)!;
            node[nameof(ElectricalConnection.FromEndpointId)] = from?.GetValueOrDefault(connection.FromEndpointId) ?? connection.FromEndpointId;
            node[nameof(ElectricalConnection.ToEndpointId)] = to?.GetValueOrDefault(connection.ToEndpointId) ?? connection.ToEndpointId;
            var updated = node.Deserialize<ElectricalConnection>()!;
            if (connection.MatingMap is { } mating) updated.MatingMap = mating with { Pairs = mating.Pairs.Select(p => p with {
                FromPinId = from?.GetValueOrDefault(p.FromPinId) ?? p.FromPinId,
                ToPinId = to?.GetValueOrDefault(p.ToPinId) ?? p.ToPinId }).ToList() };
            next.Connections[i] = updated;
        }
        foreach (var cable in next.Cables)
        foreach (var core in cable.CoreAssignments)
        {
            string? Remap(string? endpoint)
            {
                if (endpoint is null || !owners.ContainsKey(endpoint)) return endpoint;
                var connections = project.Connections.Where(c => c.CableInstanceId == cable.CableInstanceId && c.CableCoreId == core.CoreId &&
                    (c.FromEndpointId == endpoint || c.ToEndpointId == endpoint)).ToArray();
                var targets = connections.Select(c => {
                    var updated = next.Connections.Single(n => n.ConnectionId == c.ConnectionId);
                    return c.FromEndpointId == endpoint ? updated.FromEndpointId : updated.ToEndpointId; }).Distinct().ToArray();
                if (targets.Length != 1) throw new InvalidOperationException("線芯 " + core.CoreId + " 的接點歸屬不明；未拆分線材。");
                return targets[0];
            }
            core.FromEndpointId = Remap(core.FromEndpointId); core.ToEndpointId = Remap(core.ToEndpointId);
        }
        SchematicAttachment Attachment(SchematicAttachment a) => a.SymbolId is { } id && a.EndpointId is { } endpoint && maps.TryGetValue(id, out var ids)
            ? a with { EndpointId = ids[endpoint] } : a;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i]; doc.Wires[i] = wire with { Start = Attachment(wire.Start), End = Attachment(wire.End) };
        }
        foreach (var page in doc.Pages.ToArray())
        {
            SchematicCableDetailBinding Binding(SchematicCableDetailBinding binding)
            {
                var group = groups.SingleOrDefault(g => g.Key == binding.CableInstanceId);
                if (group is null) return binding;
                var selected = group.SingleOrDefault(s => s.SymbolId == binding.SourceSymbolId);
                if (binding.SourceSymbolId is not null && selected is null)
                    throw new InvalidOperationException("製作明細指定的來源圖塊不存在或不屬於這條線材；未拆分線材。");
                if (selected is null)
                {
                    var local = group.Where(s => s.PageId == page.PageId).ToArray();
                    if (local.Length == 0)
                        throw new InvalidOperationException("製作明細所在頁沒有可辨識的來源線材；請先確認明細所屬圖塊，未拆分線材。");
                    var positions = new List<SchematicPoint>();
                    if (binding.TableVisible) positions.Add(binding.TablePosition);
                    if (binding.SketchVisible) positions.Add(binding.SketchPosition);
                    foreach (var position in positions)
                    {
                        double Distance(SchematicSymbol s)
                        {
                            var width = s.Rotation is 90 or 270 ? s.Height : s.Width;
                            var height = s.Rotation is 90 or 270 ? s.Width : s.Height;
                            var x = Math.Max(0, Math.Max(s.Position.X - position.X, position.X - s.Position.X - width));
                            var y = Math.Max(0, Math.Max(s.Position.Y - position.Y, position.Y - s.Position.Y - height));
                            return x * x + y * y;
                        }
                        var ordered = local.OrderBy(Distance).ToArray();
                        if (ordered.Length > 1 && Math.Abs(Distance(ordered[0]) - Distance(ordered[1])) < .001 ||
                            selected is not null && selected.SymbolId != ordered[0].SymbolId)
                            throw new InvalidOperationException("製作明細的線材歸屬不明；請先將明細移到所屬圖塊附近再拆分。");
                        selected = ordered[0];
                    }
                    if (selected is null) throw new InvalidOperationException("製作明細沒有可辨識的可見部分；未拆分線材。");
                }
                return binding with { CableInstanceId = doc.Symbols.Single(s => s.SymbolId == selected.SymbolId).CableInstanceId!, SourceSymbolId = selected.SymbolId };
            }
            var at = doc.Pages.IndexOf(page);
            doc.Pages[at] = page with { CableDetail = page.CableDetail is null ? null : Binding(page.CableDetail),
                InlineCableDetails = page.InlineCableDetails.Select(Binding).ToList() };
        }
        SchematicAuthoringService.Validate(next);
        Connectivity.ProjectPinFunctionService.Synchronize(next);
        SchematicWireIdentification.Reconcile(next);
        return next;
    }
}
