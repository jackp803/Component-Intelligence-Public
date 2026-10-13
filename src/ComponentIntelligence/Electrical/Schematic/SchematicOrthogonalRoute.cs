namespace ComponentIntelligence.Electrical.Schematic;

internal static class SchematicOrthogonalRoute
{
    public static List<SchematicPoint> Between(SchematicPoint start, SchematicPoint end,
        SchematicPage page, IReadOnlyList<SchematicGridBounds> bodies)
    {
        var obstacles = bodies.Select(b => new SchematicGridBounds(b.X - 2, b.Y - 2, b.Width + 4, b.Height + 4)).ToArray();
        bool Inside(SchematicPoint p) => obstacles.Any(b => p.X > b.X && p.X < b.X + b.Width && p.Y > b.Y && p.Y < b.Y + b.Height);
        bool Clear(SchematicPoint a, SchematicPoint b) => !obstacles.Any(r => a.Y == b.Y
            ? a.Y > r.Y && a.Y < r.Y + r.Height && Math.Min(a.X, b.X) < r.X + r.Width && Math.Max(a.X, b.X) > r.X
            : a.X > r.X && a.X < r.X + r.Width && Math.Min(a.Y, b.Y) < r.Y + r.Height && Math.Max(a.Y, b.Y) > r.Y);
        bool OnPage(SchematicPoint p) => p.X >= page.Margin && p.Y >= page.Margin &&
            p.X <= page.Width - page.Margin && p.Y <= page.Height - page.Margin;
        if (start == end) return [start];
        if (Inside(start) || Inside(end) || !OnPage(start) || !OnPage(end))
            throw new InvalidOperationException("接點外側空間不足，請拉開模塊或調整接點位置。");
        if ((start.X == end.X || start.Y == end.Y) && Clear(start, end)) return [start, end];
        foreach (var corner in new[] { new SchematicPoint((start.X + end.X) / 2, start.Y), new(end.X, start.Y), new(start.X, end.Y) })
        {
            var route = corner.Y == start.Y && corner.X != end.X && corner.X != start.X
                ? new[] { start, corner, new SchematicPoint(corner.X, end.Y), end } : [start, corner, end];
            if (route.All(p => !Inside(p) && OnPage(p)) && route.Zip(route.Skip(1)).All(e => Clear(e.First, e.Second))) return route.ToList();
        }

        // A sparse rectilinear visibility grid is only needed when the simple paths meet a module.
        var xs = obstacles.SelectMany(b => new[] { b.X, b.X + b.Width }).Concat([start.X, end.X, page.Margin, page.Width - page.Margin])
            .Where(x => x >= page.Margin && x <= page.Width - page.Margin).Distinct().Order().ToArray();
        var ys = obstacles.SelectMany(b => new[] { b.Y, b.Y + b.Height }).Concat([start.Y, end.Y, page.Margin, page.Height - page.Margin])
            .Where(y => y >= page.Margin && y <= page.Height - page.Margin).Distinct().Order().ToArray();
        var count = xs.Length * ys.Length;
        var distance = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
        var previous = Enumerable.Repeat(-1, count).ToArray();
        int Index(SchematicPoint p) => Array.IndexOf(ys, p.Y) * xs.Length + Array.IndexOf(xs, p.X);
        SchematicPoint Point(int i) => new(xs[i % xs.Length], ys[i / xs.Length]);
        var from = Index(start); var to = Index(end);
        var queue = new PriorityQueue<int, double>(); distance[from] = 0; queue.Enqueue(from, 0);
        while (queue.TryDequeue(out var current, out var cost))
        {
            if (cost > distance[current]) continue;
            if (current == to) break;
            var x = current % xs.Length; var y = current / xs.Length;
            foreach (var next in new[] { x > 0 ? current - 1 : -1, x + 1 < xs.Length ? current + 1 : -1,
                y > 0 ? current - xs.Length : -1, y + 1 < ys.Length ? current + xs.Length : -1 })
            {
                if (next < 0) continue;
                var a = Point(current); var b = Point(next);
                if (Inside(b) || !Clear(a, b)) continue;
                var total = cost + Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
                if (total >= distance[next]) continue;
                distance[next] = total; previous[next] = current; queue.Enqueue(next, total);
            }
        }
        if (previous[to] < 0) throw new InvalidOperationException("找不到避開模塊的走線空間，請調整模塊位置。");
        var result = new List<SchematicPoint>();
        for (var i = to; i >= 0; i = previous[i]) result.Add(Point(i));
        result.Reverse(); return result;
    }
}
