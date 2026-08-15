using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Naming;

public sealed class NamingPolicy
{
    public int NumberPadding { get; init; } = 2;
    public Dictionary<string, string> PrefixByTypeKey { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ReferenceChange(string ObjectId, string? OldReference, string NewReference);

public sealed class NamingEngine
{
    public string AssignNextReference(ElectricalProject project, ComponentInstance component, NamingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(policy);

        if (component.ReferenceLocked && !string.IsNullOrWhiteSpace(component.ReferenceDesignator))
            return component.ReferenceDesignator;

        if (!policy.PrefixByTypeKey.TryGetValue(component.TypeKey, out var prefix) || string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException($"No naming prefix is configured for component type '{component.TypeKey}'.");

        var existing = project.Components
            .Where(candidate => !ReferenceEquals(candidate, component))
            .Select(candidate => candidate.ReferenceDesignator)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Cast<string>();

        var reference = NextReference(existing, prefix, policy.NumberPadding);
        component.ReferenceDesignator = reference;
        component.ReferenceSource = ReferenceSource.AutoAssigned;
        return reference;
    }

    public void SetManualReference(ElectricalProject project, ComponentInstance component, string reference)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        if (project.Components.Any(candidate =>
                !ReferenceEquals(candidate, component) &&
                string.Equals(candidate.ReferenceDesignator, reference, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Duplicate reference designator '{reference}'.");
        }

        component.ReferenceDesignator = reference.Trim();
        component.ReferenceSource = ReferenceSource.Manual;
        component.ReferenceLocked = true;
    }

    public IReadOnlyList<ReferenceChange> Renumber(ElectricalProject project, string typeKey, NamingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.PrefixByTypeKey.TryGetValue(typeKey, out var prefix) || string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException($"No naming prefix is configured for component type '{typeKey}'.");

        var candidates = project.Components
            .Where(component => string.Equals(component.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase) && !component.ReferenceLocked)
            .OrderBy(component => component.ReferenceDesignator, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.ComponentInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reserved = project.Components
            .Where(component => !candidates.Contains(component))
            .Select(component => component.ReferenceDesignator)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changes = new List<ReferenceChange>();
        var nextNumber = 1;
        foreach (var component in candidates)
        {
            string next;
            do
            {
                next = $"{prefix}{nextNumber.ToString().PadLeft(Math.Max(1, policy.NumberPadding), '0')}";
                nextNumber++;
            } while (reserved.Contains(next));

            changes.Add(new ReferenceChange(component.ComponentInstanceId, component.ReferenceDesignator, next));
            component.ReferenceDesignator = next;
            component.ReferenceSource = ReferenceSource.AutoAssigned;
            reserved.Add(next);
        }

        return changes;
    }

    public static string NextReference(IEnumerable<string> existingReferences, string prefix, int numberPadding)
    {
        ArgumentNullException.ThrowIfNull(existingReferences);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var max = 0;
        foreach (var reference in existingReferences)
        {
            if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = reference[prefix.Length..];
            if (int.TryParse(suffix, out var parsed) && parsed > max) max = parsed;
        }

        var next = max + 1;
        return $"{prefix}{next.ToString().PadLeft(Math.Max(1, numberPadding), '0')}";
    }
}
