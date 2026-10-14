namespace ComponentIntelligence.Electrical.Schematic;

internal static class SchematicOrthogonalRoute
{
    public static List<SchematicPoint> Between(SchematicPoint start, SchematicPoint end,
        SchematicPage page, IReadOnlyList<SchematicGridBounds> bodies, IReadOnlyList<SchematicWire>? reserved = null,
        IReadOnlyList<SchematicWire>? exitStubs = null)
    {
        var obstacles = bodies.Select(b => new SchematicGridBounds(b.X - 2, b.Y - 2, b.Width + 4, b.Height + 4)).ToArray();
        bool InsideCoordinates(double x, double y)
        {
            foreach (var b in obstacles)
                if (x > b.X && x < b.X + b.Width && y > b.Y && y < b.Y + b.Height) return true;
            return false;
        }
        bool Inside(SchematicPoint p) => InsideCoordinates(p.X, p.Y);
        var segments = (reserved ?? []).SelectMany(w => w.Points.Zip(w.Points.Skip(1))).ToArray();
        var stubSegments = (exitStubs ?? []).SelectMany(w => w.Points.Zip(w.Points.Skip(1))).ToArray();
        var visibilitySegments = segments.Concat(stubSegments).ToArray();
        var horizontalReservations = new ReservedAxis(segments, stubSegments, horizontal: true);
        var verticalReservations = new ReservedAxis(segments, stubSegments, horizontal: false);
        bool ClearCoordinates(double ax, double ay, double bx, double by)
        {
            var horizontal = ay == by;
            var low = horizontal ? Math.Min(ax, bx) : Math.Min(ay, by);
            var high = horizontal ? Math.Max(ax, bx) : Math.Max(ay, by);
            foreach (var r in obstacles)
                if (horizontal ? ay > r.Y && ay < r.Y + r.Height && low < r.X + r.Width && high > r.X
                    : ax > r.X && ax < r.X + r.Width && low < r.Y + r.Height && high > r.Y) return false;
            return !(horizontal ? horizontalReservations : verticalReservations).Blocks(horizontal ? ay : ax, low, high);
        }
        bool Clear(SchematicPoint a, SchematicPoint b) => ClearCoordinates(a.X, a.Y, b.X, b.Y);
        bool OnPage(SchematicPoint p) => p.X >= page.Margin && p.Y >= page.Margin &&
            p.X <= page.Width - page.Margin && p.Y <= page.Height - page.Margin;
        if (start == end) return [start];
        if (Inside(start) || Inside(end) || !OnPage(start) || !OnPage(end))
            throw new InvalidOperationException("接點外側空間不足，請拉開模塊或調整接點位置。");
        if ((start.X == end.X || start.Y == end.Y) && Clear(start, end)) return [start, end];
        var middleX = Math.Round((start.X + end.X) / 8) * 4;
        var candidates = Enumerable.Range(0, visibilitySegments.Length + 2).SelectMany(i => i == 0 ? new[] { middleX } : new[] { middleX - i * 4, middleX + i * 4 })
            .Where(x => x > Math.Min(start.X, end.X) && x < Math.Max(start.X, end.X))
            .Select(x => new SchematicPoint(x, start.Y)).Concat([new(end.X, start.Y), new(start.X, end.Y)]);
        foreach (var corner in candidates)
        {
            var route = corner.Y == start.Y && corner.X != end.X && corner.X != start.X
                ? new[] { start, corner, new SchematicPoint(corner.X, end.Y), end } : [start, corner, end];
            if (route.All(p => !Inside(p) && OnPage(p)) && route.Zip(route.Skip(1)).All(e => Clear(e.First, e.Second))) return route.ToList();
        }

        // A sparse rectilinear visibility grid is only needed when the simple paths meet a module.
        var lane = (start.X + end.X) / 2;
        var xs = obstacles.SelectMany(b => new[] { b.X, b.X + b.Width })
            .Concat(visibilitySegments.SelectMany(s => new[] { s.First.X - 4, s.First.X, s.First.X + 4, s.Second.X - 4, s.Second.X, s.Second.X + 4 }))
            .Concat([start.X, end.X, lane - 4, lane + 4, page.Margin, page.Width - page.Margin])
            .Where(x => x >= page.Margin && x <= page.Width - page.Margin).Distinct().Order().ToArray();
        var ys = obstacles.SelectMany(b => new[] { b.Y, b.Y + b.Height })
            .Concat(visibilitySegments.SelectMany(s => new[] { s.First.Y - 4, s.First.Y, s.First.Y + 4, s.Second.Y - 4, s.Second.Y, s.Second.Y + 4 }))
            .Concat([start.Y, end.Y, page.Margin, page.Height - page.Margin])
            .Where(y => y >= page.Margin && y <= page.Height - page.Margin).Distinct().Order().ToArray();
        var nodeCount = (long)xs.Length * ys.Length;
        if (nodeCount > 1_000_000)
            throw new InvalidOperationException($"Routing grid requires {nodeCount} nodes and exceeds the 1000000-node limit. " +
                "Reduce imported CAD detail or split the scene into separate sheets before routing.");
        var count = (int)nodeCount;
        var distance = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
        var previous = Enumerable.Repeat(-1, count).ToArray();
        int Index(SchematicPoint p) => Array.IndexOf(ys, p.Y) * xs.Length + Array.IndexOf(xs, p.X);
        SchematicPoint Point(int i) => new(xs[i % xs.Length], ys[i / xs.Length]);
        var from = Index(start); var to = Index(end);
        double Remaining(int i) => Math.Abs(xs[i % xs.Length] - end.X) + Math.Abs(ys[i / xs.Length] - end.Y);
        // Manhattan distance is admissible on this orthogonal grid. Prefer the nearer goal
        // among equal estimates so an open sheet does not expand every shortest-path node.
        var queue = new PriorityQueue<int, (double Estimate, double Remaining)>();
        distance[from] = 0;
        var initialRemaining = Remaining(from);
        queue.Enqueue(from, (initialRemaining, initialRemaining));
        while (queue.TryDequeue(out var current, out var priority))
        {
            var cost = distance[current];
            if (priority.Estimate > cost + Remaining(current)) continue;
            if (current == to) break;
            var x = current % xs.Length; var y = current / xs.Length;
            if (x > 0) Visit(current - 1, x - 1, y);
            if (x + 1 < xs.Length) Visit(current + 1, x + 1, y);
            if (y > 0) Visit(current - xs.Length, x, y - 1);
            if (y + 1 < ys.Length) Visit(current + xs.Length, x, y + 1);

            void Visit(int next, int nx, int ny)
            {
                var bx = xs[nx]; var by = ys[ny];
                if (InsideCoordinates(bx, by) || !ClearCoordinates(xs[x], ys[y], bx, by)) return;
                var total = cost + Math.Abs(bx - xs[x]) + Math.Abs(by - ys[y]);
                if (total >= distance[next]) return;
                distance[next] = total; previous[next] = current;
                var remaining = Math.Abs(bx - end.X) + Math.Abs(by - end.Y);
                queue.Enqueue(next, (total + remaining, remaining));
            }
        }
        if (previous[to] < 0) throw new InvalidOperationException("找不到避開模塊的走線空間，請調整模塊位置。");
        var result = new List<SchematicPoint>();
        for (var i = to; i >= 0; i = previous[i]) result.Add(Point(i));
        result.Reverse(); return result;
    }

