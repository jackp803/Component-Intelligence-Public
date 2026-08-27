using System.Security.Cryptography;
using System.Text;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Export;

/// <summary>
/// Single authority for the machine net identity used by AutoCAD evidence validation and staging.
/// One explicit topology NetId is preserved after trimming across its connected endpoint component.
/// A component without an explicit NetId is identified only by its deterministically sorted endpoint
/// set. Conflicting explicit identities remain unresolved so preflight callers can block staging.
/// </summary>
public static class AutocadMachineNetIdentityResolver
{
    public static string Resolve(ElectricalProject project, ElectricalConnection connection)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(connection);
        var component = Analyze(project).SingleOrDefault(candidate => candidate.Connections.Contains(connection));
        if (component is null)
            throw new ArgumentException("The connection must belong to the supplied electrical project.", nameof(connection));
        if (component.IsAmbiguous)
            throw new InvalidOperationException(
                $"Connected endpoint component has conflicting explicit net identities: {string.Join(", ", component.ExplicitNetIds)}.");
        return component.NetIdentity!;
    }

    public static IReadOnlyList<AutocadMachineNetComponentResolution> Analyze(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var adjacent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in project.Connections)
        {
            if (!adjacent.TryGetValue(connection.FromEndpointId, out var from))
                adjacent[connection.FromEndpointId] = from = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!adjacent.TryGetValue(connection.ToEndpointId, out var to))
                adjacent[connection.ToEndpointId] = to = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            from.Add(connection.ToEndpointId);
            to.Add(connection.FromEndpointId);
        }

        var allVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AutocadMachineNetComponentResolution>();
        foreach (var seed in adjacent.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (allVisited.Contains(seed)) continue;
            var endpointIds = ConnectedEndpointIds(adjacent, seed);
            allVisited.UnionWith(endpointIds);
            var componentConnections = project.Connections
                .Where(connection => endpointIds.Contains(connection.FromEndpointId))
                .ToArray();
            var explicitNetIds = componentConnections
                .Select(connection => connection.NetId)
                .Where(netId => !string.IsNullOrWhiteSpace(netId))
                .Select(netId => netId!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(netId => netId, StringComparer.Ordinal)
                .ToArray();
            var orderedEndpointIds = endpointIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            result.Add(new AutocadMachineNetComponentResolution
            {
                Connections = componentConnections,
                ConnectedEndpointIds = orderedEndpointIds,
                ExplicitNetIds = explicitNetIds,
                NetIdentity = explicitNetIds.Length switch
                {
                    0 => DeriveIdentity(orderedEndpointIds),
                    1 => explicitNetIds[0],
                    _ => null
                }
            });
        }
        return result;
    }

    private static IReadOnlySet<string> ConnectedEndpointIds(
        IReadOnlyDictionary<string, HashSet<string>> adjacent,
        string seed)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(seed);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current) || !adjacent.TryGetValue(current, out var neighbours)) continue;
            foreach (var neighbour in neighbours.OrderBy(id => id, StringComparer.Ordinal)) pending.Enqueue(neighbour);
        }
        return visited;
    }

    private static string DeriveIdentity(IEnumerable<string> orderedEndpointIds)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", orderedEndpointIds)));
        return $"NET-TBD-{Convert.ToHexString(hash)[..12]}";
    }
}

public sealed record AutocadMachineNetComponentResolution
{
    public required IReadOnlyList<ElectricalConnection> Connections { get; init; }
    public required IReadOnlyList<string> ConnectedEndpointIds { get; init; }
    public required IReadOnlyList<string> ExplicitNetIds { get; init; }
    public required string? NetIdentity { get; init; }
    public bool IsAmbiguous => ExplicitNetIds.Count > 1;
}
