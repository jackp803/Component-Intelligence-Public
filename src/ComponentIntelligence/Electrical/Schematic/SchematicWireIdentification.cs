using ComponentIntelligence.Electrical.Cables;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Naming;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWireSizingProposal(double? AreaMm2, double? LoadCurrentAmp, string Basis, string Review);

public static class SchematicWireIdentification
{
    public const string ReferenceSource = "LAPP T12-1 (DIN VDE 0298-4:2013-06 reference excerpt), 30 C; provisional only";
    public const string VoltageDropSource = "Schneider Electrical Installation Guide, G29, 2026-08-05; DC equal-area copper return assumption";
    // Small excerpt of category B, two/three loaded cores; not a machinery compliance calculation.
    private static readonly (double Area, double Current)[] ReferenceSizes = [(0.5, 3), (.75, 6), (1, 10), (1.5, 16), (2.5, 20), (4, 25)];

    public static void Reconcile(ElectricalProject project)
    {
        if (project.Schematic is not { } doc) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var assignedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in doc.Wires.ToArray())
        {
            if (seen.Contains(original.WireId)) continue;
            var group = Segments(doc, original);
            seen.UnionWith(group.Select(w => w.WireId));
            var manualNames = group.Where(w => w.ManualDesignation).Select(w => w.Designation).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (manualNames.Length > 1) throw new InvalidOperationException("Paired segments have different manual wire names.");
            var name = manualNames.FirstOrDefault() ?? group.Select(w => w.Designation).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            var splitName = name is not null && assignedNames.Contains(name);
            if (splitName) name = null;
            name ??= AllocateName(project, group);
            assignedNames.Add(name);
            if (!doc.UsedWireDesignations.Contains(name, StringComparer.OrdinalIgnoreCase)) doc.UsedWireDesignations.Add(name);
            var manualMembers = group.Where(w => w.ManualSpecification || w.Awg.HasValue && w.SizingProposal is null).ToArray();
            var manual = manualMembers.FirstOrDefault();
            var sizes = manualMembers.Select(w => w.Awg is int gauge ? WireSize.AwgToAreaMm2(gauge) : w.AreaMm2).Distinct().ToArray();
            if (sizes.Length > 1) throw new InvalidOperationException("Paired segments have conflicting manual conductor sizes.");
            var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == original.ConnectionId);
            var proposal = manual is not null || connection?.Kind == ConnectionKind.Cable ||
                connection?.ConductorAreaMm2 is not null && group.All(w => w.SizingProposal is null)
                ? null : Propose(project, group);
            if (proposal is null && manual is null && connection?.Kind != ConnectionKind.Cable && group.Any(w => w.SizingProposal is not null))
                proposal = new(null, null, "INVALIDATED", "資料已變更，原自動線徑失效；請核對負載與適用條件。");
            foreach (var member in group)
            {
                var index = doc.Wires.FindIndex(w => w.WireId == member.WireId);
                var catalogReplacesProposal = connection?.Kind == ConnectionKind.Cable && member.SizingProposal is not null;
                doc.Wires[index] = member with { Designation = name, ManualDesignation = manualNames.Length > 0 && !splitName,
                    Awg = catalogReplacesProposal ? null : manual is not null ? manual.Awg : member.Awg,
                    AreaMm2 = catalogReplacesProposal ? null : manual is not null ? manual.AreaMm2 : proposal is not null ? proposal.AreaMm2 : member.AreaMm2,
                    ManualSpecification = manual is not null, SizingProposal = proposal };
            }
            if (connection is not null && proposal is not null) connection.ConductorAreaMm2 = proposal.AreaMm2;
            if (connection is not null && manual is not null && connection.Kind != ConnectionKind.Cable && sizes[0] is double manualArea)
                connection.ConductorAreaMm2 = manualArea;
        }
    }

    public static IReadOnlyList<SchematicWire> Segments(SchematicDocument doc, SchematicWire wire)
    {
        var result = new List<SchematicWire> { wire }; var seen = new HashSet<string> { wire.WireId };
        for (var i = 0; i < result.Count; i++)
        {
            var member = result[i];
            foreach (var next in doc.Wires.Where(w => member.ConnectionId is not null && w.ConnectionId == member.ConnectionId))
                if (seen.Add(next.WireId)) result.Add(next);
            foreach (var end in new[] { member.Start, member.End }.Where(a => a.Kind == SchematicAttachmentKind.Continuation))
            {
                var pair = doc.Continuations.SingleOrDefault(c => c.Source.MarkerId == end.MarkerId || c.Destination.MarkerId == end.MarkerId);
                if (pair is null) continue;
                var other = pair.Source.MarkerId == end.MarkerId ? pair.Destination.MarkerId : pair.Source.MarkerId;
                foreach (var next in doc.Wires.Where(w => w.Start.MarkerId == other || w.End.MarkerId == other))
                    if (seen.Add(next.WireId)) result.Add(next);
            }
        }
        return result;
    }

    public static int ApproximateAwg(double area) => Enumerable.Range(0, 41).MinBy(a => Math.Abs(WireSize.AwgToAreaMm2(a) - area));

    private static string AllocateName(ElectricalProject project, IReadOnlyList<SchematicWire> group)
    {
        var doc = project.Schematic!;
        var ends = group.SelectMany(w => new[] { w.Start, w.End }).Select(e => Endpoint(project, e.EndpointId)).Where(e => e is not null).ToArray();
        var first = ends.OrderBy(e => Prefix(e!.Value.Component) == "IOLink" ? 0 : 1).FirstOrDefault();
        var stem = "W";
        if (first is { } e)
        {
            var component = e.Component;
            if (!doc.WireNamingReferences.TryGetValue(component.ComponentInstanceId, out var reference))
            {
                reference = !string.IsNullOrWhiteSpace(component.ReferenceDesignator) ? Token(component.ReferenceDesignator) :
                    NamingEngine.NextReference(doc.WireNamingReferences.Values.Concat(project.Components.Select(c => c.ReferenceDesignator ?? "")), Prefix(component), 1);
                doc.WireNamingReferences[component.ComponentInstanceId] = reference;
            }
            stem = reference + "_" + Token(e.Port.Name) + (e.Pin is null ? "" : "_" + Token(e.Pin.PinNumber));
        }
        var reserved = doc.UsedWireDesignations.Concat(doc.Wires.Select(w => w.Designation ?? "")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stem != "W" && !reserved.Contains(stem)) return stem;
        if (stem == "W") return NamingEngine.NextReference(reserved, "W", 3);
        var suffix = 2;
        while (reserved.Contains(stem + "-" + suffix)) suffix++;
        return stem + "-" + suffix;
    }

    private static string Prefix(ComponentInstance component)
    {
        var type = Token(component.TypeKey).Replace("-", "", StringComparison.Ordinal);
        return type.Contains("IOLink", StringComparison.OrdinalIgnoreCase) ? "IOLink" : type;
    }

    private static string Token(string text)
    {
        var value = string.Concat(text.Where(c => char.IsLetterOrDigit(c) || c is '_' or '+' or '-'));
        return value.Length == 0 ? "Component" : value;
    }

    private static (ComponentInstance Component, ComponentPort Port, ComponentPin? Pin)? Endpoint(ElectricalProject project, string? id)
    {
        foreach (var c in project.Components)
        foreach (var port in c.Ports)
        {
            if (port.PortId == id) return (c, port, null);
            var pin = port.Pins.SingleOrDefault(p => p.PinId == id);
            if (pin is not null) return (c, port, pin);
        }
        return null;
    }

    private static SchematicWireSizingProposal? Propose(ElectricalProject project, IReadOnlyList<SchematicWire> group)
    {
        var doc = project.Schematic!;
        // Traverse only explicit junction/continuation relationships, never equal names or crossing geometry.
        var network = group.ToList(); var seen = group.Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < network.Count; i++)
        {
            var member = network[i];
            var neighbours = doc.Wires.Where(w => new[] { w.Start, w.End }.Any(e => e.Kind == SchematicAttachmentKind.WireJunction && e.WireId == member.WireId))
                .Concat(new[] { member.Start, member.End }.Where(e => e.Kind == SchematicAttachmentKind.WireJunction)
                    .SelectMany(e => doc.Wires.Where(w => w.WireId == e.WireId)));
            foreach (var next in neighbours.SelectMany(w => Segments(doc, w))) if (seen.Add(next.WireId)) network.Add(next);
        }
        var ends = network.SelectMany(w => new[] { w.Start, w.End }).Select(e => Endpoint(project, e.EndpointId))
            .Where(e => e is not null).Select(e => e!.Value).DistinctBy(e => e.Pin?.PinId ?? e.Port.PortId).ToArray();
        var sensor = ends.Any(e => e.Component.TypeKey.Contains("Sensor", StringComparison.OrdinalIgnoreCase));
        var min = ends.Select(e => e.Port.Connector?.MinTerminationAreaMm2).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        var max = ends.Select(e => e.Port.Connector?.MaxTerminationAreaMm2).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.MaxValue).Min();
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max <= 0 || max < min)
            return new(null, null, "INVALID-TERMINATION", "接頭適用截面積資料無效；請人工核對。");
        var pins = ends.SelectMany(e => e.Pin is null ? e.Port.Pins.AsEnumerable() : new[] { e.Pin }).ToArray();
        var voltages = pins.Select(p => p.Power?.Voltage).Where(v => v is not null).ToArray();
        if (voltages.Any(v => v!.Type == VoltageType.Ac || v.MaxVoltage > 60 || v.NominalVoltage > 60 ||
            v.NominalVoltage is double nominal && !double.IsFinite(nominal))) return null;
        var loads = pins.Select(p => p.Power?.RequiredCurrentAmp ?? p.Digital?.RequiredInputCurrentAmp).ToArray();
        if (loads.Any(v => v.HasValue && (!double.IsFinite(v.Value) || v.Value <= 0)))
            return new(null, null, "INVALID-CURRENT", "負載電流資料無效；請人工核對。");
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == group[0].ConnectionId);
        if (connection?.ConductorMaterial == ConductorMaterial.Aluminum)
            return new(null, null, "UNSUPPORTED-MATERIAL", "此版自動建議不涵蓋鋁導體；請人工選線。");
        if (connection?.ProvidedLengthMm is double suppliedLength && (!double.IsFinite(suppliedLength) || suppliedLength <= 0) ||
            connection?.MaxVoltageDropPercent is double suppliedLimit && (!double.IsFinite(suppliedLimit) || suppliedLimit <= 0 || suppliedLimit >= 100))
            return new(null, null, "INVALID-LENGTH-OR-LIMIT", "實際線長或允許壓降資料無效；請人工核對。");
        SchematicWireSizingProposal? SensorDefault()
        {
            if (!sensor) return null;
            var defaultArea = ReferenceSizes.FirstOrDefault(s => s.Area >= Math.Max(.5, min) && s.Area <= max).Area;
            return new(defaultArea > 0 ? defaultArea : null, null, "PRODUCT-SENSOR-DRAFT-V1",
                defaultArea > 0 ? "Sensor 草稿預設，非安全額定值；接法、負載、實際線長、敷設及保護仍待核對。"
                    : "Sensor 草稿預設不符合接頭截面積範圍；請人工選線。");
        }
        if (ends.Any(e => e.Pin is null)) return SensorDefault();
        var current = loads.Where(v => v > 0).Sum(v => v!.Value);
        if (current == 0) return SensorDefault();
        if (!sensor && !pins.Any(p => p.Power is not null)) return null;
        var basis = ReferenceSource;
        var lengthReview = "實際線長與壓降待核對。";
        if (connection?.LengthSource != CableLengthSource.Unknown && connection?.ProvidedLengthMm is double length &&
            connection.MaxVoltageDropPercent is double limit)
        {
            var voltage = voltages.Where(v => v!.Type == VoltageType.Dc && v.NominalVoltage > 0).Select(v => v!.NominalVoltage!.Value).DefaultIfEmpty(0).Min();
            if (voltage > 0)
            {
                // DC reduction of G29: round-trip resistance; never use drawn route length.
                min = Math.Max(min, 2 * current * 23.7 * (length / 1_000_000) / (voltage * limit / 100));
                basis += "; " + VoltageDropSource;
                lengthReview = "已以提供線長及壓降上限估算；假設銅／XLPE、等截面積回路，材質與整路壓降仍待核對。";
            }
        }
        var area = ReferenceSizes.FirstOrDefault(s => s.Current >= current && s.Area >= min && s.Area <= max).Area;
        return new(area > 0 ? area : null, current, basis,
            area > 0 ? lengthReview + "機械配線適用性、溫度、束線、絕緣及過電流保護待核對；缺資料的分支負載尚未計入。"
                : "參考線徑不符合已知負載、接頭或壓降條件；請人工選線。");
    }
}
