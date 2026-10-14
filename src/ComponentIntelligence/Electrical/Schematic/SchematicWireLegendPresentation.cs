using System.Globalization;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicWireLegendSpecification(int? Awg, double? AreaMm2, bool Approximate, string Status)
{
    public string Text => Awg is null && AreaMm2 is null ? "線徑待核對" :
        (Awg is int gauge ? (Approximate ? "約 AWG " : "AWG ") + gauge : "AWG 待核對") + " / " +
        (AreaMm2 is double area ? area.ToString("0.###", CultureInfo.InvariantCulture) + " mm²" : "截面積待核對") + " / " + Status;
}

public sealed record SchematicWireLegendEntry(string WireId, string PageId, string Index, string Designation,
    string Contacts, string Functions, string Color, SchematicWireLegendSpecification Specification, string Review)
{
    public string ShortFunction { get; init; } = "";
    public int PageNumber { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(Designation) ? Index : Designation;
    public string Caption => DisplayName + (ShortFunction.Length > 0 ? " · " + ShortFunction : "");
    public string FullText => string.Join("\n", new[] { Index + " / " + Designation, Contacts, Functions,
        Color + " / " + Specification.Text, Review }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record SchematicWireLegendGroup(SchematicWireLegendSpecification Specification,
    IReadOnlyList<SchematicWireLegendEntry> Entries)
{
    public string Text => string.Join(", ", Entries.Select(e => e.DisplayName)) + "：" + Specification.Text;
}

public static class SchematicWireLegendPresentation
{
    public static bool IsOrdinary(ElectricalProject project, SchematicWire wire)
    {
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
        return connection?.Kind != ConnectionKind.DirectMating && (connection?.Kind != ConnectionKind.Cable || connection.CableInstanceId is null);
    }

    public static IReadOnlyList<SchematicWireLegendEntry> Entries(ElectricalProject project, string? pageId = null)
    {
        var indices = Indices(project);
        return (project.Schematic?.Wires ?? []).Where(w => indices.ContainsKey(w.WireId) && (pageId is null || w.PageId == pageId))
            .Select(w => Create(project, w, indices[w.WireId])).ToArray();
    }

    public static SchematicWireLegendEntry Entry(ElectricalProject project, SchematicWire wire)
    {
        return Create(project, wire, Indices(project).GetValueOrDefault(wire.WireId));
    }

    private static Dictionary<string, int> Indices(ElectricalProject project)
    {
        var connections = project.Connections.ToDictionary(c => c.ConnectionId, StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var wire in project.Schematic?.Wires ?? [])
        {
            var connection = wire.ConnectionId is null ? null : connections.GetValueOrDefault(wire.ConnectionId);
            if (!string.IsNullOrWhiteSpace(wire.Designation) && connection?.Kind != ConnectionKind.DirectMating && (connection?.Kind != ConnectionKind.Cable || connection.CableInstanceId is null))
                indices.Add(wire.WireId, indices.Count + 1);
        }
        return indices;
    }

    public static IReadOnlyList<SchematicWireLegendGroup> Groups(IEnumerable<SchematicWireLegendEntry> entries) =>
        entries.GroupBy(e => e.Specification).Select(g => new SchematicWireLegendGroup(g.Key, g.ToArray())).ToArray();

    private static SchematicWireLegendEntry Create(ElectricalProject project, SchematicWire wire, int index)
    {
        var ends = SchematicWireContacts.Ends(project, wire);
        var spec = SchematicWirePresentation.Resolve(project, wire);
        var evidence = SchematicWireEvidence.Resolve(project, wire);
        var status = wire.SizingProposal is not null ? "待核對" : wire.ManualSpecification ? "手動設定" :
            spec.Awg is null && spec.AreaMm2 is null ? "未設定" : "已指定";
        var shortFunctions = ends.Select(e => e.ShortFunction).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var shortFunction = string.Join(" / ", shortFunctions);
        if (shortFunction.Length > 48) shortFunction = "功能見明細";
        var functions = string.Join("\n", ends.Select(e => e.Caption + ": " + (e.Pin?.DisplayFunction ?? e.Function) +
            (!string.IsNullOrWhiteSpace(e.Pin?.PinName) && e.Pin.PinName != e.Pin.Function ? " [" + e.Pin.PinName + "]" : "")));
        return new(wire.WireId, wire.PageId, index > 0 ? "W" + index.ToString("D2", CultureInfo.InvariantCulture) : "W?",
            wire.Designation ?? "", string.Join(" → ", ends.Select(e => e.Caption)), functions,
            evidence.ColorConflict ? "線色待確認" : evidence.PhysicalColor ?? "線色未指定",
            new(spec.Awg, spec.AreaMm2, spec.AwgIsApproximate, status), wire.SizingProposal?.Review ?? "")
        { ShortFunction = shortFunction, PageNumber = project.Schematic?.Pages.FindIndex(p => p.PageId == wire.PageId) + 1 ?? 0 };
    }
}
