using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed class CableWiringEditorService
{
    public static string EdgeKey(CablePinMapping pair) => EdgeKey(pair.FromSourcePinId, pair.ToSourcePinId);
    public static string EdgeKey(string from, string to) => string.CompareOrdinal(from, to) < 0
        ? JsonSerializer.Serialize(new[] { from, to }) : JsonSerializer.Serialize(new[] { to, from });
    public static IReadOnlyList<CablePinMapping> Mapping(CableManufacturingDraft draft) => draft.InventoryOnly
        ? draft.ExplicitMapping ?? [] : draft.Rows.SelectMany(CableManufacturingCells.Pairs).ToArray();

    public void EnterVisualMode(CableManufacturingDraft draft)
    {
        if (draft.InventoryOnly) { EnsureLayout(draft); return; }
        var errors = new CableManufacturingEditorService().Validate(draft);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        if (draft.Rows.SelectMany(CableManufacturingCells.All).Any(c => c.SourcePinId is null && !string.IsNullOrWhiteSpace(c.Function)))
            throw new InvalidOperationException("舊表格有尚未綁定 Pin 的功能文字；請先指定 Pin 或整理該列，再進入接線圖。原接法保持不變。");
        draft.ExplicitMapping = Mapping(draft).ToList();
        draft.InventoryOnly = true;
        RebuildInventory(draft);
        EnsureLayout(draft);
    }

    public static void RebuildInventory(CableManufacturingDraft draft)
    {
        if (!draft.InventoryOnly) return;
        var cells = draft.Rows.SelectMany(CableManufacturingCells.All).Where(c => c.SourcePinId is not null)
            .GroupBy(c => c.SourcePinId!, StringComparer.Ordinal).ToDictionary(g => g.Key,
                g => g.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Function)) ?? g.First(), StringComparer.Ordinal);
        var ordered = draft.Ends.ToDictionary(p => p.PortId,
            p => p.Pins.OrderBy(pin => pin.PinNumber, DrawingContactDisplayOrder.NumberComparer).ToArray(), StringComparer.Ordinal);
        var rows = new List<CableManufacturingRow>();
        for (var i = 0; i < ordered.Values.Max(p => p.Length); i++)
        {
            var row = new CableManufacturingRow();
            foreach (var port in draft.Ends)
            {
                if (i >= ordered[port.PortId].Length) continue;
                var pin = ordered[port.PortId][i];
                CableManufacturingCells.Set(draft, row, port.PortId, cells.TryGetValue(pin.PinId, out var cell)
                    ? Clone(cell) : new() { SourcePinId = pin.PinId, Function = pin.Function });
            }
            rows.Add(row);
        }
        draft.Rows = rows; CableManufacturingCells.EnsureColumns(draft);
    }

    public void EnsureLayout(CableManufacturingDraft draft)
    {
        draft.WiringDiagram ??= new();
        var layout = draft.WiringDiagram;
        var y = 10d;
        for (var i = 0; i < draft.Ends.Count; i += 2)
        {
            var pair = draft.Ends.Skip(i).Take(2).ToArray();
            var widths = pair.Select(p => Math.Max(60, 12 + p.Pins.Max(pin => (pin.Function ?? "").Length + pin.PinNumber.Length) * 1.9)).ToArray();
            for (var n = 0; n < pair.Length; n++)
            {
                var port = pair[n];
                if (!layout.PortBounds.TryGetValue(port.PortId, out var bounds))
                    layout.PortBounds[port.PortId] = bounds = new(n == 0 ? 10 : 10 + widths[0] + 110, y, widths[n], 24 + port.Pins.Count * 7);
                bounds = ExpandHeight(draft, port, bounds, 24 + port.Pins.Count * 7);
                var ordered = port.Pins.OrderBy(p => p.PinNumber, DrawingContactDisplayOrder.NumberComparer).ToArray();
                for (var p = 0; p < ordered.Length; p++)
                {
                    var id = ordered[p].PinId;
                    if (!layout.PinPositions.ContainsKey(id))
                    {
                        var pinY = bounds.Y + 22 + p * 7;
                        while (port.Pins.Any(pin => layout.PinPositions.TryGetValue(pin.PinId, out var existing) && Math.Abs(existing.Y - pinY) < 5)) pinY += 7;
                        bounds = ExpandHeight(draft, port, bounds, pinY - bounds.Y + 9);
                        layout.PinPositions[id] = new(n == 0 ? bounds.X + bounds.Width : bounds.X, pinY);
                    }
                    var attached = AttachToBorder(bounds, layout.PinPositions[id]);
                    if (attached == layout.PinPositions[id]) continue;
                    layout.PinPositions[id] = attached;
                    ClearAffectedRoutes(draft, id);
                }
            }
            y += pair.Max(p => layout.PortBounds[p.PortId].Height) + 22;
        }
    }

    public void Connect(CableManufacturingDraft draft, string from, string to)
    {
        RequireVisual(draft); RequirePin(draft, from); RequirePin(draft, to);
        if (from == to) throw new InvalidOperationException("不能將同一個 Pin 連到自己。");
        if (Mapping(draft).Any(p => EdgeKey(p) == EdgeKey(from, to))) throw new InvalidOperationException("這兩個 Pin 已有接線。");
        if (new[] { from, to }.Any(id => Usage(draft, id) != CablePinUsage.Pending))
            throw new InvalidOperationException("NC／未使用的 Pin 不能接線；請先改為 Pending。");
        draft.ExplicitMapping!.Add(new(from, to)); draft.ConfirmMapping = false;
    }

    public void Disconnect(CableManufacturingDraft draft, string from, string to)
    {
        RequireVisual(draft);
        if (draft.ExplicitMapping!.RemoveAll(p => EdgeKey(p) == EdgeKey(from, to)) == 0)
            throw new InvalidOperationException("接線不存在。");
        draft.WiringDiagram?.Routes.Remove(EdgeKey(from, to)); draft.ConfirmMapping = false;
    }

    public void SetUsage(CableManufacturingDraft draft, string pinId, CablePinUsage usage)
    {
        RequireVisual(draft); RequirePin(draft, pinId);
        if (!Enum.IsDefined(usage)) throw new InvalidOperationException("未知 Pin 狀態。");
        if (usage != CablePinUsage.Pending && Mapping(draft).Any(p => p.FromSourcePinId == pinId || p.ToSourcePinId == pinId))
            throw new InvalidOperationException("這個 Pin 已接線，請先刪除相關接線再標示 NC／未使用。");
        foreach (var row in draft.Rows)
        foreach (var port in draft.Ends)
        {
            var cell = CableManufacturingCells.Get(draft, row, port.PortId);
            if (cell.SourcePinId != pinId) continue;
            if (cell.Usage != usage) draft.ConfirmMapping = false;
            cell.Usage = usage; CableManufacturingCells.Set(draft, row, port.PortId, cell);
        }
    }

    public void MovePort(CableManufacturingDraft draft, string portId, SchematicPoint position)
    {
        RequireVisual(draft); CheckPoint(position); EnsureLayout(draft);
        var port = draft.Ends.Single(p => p.PortId == portId);
        var b = draft.WiringDiagram!.PortBounds[portId];
        var dx = position.X - b.X; var dy = position.Y - b.Y;
        draft.WiringDiagram.PortBounds[portId] = b with { X = position.X, Y = position.Y };
        foreach (var pin in port.Pins)
        {
            var p = draft.WiringDiagram.PinPositions[pin.PinId];
            draft.WiringDiagram.PinPositions[pin.PinId] = new(p.X + dx, p.Y + dy);
            ClearAffectedRoutes(draft, pin.PinId);
        }
    }

    public void MovePin(CableManufacturingDraft draft, string pinId, SchematicPoint position)
    {
        RequireVisual(draft); RequirePin(draft, pinId); CheckPoint(position); EnsureLayout(draft);
        var port = draft.Ends.Single(p => p.Pins.Any(pin => pin.PinId == pinId));
        position = AttachToBorder(draft.WiringDiagram!.PortBounds[port.PortId], position);
        var samePort = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (draft.WiringDiagram!.PinPositions.Any(p => p.Key != pinId && samePort.Contains(p.Key) && Math.Abs(p.Value.Y - position.Y) < 5))
            throw new InvalidOperationException("這個接頭的 Pin 標示會重疊；請保留至少 5 mm 的上下間距。");
        if (draft.WiringDiagram!.PinPositions.Any(p => p.Key != pinId && Math.Abs(p.Value.X - position.X) < 3 && Math.Abs(p.Value.Y - position.Y) < 5))
            throw new InvalidOperationException("接點位置太靠近其他 Pin，請選擇其他位置。");
        draft.WiringDiagram.PinPositions[pinId] = position; ClearAffectedRoutes(draft, pinId);
    }

    public void SetRoute(CableManufacturingDraft draft, string from, string to, IReadOnlyList<SchematicPoint> points)
    {
        RequireVisual(draft); EnsureLayout(draft);
        var pair = Mapping(draft).SingleOrDefault(p => EdgeKey(p) == EdgeKey(from, to)) ?? throw new InvalidOperationException("接線不存在。");
        var ordered = pair.FromSourcePinId == from ? points : points.Reverse().ToArray();
        var route = Simplify(ordered);
        if (route.Count < 2 || route[0] != draft.WiringDiagram!.PinPositions[pair.FromSourcePinId] ||
            route[^1] != draft.WiringDiagram.PinPositions[pair.ToSourcePinId]) throw new InvalidOperationException("路徑端點必須留在指定 Pin。");
        foreach (var point in route) CheckPoint(point);
        draft.WiringDiagram.Routes[EdgeKey(pair)] = route;
    }

    private static void ClearAffectedRoutes(CableManufacturingDraft draft, string id)
    {
        foreach (var edge in Mapping(draft).Where(p => p.FromSourcePinId == id || p.ToSourcePinId == id))
            draft.WiringDiagram?.Routes.Remove(EdgeKey(edge));
    }

    public static IReadOnlyList<string> ValidateDiagram(CableManufacturingDraft draft)
    {
        var errors = new List<string>();
        if (draft.WiringDiagram is not { } d) return errors;
        var pins = draft.Ends.SelectMany(p => p.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        if (d.PortBounds.Any(p => !draft.Ends.Any(e => e.PortId == p.Key) ||
            !Finite(p.Value.X, p.Value.Y, p.Value.Width, p.Value.Height) || p.Value.Width <= 0 || p.Value.Height <= 0) ||
            d.PinPositions.Any(p => !pins.Contains(p.Key) || !Finite(p.Value.X, p.Value.Y))) errors.Add("Invalid wiring diagram Port/Pin layout.");
        var pairs = Mapping(draft).GroupBy(EdgeKey).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var (key, route) in d.Routes)
        {
            if (!pairs.TryGetValue(key, out var pair) || route.Count < 2 || route.Any(p => !Finite(p.X, p.Y)) ||
                !d.PinPositions.TryGetValue(pair.FromSourcePinId, out var from) || !d.PinPositions.TryGetValue(pair.ToSourcePinId, out var to) ||
                route[0] != from || route[^1] != to || route.Zip(route.Skip(1)).Any(p => p.First.X != p.Second.X && p.First.Y != p.Second.Y))
                errors.Add("Invalid or stale exact-Pin wiring route.");
        }
        return errors;
    }

    public CableWiringDiagramPresentation Build(CableManufacturingDraft source)
    {
        var draft = Clone(source); EnsureLayout(draft);
        var errors = ValidateDiagram(draft);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        var layout = draft.WiringDiagram!;
        var primitives = new List<SchematicCadPrimitive>();
        var paths = new Dictionary<string, IReadOnlyList<SchematicPoint>>();
        void Line(SchematicPoint a, SchematicPoint b) => primitives.Add(new() { Kind = "LINE", Start = a, End = b });
        void Text(string value, double x, double y, double height = 2.7) => primitives.Add(new() { Kind = "TEXT", Start = new(x, y), Text = value, TextHeight = height, TextAttachment = "TopLeft" });
        foreach (var port in draft.Ends)
        {
            var b = layout.PortBounds[port.PortId];
            Line(new(b.X, b.Y), new(b.X + b.Width, b.Y)); Line(new(b.X, b.Y), new(b.X, b.Y + b.Height));
            Line(new(b.X + b.Width, b.Y), new(b.X + b.Width, b.Y + b.Height)); Line(new(b.X, b.Y + b.Height), new(b.X + b.Width, b.Y + b.Height));
            Text(port.Name, b.X + 3, b.Y + 3, 3.5);
            Text(port.Connector is { } c ? $"{c.Family} {c.Coding} {c.Gender} / {port.Pins.Count} Pin" : $"接頭待確認 / {port.Pins.Count} Pin", b.X + 3, b.Y + 10);
            foreach (var pin in port.Pins)
            {
                var p = layout.PinPositions[pin.PinId];
                primitives.Add(new() { Kind = "CIRCLE", Start = p, Radius = 1 });
                var cell = draft.Rows.SelectMany(CableManufacturingCells.All).FirstOrDefault(c => c.SourcePinId == pin.PinId);
                var status = Usage(draft, pin.PinId);
                var label = pin.PinNumber + " / " + (status == CablePinUsage.Nc ? "NC" : status == CablePinUsage.Unused ? "未使用" : cell?.Function ?? pin.Function ?? "待確認");
                var rightSide = p.X >= b.X + b.Width / 2;
                var x = rightSide ? b.X + 3 : p.X + 3;
                Text(label, x, p.Y - 3);
            }
        }
        var index = 0;
        foreach (var pair in Mapping(draft))
        {
            var from = layout.PinPositions[pair.FromSourcePinId]; var to = layout.PinPositions[pair.ToSourcePinId];
            if (!layout.Routes.TryGetValue(EdgeKey(pair), out var route))
            {
                var x = Math.Abs(from.X - to.X) < 10 ? Math.Max(from.X, to.X) + 10 + index * 3 :
                    (from.X + to.X) / 2 + index * 2;
                route = Simplify([from, new(x, from.Y), new(x, to.Y), to]);
            }
            paths.Add(EdgeKey(pair), route);
            for (var i = 1; i < route.Count; i++) Line(route[i - 1], route[i]);
            index++;
        }
        // A geometric crossing is never an electrical connection. Mark only shared
        // vertices of explicitly connected pairs, not arbitrary segment intersections.
        foreach (var pair in Mapping(draft))
        foreach (var other in Mapping(draft).Where(p => EdgeKey(p) != EdgeKey(pair) &&
            new[] { p.FromSourcePinId, p.ToSourcePinId }.Intersect(new[] { pair.FromSourcePinId, pair.ToSourcePinId }).Any()))
            foreach (var point in paths[EdgeKey(pair)].Where(point => paths[EdgeKey(other)].Zip(paths[EdgeKey(other)].Skip(1)).Any(segment =>
                point.X == segment.First.X && point.X == segment.Second.X && point.Y >= Math.Min(segment.First.Y, segment.Second.Y) && point.Y <= Math.Max(segment.First.Y, segment.Second.Y) ||
                point.Y == segment.First.Y && point.Y == segment.Second.Y && point.X >= Math.Min(segment.First.X, segment.Second.X) && point.X <= Math.Max(segment.First.X, segment.Second.X))))
                primitives.Add(new() { Kind = "CIRCLE", Start = point, Radius = .6 });
        var points = primitives.SelectMany(p => new[] { p.Start, p.End ?? p.Start }).ToArray();
        var minX = points.Min(p => p.X) - 5; var minY = points.Min(p => p.Y) - 5;
        var maxX = primitives.Max(p => p.Kind == "TEXT" ? p.Start.X + (p.Text?.Length ?? 0) * p.TextHeight : Math.Max(p.Start.X, p.End?.X ?? p.Start.X));
        var maxY = primitives.Max(p => p.Kind == "TEXT" ? p.Start.Y + p.TextHeight * 1.5 : Math.Max(p.Start.Y, p.End?.Y ?? p.Start.Y));
        return new(primitives, new(minX, minY, maxX - minX + 5, maxY - minY + 5), layout.PortBounds, layout.PinPositions, paths);
    }

    private static List<SchematicPoint> Simplify(IEnumerable<SchematicPoint> points)
    {
        var route = new List<SchematicPoint>();
        foreach (var p in points) route = SchematicDraftRoute.Append(route, p);
        return route;
    }
    private static SchematicPoint AttachToBorder(SchematicGridBounds bounds, SchematicPoint point)
    {
        if (!Finite(point.X, point.Y)) return point;
        var x = Math.Clamp(point.X, bounds.X, bounds.X + bounds.Width);
        var y = Math.Clamp(point.Y, bounds.Y, bounds.Y + bounds.Height);
        return new SchematicPoint[] { new(bounds.X, y), new(bounds.X + bounds.Width, y),
            new(x, bounds.Y), new(x, bounds.Y + bounds.Height) }
            .MinBy(p => (p.X - point.X) * (p.X - point.X) + (p.Y - point.Y) * (p.Y - point.Y))!;
    }
    private static SchematicGridBounds ExpandHeight(CableManufacturingDraft draft, ComponentPort port, SchematicGridBounds bounds, double minimumHeight)
    {
        if (bounds.Height >= minimumHeight) return bounds;
        var expanded = bounds with { Height = minimumHeight };
        foreach (var pin in port.Pins.Where(pin => draft.WiringDiagram!.PinPositions.ContainsKey(pin.PinId)))
        {
            var point = draft.WiringDiagram!.PinPositions[pin.PinId];
            if (point.Y != bounds.Y + bounds.Height) continue;
            draft.WiringDiagram.PinPositions[pin.PinId] = point with { Y = expanded.Y + expanded.Height };
            ClearAffectedRoutes(draft, pin.PinId);
        }
        draft.WiringDiagram!.PortBounds[port.PortId] = expanded;
        return expanded;
    }
    private static CablePinUsage Usage(CableManufacturingDraft draft, string id) => draft.Rows.SelectMany(CableManufacturingCells.All)
        .FirstOrDefault(c => c.SourcePinId == id)?.Usage ?? CablePinUsage.Pending;
    private static void RequireVisual(CableManufacturingDraft draft)
    {
        if (!draft.InventoryOnly || draft.ExplicitMapping is null) throw new InvalidOperationException("請先進入線材接線圖模式。");
    }
    private static void RequirePin(CableManufacturingDraft draft, string id)
    {
        if (!draft.Ends.Any(p => p.Pins.Any(pin => pin.PinId == id))) throw new InvalidOperationException("指定的 Pin 不存在。");
    }
    private static bool Finite(params double[] values) => values.All(double.IsFinite) && values.All(v => Math.Abs(v) <= 100000);
    private static void CheckPoint(SchematicPoint point)
    {
        if (!Finite(point.X, point.Y)) throw new InvalidOperationException("無效的接線圖座標。");
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
