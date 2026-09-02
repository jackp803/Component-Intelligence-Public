using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Resolution;

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
                 !byDisplayName.TryGetValue(Normalize(instance.DisplayName), out source)) &&
                !TryResolveBomIdentity(instance, archive, out source))
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

    private static bool TryResolveBomIdentity(
        ComponentInstance instance,
        IReadOnlyList<ComponentIR> archive,
        out ComponentIR source)
    {
        source = null!;
        if (!TryReadBomIdentity(instance.ComponentInstanceId, out var manufacturer, out var model))
            return false;

        var manufacturerKey = ManufacturerKey(manufacturer);
        var modelKey = Normalize(model);
        var candidates = archive
            .Where(component => string.Equals(
                ManufacturerKey(component.Identity.Manufacturer),
                manufacturerKey,
                StringComparison.OrdinalIgnoreCase))
            .Where(component => ModelMatches(modelKey, Normalize(component.Identity.Model)))
            .Take(2)
            .ToArray();

        // A suffix match is allowed only when it resolves to one archive identity. This supports
        // historical BOM family prefixes such as "CRNE 3-3 ..." versus archive model "3-3 ..."
        // without turning central synchronization into an unsafe fuzzy-model lookup.
        if (candidates.Length != 1) return false;
        source = candidates[0];
        return true;
    }

    private static bool TryReadBomIdentity(string instanceId, out string manufacturer, out string model)
    {
        manufacturer = string.Empty;
        model = string.Empty;
        var segments = instanceId.Split(':', StringSplitOptions.None);
        if (segments.Length != 5 || !string.Equals(segments[0], "bom", StringComparison.OrdinalIgnoreCase))
            return false;

        manufacturer = segments[2].Replace('-', ' ');
        model = segments[3].Replace('-', ' ');
        return !string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(model);
    }

    private static string ManufacturerKey(string value) =>
        Normalize(ManufacturerNormalizer.NormalizeKey(value) ?? value);

    private static bool ModelMatches(string bomModelKey, string archiveModelKey)
    {
        if (string.Equals(bomModelKey, archiveModelKey, StringComparison.OrdinalIgnoreCase)) return true;

        // Do not suffix-match short/generic model tokens (for example "200").
        const int minimumSpecificModelLength = 8;
        return archiveModelKey.Length >= minimumSpecificModelLength &&
               bomModelKey.Length > archiveModelKey.Length &&
               bomModelKey.EndsWith(archiveModelKey, StringComparison.OrdinalIgnoreCase);
    }
}
