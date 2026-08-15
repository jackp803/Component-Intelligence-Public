using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Connectivity;

public sealed record ConnectivityGroup(IReadOnlyList<string> EndpointIds);

public sealed class TerminalConnectivityEngine
{
    public IReadOnlyList<ConnectivityGroup> BuildGroups(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var terminalBlock in project.TerminalBlocks)
        foreach (var position in terminalBlock.Positions)
        foreach (var level in position.Levels)
        foreach (var point in level.ConnectionPoints)
            allIds.Add(point.ConnectionPointId);

        foreach (var connection in project.Connections)
        {
            allIds.Add(connection.FromEndpointId);
            allIds.Add(connection.ToEndpointId);
        }

        if (allIds.Count == 0) return Array.Empty<ConnectivityGroup>();

        var unionFind = new UnionFind(allIds);

        foreach (var terminalBlock in project.TerminalBlocks)
        {
            foreach (var position in terminalBlock.Positions)
            foreach (var level in position.Levels)
            foreach (var internalConnection in level.InternalConnections)
                unionFind.Union(internalConnection.FromConnectionPointId, internalConnection.ToConnectionPointId);

            foreach (var jumper in terminalBlock.Jumpers)
            {
                if (jumper.ConnectionPointIds.Count < 2) continue;
                var first = jumper.ConnectionPointIds[0];
                foreach (var pointId in jumper.ConnectionPointIds.Skip(1))
                    unionFind.Union(first, pointId);
            }
        }

        foreach (var connection in project.Connections)
            unionFind.Union(connection.FromEndpointId, connection.ToEndpointId);

        return allIds
            .GroupBy(unionFind.Find, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ConnectivityGroup(group.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(group => group.EndpointIds[0], StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool AreConnected(ElectricalProject project, string endpointA, string endpointB)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointA);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointB);
        return BuildGroups(project).Any(group =>
            group.EndpointIds.Contains(endpointA, StringComparer.OrdinalIgnoreCase) &&
            group.EndpointIds.Contains(endpointB, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class UnionFind
    {
        private readonly Dictionary<string, string> _parent;

        public UnionFind(IEnumerable<string> ids)
        {
            _parent = ids.Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);
        }

        public string Find(string id)
        {
            if (!_parent.ContainsKey(id)) _parent[id] = id;
            var parent = _parent[id];
            if (!string.Equals(parent, id, StringComparison.OrdinalIgnoreCase))
                _parent[id] = Find(parent);
            return _parent[id];
        }

        public void Union(string first, string second)
        {
            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (!string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase))
                _parent[secondRoot] = firstRoot;
        }
    }
}
