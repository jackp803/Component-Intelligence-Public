using System.Globalization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWireEvidence(string Category, string ColorHex, string Description)
{
    // A bounded display profile, not physical core colour or installation certification.
    public const string Profile = "IEC60445-2021-DC-PREVIEW";

    public static SchematicWireEvidence Resolve(ElectricalProject project, SchematicWire wire)
    {
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        var chain = new List<SchematicWire> { wire };
        var seen = new HashSet<string>(StringComparer.Ordinal) { wire.WireId };
        if (connection is null && project.Schematic is { } doc)
        {
            for (var i = 0; i < chain.Count; i++)
            foreach (var attachment in new[] { chain[i].Start, chain[i].End }.Where(a => a.Kind == SchematicAttachmentKind.Continuation))
            {
                var pair = doc.Continuations.SingleOrDefault(c => c.Source.MarkerId == attachment.MarkerId || c.Destination.MarkerId == attachment.MarkerId);
                if (pair is null) continue;
                var peer = pair.Source.MarkerId == attachment.MarkerId ? pair.Destination.MarkerId : pair.Source.MarkerId;
                foreach (var next in doc.Wires.Where(w => w.Start.MarkerId == peer || w.End.MarkerId == peer))
                    if (seen.Add(next.WireId)) chain.Add(next);
            }
        }
        var endpointIds = connection is null
            ? chain.SelectMany(w => new[] { w.Start, w.End }).Where(a => a.Kind == SchematicAttachmentKind.Pin).Select(a => a.EndpointId).ToArray()
            : new[] { connection.FromEndpointId, connection.ToEndpointId };
        var pins = project.Components.SelectMany(c => c.Ports).SelectMany(p => p.Pins)
            .Where(p => endpointIds.Contains(p.PinId, StringComparer.Ordinal)).ToArray();
        var powers = pins.Select(p => p.Power).Where(p => p is not null).Cast<PowerCapability>().ToArray();
        var polarities = powers.Select(p => p.Polarity).Where(p => p is Polarity.Positive or Polarity.Negative or Polarity.Return).Distinct().ToArray();
        var types = powers.Select(p => p.Voltage?.Type ?? VoltageType.Unknown).Where(t => t != VoltageType.Unknown).Distinct().ToArray();
        var conflict = polarities.Length > 1 || types.Length > 1;
        var category = conflict ? "CONFLICT" : types.SequenceEqual([VoltageType.Dc]) && polarities.Length == 1
            ? polarities[0] switch { Polarity.Positive => "DC_POSITIVE", Polarity.Negative => "DC_NEGATIVE", _ => "UNCLASSIFIED" }
            : "UNCLASSIFIED";
        var descriptions = pins.Select(pin =>
        {
            var v = pin.Power?.Voltage;
            var value = v?.NominalVoltage is double n ? n.ToString("0.###", CultureInfo.InvariantCulture)
                : v?.MinVoltage is double min && v.MaxVoltage is double max
                    ? $"{min.ToString("0.###", CultureInfo.InvariantCulture)}..{max.ToString("0.###", CultureInfo.InvariantCulture)}" : "?";
            return $"{pin.PinNumber} {pin.PinName}: {value} V {(v?.Type ?? VoltageType.Unknown).ToString().ToUpperInvariant()}";
        });
        return new(category, category switch { "DC_POSITIVE" => "#B42318", "DC_NEGATIVE" => "#FFFFFF", "CONFLICT" => "#8B008B", _ => "#333333" },
            string.Join("\n", descriptions));
    }
}
