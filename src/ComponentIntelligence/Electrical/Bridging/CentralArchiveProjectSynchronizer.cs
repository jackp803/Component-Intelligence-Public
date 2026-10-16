using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Bridging;

public sealed record CentralArchiveSyncResult(
    int UpdatedInstances,
    int UpdatedDefinitions,
    IReadOnlyList<string> MissingInstanceIds);

/// <summary>
/// Refreshes central-library knowledge on existing instances without replacing project identity,
/// quantity, endpoint IDs, wiring, topology routes, placements, or physical placements.
/// </summary>
public sealed class CentralArchiveProjectSynchronizer
{
    private readonly ComponentInstanceKnowledgeSynchronizer _instanceSynchronizer = new();

    public CentralArchiveSyncResult Synchronize(ElectricalProject project, IEnumerable<ComponentIR> archiveComponents)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(archiveComponents);

        var archive = archiveComponents.ToArray();
        var byId = archive
            .GroupBy(component => Normalize(component.Identity.ComponentId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byDisplayName = archive
            .GroupBy(component => Normalize($"{component.Identity.Manufacturer} {component.Identity.Model}"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var updatedDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var updated = 0;
        foreach (var instance in project.Components)
        {
            if (!byId.TryGetValue(Normalize(instance.ComponentDefinitionId), out var source) &&
                (string.IsNullOrWhiteSpace(instance.DisplayName) ||
                 !byDisplayName.TryGetValue(Normalize(instance.DisplayName), out source)))
            {
                missing.Add(instance.ComponentInstanceId);
                continue;
            }

            _instanceSynchronizer.Apply(instance, source, overwriteExistingKnowledge: true);
            updated++;
            updatedDefinitions.Add(source.Identity.ComponentId);
        }

        return new CentralArchiveSyncResult(updated, updatedDefinitions.Count, missing);
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