    private sealed class ReservedAxis
    {
        private readonly double[] _lanes;
        private readonly IntervalBucket[] _intervals;
        private readonly Dictionary<double, double[]> _perpendicularEndpoints;

        public ReservedAxis((SchematicPoint First, SchematicPoint Second)[] segments,
            (SchematicPoint First, SchematicPoint Second)[] exitStubs, bool horizontal)
        {
            var lanes = new Dictionary<double, List<(double Low, double High)>>();
            var endpoints = new Dictionary<double, List<double>>();
            foreach (var (a, b) in segments) AddSegment(a, b, includeEndpoints: true);
            // Owned stubs prohibit retracing, but their perpendicular joins at the route's
            // start/end are intentional and must not become unrelated-wire blockers.
            foreach (var (a, b) in exitStubs) AddSegment(a, b, includeEndpoints: false);
            _lanes = lanes.Keys.Order().ToArray();
            _intervals = _lanes.Select(lane => new IntervalBucket(lanes[lane])).ToArray();
            _perpendicularEndpoints = endpoints.ToDictionary(e => e.Key, e => e.Value.Order().ToArray());

            void AddSegment(SchematicPoint a, SchematicPoint b, bool includeEndpoints)
            {
                if (a == b) return;
                if ((a.Y == b.Y) == horizontal)
                {
                    var lane = horizontal ? a.Y : a.X;
                    if (!lanes.TryGetValue(lane, out var intervals)) lanes.Add(lane, intervals = []);
                    intervals.Add(horizontal ? (Math.Min(a.X, b.X), Math.Max(a.X, b.X))
                        : (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                }
                else if (includeEndpoints)
                {
                    AddEndpoint(a); AddEndpoint(b);
                }
            }
            void AddEndpoint(SchematicPoint p)
            {
                var lane = horizontal ? p.Y : p.X;
                if (!endpoints.TryGetValue(lane, out var coordinates)) endpoints.Add(lane, coordinates = []);
                coordinates.Add(horizontal ? p.X : p.Y);
            }
        }

        public bool Blocks(double lane, double low, double high)
        {
            if (low == high) return false;
            for (var i = LowerBound(_lanes, lane - 2); i < _lanes.Length && _lanes[i] <= lane + 2; i++)
                if (Math.Abs(lane - _lanes[i]) < 2 && _intervals[i].Overlaps(low, high)) return true;
            // Only reserved perpendicular endpoints block a crossing. Its interior remains
            // available for the existing nonconductive crossover presentation.
            if (!_perpendicularEndpoints.TryGetValue(lane, out var endpoints)) return false;
            var index = LowerBound(endpoints, low);
            return index < endpoints.Length && endpoints[index] <= high;
        }
    }

    private sealed class IntervalBucket
    {
        private readonly double[] _starts;
        private readonly double[] _prefixEnds;

        public IntervalBucket(List<(double Low, double High)> intervals)
        {
            var sorted = intervals.OrderBy(i => i.Low).ToArray();
            _starts = new double[sorted.Length];
            _prefixEnds = new double[sorted.Length];
            var high = double.NegativeInfinity;
            for (var i = 0; i < sorted.Length; i++)
            {
                _starts[i] = sorted[i].Low;
                _prefixEnds[i] = high = Math.Max(high, sorted[i].High);
            }
        }

        public bool Overlaps(double low, double high)
        {
            var count = LowerBound(_starts, high);
            return count > 0 && _prefixEnds[count - 1] > low;
        }
    }

    private static int LowerBound(double[] values, double value)
    {
        var low = 0; var high = values.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] < value) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}
