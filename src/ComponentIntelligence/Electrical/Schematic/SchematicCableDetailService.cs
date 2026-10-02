using System.Globalization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicCableDetailPresentation(IReadOnlyList<SchematicCadPrimitive> Primitives,
    IReadOnlyList<string> ConnectionIds, IReadOnlyList<string> Diagnostics)
{
    public IReadOnlyDictionary<SchematicCableDetailPart, SchematicGridBounds> PartBounds { get; init; }
        = new Dictionary<SchematicCableDetailPart, SchematicGridBounds>();
}

public enum SchematicCableDetailPart { Table, Sketch }

// A detail is another view of existing cable authority, never a second cable or electrical graph.
public sealed partial class SchematicCableDetailService
{
    public ElectricalProject AddPage(ElectricalProject project, string connectionId, string? templatePageId = null)
    {
        var connection = project.Connections.SingleOrDefault(c => c.ConnectionId == connectionId)
            ?? throw new InvalidOperationException("請選取已完成的線材導體。");
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == connection.CableInstanceId)
            ?? throw new InvalidOperationException("普通 Wire 沒有線材明細；請先在「線材設定」明確建立實體線材。");
        var assemblies = project.CableAssemblies.Where(a => a.PhysicalTopology?.CableInstanceId == cable.CableInstanceId ||
            a.Members.Any(m => m.CableInstanceId == cable.CableInstanceId)).ToArray();
        if (assemblies.Length > 1) throw new InvalidOperationException("此線材有多重組合歸屬，請先確認。");
        var assembly = assemblies.SingleOrDefault();
        if (assembly is not null && assembly.PhysicalTopology is null)
            throw new InvalidOperationException("此組合線尚無明確的共同端／分支結構；請先使用線材設定確認。");
        var binding = new SchematicCableDetailBinding(cable.CableInstanceId, assembly?.CableAssemblyId);
        var next = EngineeringReviewService.Clone(project);
        if (next.Schematic?.Pages.Any(p => p.CableDetail == binding) == true) return next;
        var template = next.Schematic?.Pages.SingleOrDefault(p => p.PageId == templatePageId);
        var reference = assembly?.ReferenceDesignator ?? cable.ReferenceDesignator ?? cable.DisplayName ?? "線材明細";
        var page = (template ?? new SchematicPage { PageId = "", Title = "" }) with
        {
            PageId = $"sheet-{Guid.NewGuid():N}", Title = reference + " / Cable Detail", CableDetail = binding
        };
        next.Schematic ??= new();
        next.Schematic.Pages.Add(page);
        Build(next, page);
        SchematicAuthoringService.Validate(next);
        return next;
    }

    public SchematicCableDetailPresentation Build(ElectricalProject project, SchematicPage page)
    {
        var binding = page.CableDetail ?? throw new InvalidOperationException("此頁不是線材明細。");
        var cable = project.Cables.SingleOrDefault(c => c.CableInstanceId == binding.CableInstanceId)
            ?? throw new InvalidOperationException("線材明細的來源線材不存在。");
        if (cable.ArchivedCable is not null) return BuildArchived(project, page, binding);
        var assembly = binding.CableAssemblyId is null ? null : project.CableAssemblies.SingleOrDefault(a => a.CableAssemblyId == binding.CableAssemblyId)
            ?? throw new InvalidOperationException("線材明細的來源組合不存在。");
        var physical = assembly?.PhysicalTopology;
        if (assembly is not null && (physical is null || physical.CableInstanceId != cable.CableInstanceId))
            throw new InvalidOperationException("線材明細的實體歸屬已變更，請重新確認。");
        var connections = physical is null ? project.Connections.Where(c => c.CableInstanceId == cable.CableInstanceId).ToArray()
            : physical.ConnectionIds.Select(id => project.Connections.SingleOrDefault(c => c.ConnectionId == id && c.CableInstanceId == cable.CableInstanceId)
                ?? throw new InvalidOperationException("多端線材的導體歸屬缺失或衝突。")).ToArray();
        if (connections.Length == 0 || connections.Any(c => c.Kind == ConnectionKind.DirectMating) ||
            connections.Select(c => c.ConnectionId).Distinct(StringComparer.Ordinal).Count() != connections.Length)
            throw new InvalidOperationException("線材明細需要不重複且明確的導體連線。");
        if (physical is not null && !connections.Select(c => c.ConnectionId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(project.Connections.Where(c => c.CableInstanceId == cable.CableInstanceId).Select(c => c.ConnectionId)))
            throw new InvalidOperationException("多端線材的實體清單未涵蓋所有導體，不能省略未列入的接線。");
        var ends = new UnifiedCableSettingsService().DescribeEnds(project, connections.Select(c => c.ConnectionId).ToArray());
        if (physical is null && ends.Count != 2) throw new InvalidOperationException("多端線材需要明確共同端與分支，不能從接線形狀推測。");
        if (physical is not null && (physical.Branches.Count < 2 || physical.Branches.Any(b => b.Index <= 0) ||
            physical.Branches.Select(b => b.Index).Distinct().Count() != physical.Branches.Count ||
            physical.Branches.Select(b => b.PortId).Append(physical.CommonPortId).Distinct(StringComparer.Ordinal).Count() != physical.Branches.Count + 1 ||
            !ends.Select(e => e.EndpointGroupId).ToHashSet(StringComparer.Ordinal).SetEquals(physical.Branches.Select(b => b.PortId).Append(physical.CommonPortId))))
            throw new InvalidOperationException("多端線材的共同端／分支與現有導體不一致。");

        var primitives = new List<SchematicCadPrimitive>(); var diagnostics = new List<string>();
        void Line(double x1, double y1, double x2, double y2) => primitives.Add(new() { Kind = "LINE", Start = new(x1, y1), End = new(x2, y2) });
        void Text(string value, double x, double y, double size = 3) => primitives.Add(new() { Kind = "TEXT", Start = new(x, y), Text = value, TextHeight = size });
        var left = page.Margin + 8; var right = page.Width - page.Margin - 8;
        var y = page.Margin + 10;
        string Length(double? value) => value is null ? "待確認" : !double.IsFinite(value.Value) || value <= 0
            ? throw new InvalidOperationException("線材長度必須為正值；未知請保持空白。")
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " mm";
        var construction = assembly?.CableConstructionType ?? cable.CableConstructionType;
        if (construction == CableConstructionType.Unknown) diagnostics.Add("CABLE_CONSTRUCTION_UNKNOWN");
        if (cable.ProvidedLengthMm is null && physical is null) diagnostics.Add("CABLE_LENGTH_UNKNOWN");
        foreach (var title in Wrap(assembly?.ReferenceDesignator ?? cable.ReferenceDesignator ?? cable.DisplayName ?? "Reference 待確認", Math.Max(8, (int)((right - left) / 5))))
        { Text(title, left, y, 5); y += 8; }
        foreach (var specification in Wrap($"{construction}   |   {cable.CableDefinitionId}", Math.Max(8, (int)((right - left) / 3))))
        { Text(specification, left, y); y += 6; }
        Text($"Length: {Length(cable.ProvidedLengthMm)}   |   Source: {cable.LengthSource}", left, y);
        y += 16;
        var common = physical is null ? ends[0] : ends.Single(e => e.EndpointGroupId == physical.CommonPortId);
        var branches = physical is null ? new[] { (Label: InterfaceLabel(project, ends[1]), Index: 0, Length: cable.ProvidedLengthMm) }
            : physical.Branches.OrderBy(b => b.Index).Select(b => (Label: InterfaceLabel(project, ends.Single(e => e.EndpointGroupId == b.PortId)), Index: b.Index, Length: b.LengthMm)).ToArray();
        var splitX = left + (right - left) * .42;
        var endX = left + (right - left) * .72;
        foreach (var label in Wrap(InterfaceLabel(project, common), Math.Max(8, (int)((splitX - left) / 3))))
        { Text(label, left, y); y += 5; }
        var branchLabels = branches.Select(b => Wrap((b.Index == 0 ? "" : $"Branch {b.Index}  ") + b.Label,
            Math.Max(8, (int)((right - endX - 3) / 3)))).ToArray();
        var branchStep = Math.Max(24, branchLabels.Max(lines => lines.Length) * 5 + 12);
        var commonY = y + 14 + (branches.Length - 1) * branchStep / 2;
        Line(left, commonY - 1.5, splitX, commonY - 1.5); Line(left, commonY + 1.5, splitX, commonY + 1.5);
        if (physical is not null)
        {
            Text("Trunk: " + Length(physical.TrunkLengthMm), left, commonY + 5);
            if (physical.TrunkLengthMm is null) diagnostics.Add("TRUNK_LENGTH_UNKNOWN");
        }
        for (var i = 0; i < branches.Length; i++)
        {
            var branch = branches[i]; var by = y + 14 + i * branchStep;
            Line(splitX, commonY - 1.5, endX, by - 1.5); Line(splitX, commonY + 1.5, endX, by + 1.5);
            for (var row = 0; row < branchLabels[i].Length; row++) Text(branchLabels[i][row], endX + 3, by - 6 + row * 5);
            Text(Length(branch.Length), endX + 3, by - 1 + branchLabels[i].Length * 5);
            if (branch.Length is null) diagnostics.Add("BRANCH_LENGTH_UNKNOWN");
        }
        y += 28 + branches.Length * branchStep;
        Text("接線對照 / Electrical connections", left, y, 4); y += 10;
        var middle = left + (right - left) * .45; var spec = left + (right - left) * .86;
        Text("From / Pin", left + 2, y); Text("To / Pin", middle + 2, y); Text("Core / Area", spec + 2, y);
        y += 7; Line(left, y, right, y);
        foreach (var connection in connections)
        {
            var from = EndpointLabel(project, connection.FromEndpointId);
            var to = EndpointLabel(project, connection.ToEndpointId);
            var fromLines = Wrap(from, 33); var toLines = Wrap(to, 30);
            var coreLines = Wrap(connection.CableCoreId ?? "Core 待確認", Math.Max(6, (int)((right - spec - 4) / 2.5))).ToList();
            if (connection.CableCoreId is null) diagnostics.Add("CABLE_CORE_MAPPING_UNKNOWN");
            var gauges = project.Schematic?.Wires.Where(w => w.ConnectionId == connection.ConnectionId && w.Awg.HasValue).Select(w => w.Awg!.Value).Distinct().ToArray() ?? [];
            if (gauges.Length == 1) coreLines.Add($"AWG {gauges[0]}");
            if (gauges.Length > 1) throw new InvalidOperationException("同一導體的 AWG 規格互相衝突。");
            if (connection.ConductorAreaMm2.HasValue) coreLines.Add(connection.ConductorAreaMm2.Value.ToString("0.###", CultureInfo.InvariantCulture) + " mm2");
            var height = Math.Max(Math.Max(fromLines.Length, toLines.Length), coreLines.Count) * 5 + 5;
            if (y + height > page.Height - page.Margin - 18)
                throw new InvalidOperationException("此線材明細超出有效圖面；請先使用較大紙張，不能隱藏導體資料。");
            for (var i = 0; i < fromLines.Length; i++) Text(fromLines[i], left + 2, y + 3 + i * 5);
            for (var i = 0; i < toLines.Length; i++) Text(toLines[i], middle + 2, y + 3 + i * 5);
            for (var i = 0; i < coreLines.Count; i++) Text(coreLines[i], spec + 2, y + 3 + i * 5, 2.5);
            y += height; Line(left, y, right, y);
        }
        return new(primitives, connections.Select(c => c.ConnectionId).ToArray(), diagnostics.Distinct().ToArray());
    }

    private static string[] Wrap(string value, int length) => Enumerable.Range(0, Math.Max(1, (value.Length + length - 1) / length))
        .Select(i => value.Substring(i * length, Math.Min(length, value.Length - i * length))).ToArray();

    private static string InterfaceLabel(ElectricalProject project, CableEndpointContext end)
    {
        var port = project.Components.SelectMany(c => c.Ports).SingleOrDefault(p => p.PortId == end.EndpointGroupId);
        if (port?.Connector is not { } connector) return end.Label;
        return end.Label + " / " + string.Join(" ", new[] { connector.Family, connector.Coding,
            connector.PinCount is int pins ? $"{pins}P" : null, connector.Gender == ConnectorGender.Unknown ? null : connector.Gender.ToString() }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string EndpointLabel(ElectricalProject project, string id)
    {
        var matches = project.Components.SelectMany(c => c.Ports.SelectMany(p => p.Pins.Where(pin => pin.PinId == id)
            .Select(pin => $"{c.ReferenceDesignator ?? c.DisplayName ?? c.ComponentDefinitionId} / {p.Name} / {pin.PinNumber} {pin.PinName}"))).ToArray();
        if (matches.Length == 1) return matches[0];
        // Port-level or terminal endpoints remain explicit boundaries; never expand guessed pin pairs.
        var ports = project.Components.SelectMany(c => c.Ports.Where(p => p.PortId == id)
            .Select(p => $"{c.ReferenceDesignator ?? c.DisplayName ?? c.ComponentDefinitionId} / {p.Name} / Pin mapping 待確認")).ToArray();
        if (matches.Length == 0 && ports.Length == 1) return ports[0];
        var terminals = project.TerminalBlocks.SelectMany(b => b.Positions.SelectMany(p => p.Levels.SelectMany(l => l.ConnectionPoints.Where(cp => cp.ConnectionPointId == id)
            .Select(cp => $"{b.ReferenceDesignator} / {p.PositionLabel} / {l.LevelName} / {cp.PhysicalSide ?? cp.ConnectionPointId}")))).ToArray();
        if (matches.Length == 0 && ports.Length == 0 && terminals.Length == 1) return terminals[0];
        throw new InvalidOperationException("線材明細端點不存在或不唯一。");
    }
}
