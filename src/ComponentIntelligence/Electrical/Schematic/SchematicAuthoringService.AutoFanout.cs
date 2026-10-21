using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicFanoutAlignmentResult(ElectricalProject Project, IReadOnlyList<string> Warnings, int AlignedPorts);

public sealed partial class SchematicAuthoringService
{
    public SchematicFanoutAlignmentResult AutoAlignExpandedPorts(ElectricalProject project, string symbolId)
    {
        var current = project; var warnings = new List<string>(); var aligned = 0;
        var initial = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == symbolId);
        if (initial is null) return new(project, warnings, 0);
        foreach (var portId in initial.EndpointDisplayPlacements.Select(p => p.PortId).Distinct(StringComparer.Ordinal))
        {
            var source = current.Schematic!.Symbols.Single(s => s.SymbolId == symbolId);
            var sourcePort = SchematicSymbolOwner.Resolve(current, source).Ports.Single(p => p.PortId == portId);
            var ids = sourcePort.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
            bool Own(SchematicAttachment a) => a.SymbolId == symbolId && ids.Contains(a.EndpointId ?? "");
            var peers = current.Schematic.Wires.Where(w => Own(w.Start) || Own(w.End)).Select(w => Own(w.Start) ? w.End : w.Start).ToArray();
            if (peers.Length == 0 || peers.Any(a => a.Kind != SchematicAttachmentKind.Pin)) continue;
            var ready = peers.All(a => {
                var symbol = current.Schematic.Symbols.SingleOrDefault(s => s.SymbolId == a.SymbolId);
                if (symbol is null) return false;
                var port = SchematicSymbolOwner.Resolve(current, symbol).Ports.Single(p => p.Pins.Any(pin => pin.PinId == a.EndpointId));
                return port.Pins.All(pin => symbol.EndpointDisplayPlacements.Any(p => p.EndpointId == pin.PinId)); });
            if (!ready) continue;
            try
            {
                var candidate = current;
                var affectedPorts = peers.Select(a => {
                    var symbol = current.Schematic.Symbols.Single(s => s.SymbolId == a.SymbolId);
                    var port = SchematicSymbolOwner.Resolve(current, symbol).Ports.Single(p => p.Pins.Any(pin => pin.PinId == a.EndpointId));
                    return (symbol.SymbolId, port.PortId); }).Prepend((symbolId, portId)).Distinct().ToArray();
                foreach (var (affectedSymbolId, affectedPortId) in affectedPorts)
                {
                    var affected = candidate.Schematic!.Symbols.Single(s => s.SymbolId == affectedSymbolId);
                    var affectedPort = SchematicSymbolOwner.Resolve(candidate, affected).Ports.Single(p => p.PortId == affectedPortId);
                    var affectedIds = affectedPort.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
                    var placements = affected.EndpointDisplayPlacements.Where(p => p.PortId == affectedPortId).ToArray();
                    var directions = affected.Anchors.Where(a => affectedIds.Contains(a.EndpointId)).Select(a => a.Direction).Distinct().ToArray();
                    if (directions.Length == 1 && placements.Length > 1)
                    {
                        var side = directions[0];
                        var rows = placements.Select(p => side is "Left" or "Right" ? p.Position.Y : p.Position.X).Order().ToArray();
                        if (rows.Zip(rows.Skip(1)).Any(pair => pair.Second - pair.First < DefaultFanoutPitch - .001))
                        {
                            var first = placements[0].Position;
                            var lead = side switch { "Left" => -first.X, "Right" => first.X - affected.Width, "Top" => -first.Y, _ => first.Y - affected.Height };
                            candidate = SetPortFanout(candidate, affectedSymbolId, affectedPortId, true, DefaultFanoutPitch, lead);
                        }
                    }
                }
                candidate = AlignPortFanout(candidate, symbolId, portId);
                current = candidate; aligned++;
            }
            catch (InvalidOperationException error) { warnings.Add(sourcePort.Name + "：" + error.Message); }
        }
        return new(current, warnings, aligned);
    }
}
