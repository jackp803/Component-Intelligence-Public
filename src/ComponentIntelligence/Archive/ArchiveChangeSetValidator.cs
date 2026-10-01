namespace ComponentIntelligence.Archive;

public sealed class ArchiveChangeSetValidator
{
    public ArchiveValidationReport Validate(ArchiveChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        var issues = new List<ArchiveValidationIssue>();

        ValidateIdentity(changeSet, issues);
        ValidateOperation(changeSet, issues);
        ValidateDuplicateIds(changeSet, issues);
        ValidateOwnershipAndStableIds(changeSet, issues);
        ValidatePinsAndReadiness(changeSet, issues);
        ValidatePaths(changeSet, issues);
        ValidateDeclaredConflicts(changeSet, issues);

        var status = issues.Any(issue => issue.Severity == ArchiveValidationSeverity.ERROR)
            ? ArchiveValidationStatus.REJECT
            : issues.Any(issue => issue.Severity == ArchiveValidationSeverity.REVIEW)
                ? ArchiveValidationStatus.REVIEW_REQUIRED
                : ArchiveValidationStatus.PASS;

        return new ArchiveValidationReport
        {
            JobId = changeSet.JobId,
            Status = status,
            Issues = issues
        };
    }

    private static void ValidateIdentity(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(changeSet.Manufacturer) || string.IsNullOrWhiteSpace(changeSet.Model))
        {
            Add(issues, "ARCHIVE-IDENTITY-001", ArchiveValidationSeverity.ERROR,
                "Manufacturer + exact Model / Part Number are required for a formal archive identity.");
        }

        if (changeSet.Components.Count != 1)
        {
            Add(issues, "ARCHIVE-IDENTITY-003", ArchiveValidationSeverity.ERROR,
                "A changeset targets exactly one Manufacturer + Model and must contain exactly one Component row.");
        }

        foreach (var component in changeSet.Components)
        {
            if (string.IsNullOrWhiteSpace(component.ComponentId) ||
                string.IsNullOrWhiteSpace(component.Manufacturer) ||
                string.IsNullOrWhiteSpace(component.Model))
            {
                Add(issues, "ARCHIVE-IDENTITY-001", ArchiveValidationSeverity.ERROR,
                    "ComponentID, Manufacturer, and Model are required.", component.ComponentId);
                continue;
            }

            if (!string.Equals(component.Manufacturer, changeSet.Manufacturer, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(component.Model, changeSet.Model, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "ARCHIVE-IDENTITY-002", ArchiveValidationSeverity.ERROR,
                    "Component identity does not match the changeset target Manufacturer + Model.", component.ComponentId);
            }
        }
    }

    private static void ValidateOperation(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        if (changeSet.Operation == ArchiveOperation.CREATE && !string.IsNullOrWhiteSpace(changeSet.ExistingComponentId))
        {
            Add(issues, "ARCHIVE-OP-001", ArchiveValidationSeverity.ERROR,
                "CREATE cannot target an identity already reported by ArchiveLookup.", changeSet.ExistingComponentId!);
        }
    }

    private static void ValidateDuplicateIds(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        AddDuplicates(changeSet.Components.Select(component => component.ComponentId), "ARCHIVE-ID-001", "Duplicate ComponentID.", issues);

        var ports = changeSet.Components.SelectMany(component => component.Ports).ToList();
        AddDuplicates(ports.Select(port => port.PortId), "ARCHIVE-ID-002", "Duplicate PortID.", issues);

        var pins = ports.SelectMany(port => port.Pins).ToList();
        AddDuplicates(pins.Select(pin => pin.PinId), "ARCHIVE-ID-003", "Duplicate PinID.", issues);
    }

    private static void AddDuplicates(
        IEnumerable<string> ids,
        string ruleId,
        string message,
        ICollection<ArchiveValidationIssue> issues)
    {
        foreach (var group in ids
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Add(issues, ruleId, ArchiveValidationSeverity.ERROR, message, group.ToArray());
        }
    }

    private static void ValidateOwnershipAndStableIds(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        foreach (var component in changeSet.Components)
        {
            if (!string.IsNullOrWhiteSpace(component.PriorStableComponentId) &&
                !string.Equals(component.PriorStableComponentId, component.ComponentId, StringComparison.Ordinal))
            {
                Add(issues, "ARCHIVE-STABLE-ID-001", ArchiveValidationSeverity.ERROR,
                    "ComponentID mutation requires an explicit identity migration.", component.PriorStableComponentId!, component.ComponentId);
            }

            foreach (var port in component.Ports)
            {
                if (string.IsNullOrWhiteSpace(port.PortId) || string.IsNullOrWhiteSpace(port.PortName))
                {
                    Add(issues, "ARCHIVE-PORT-001", ArchiveValidationSeverity.ERROR,
                        "PortID and PortName must be non-empty.", component.ComponentId, port.PortId);
                }

                if (!string.Equals(port.ComponentId, component.ComponentId, StringComparison.OrdinalIgnoreCase))
                {
                    Add(issues, "ARCHIVE-OWNER-001", ArchiveValidationSeverity.ERROR,
                        "Port owner does not match its parent ComponentID.", component.ComponentId, port.PortId, port.ComponentId);
                }

                if (!string.IsNullOrWhiteSpace(port.PriorStablePortId) &&
                    !string.Equals(port.PriorStablePortId, port.PortId, StringComparison.Ordinal))
                {
                    Add(issues, "ARCHIVE-STABLE-ID-002", ArchiveValidationSeverity.ERROR,
                        "PortID mutation requires an explicit identity migration.", port.PriorStablePortId!, port.PortId);
                }

                foreach (var pin in port.Pins)
                {
                    if (string.IsNullOrWhiteSpace(pin.PinId) || string.IsNullOrWhiteSpace(pin.PinNumber))
                    {
                        Add(issues, "ARCHIVE-PIN-003", ArchiveValidationSeverity.ERROR,
                            "PinID and PinNumber/contact identifier must be non-empty.", port.PortId, pin.PinId);
                    }

                    if (!string.Equals(pin.PortId, port.PortId, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(issues, "ARCHIVE-OWNER-002", ArchiveValidationSeverity.ERROR,
                            "Pin owner does not match its parent PortID.", port.PortId, pin.PinId, pin.PortId);
                    }

                    if (!string.IsNullOrWhiteSpace(pin.PriorStablePinId) &&
                        !string.Equals(pin.PriorStablePinId, pin.PinId, StringComparison.Ordinal))
                    {
                        Add(issues, "ARCHIVE-STABLE-ID-003", ArchiveValidationSeverity.ERROR,
                            "PinID mutation requires an explicit identity migration.", pin.PriorStablePinId!, pin.PinId);
                    }
                }
            }
        }
    }

    private static void ValidatePinsAndReadiness(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        foreach (var component in changeSet.Components)
        {
            var componentHasCompletenessError = false;

            foreach (var port in component.Ports)
            {
                var actual = port.Pins.Count;
                if (port.PinCount is int expected && (expected < 0 || expected != actual))
                {
                    componentHasCompletenessError = true;
                    Add(issues, "ARCHIVE-PIN-001", ArchiveValidationSeverity.ERROR,
                        $"Known PinCount requires exactly that many physical Pin rows: expected {expected}, actual {actual}.",
                        component.ComponentId, port.PortId);
                }

                if (port.ActualPinCount is int declaredActual && declaredActual != actual)
                {
                    componentHasCompletenessError = true;
                    Add(issues, "ARCHIVE-PIN-004", ArchiveValidationSeverity.ERROR,
                        $"ActualPinCount must match the number of archived Pin rows: declared {declaredActual}, actual {actual}.",
                        component.ComponentId, port.PortId);
                }

                foreach (var duplicate in port.Pins
                             .Where(pin => !string.IsNullOrWhiteSpace(pin.PinNumber))
                             .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
                             .Where(group => group.Count() > 1))
                {
                    Add(issues, "ARCHIVE-PIN-002", ArchiveValidationSeverity.ERROR,
                        $"Duplicate PinNumber/contact identifier '{duplicate.Key}' within one Port.",
                        duplicate.Select(pin => pin.PinId).Prepend(port.PortId).ToArray());
                }

                foreach (var pin in port.Pins.Where(pin => pin.PinStatus is ArchivePinStatus.NC or ArchivePinStatus.Reserved))
                {
                    if (!HasExplicitEvidence(pin))
                    {
                        Add(issues, "ARCHIVE-EVIDENCE-001", ArchiveValidationSeverity.REVIEW,
                            $"{pin.PinStatus} requires explicit evidence; absence of a function is not proof.",
                            component.ComponentId, port.PortId, pin.PinId);
                    }
                }

                if (component.TopologyStatus == ArchiveTopologyStatus.Ready && port.TopologyEndpointMode is null)
                {
                    Add(issues, "ARCHIVE-READY-002", ArchiveValidationSeverity.REVIEW,
                        "TopologyStatus=Ready requires an explicit Connector vs Pins endpoint mode.",
                        component.ComponentId, port.PortId);
                }
            }

            if (component.TopologyStatus == ArchiveTopologyStatus.Ready && componentHasCompletenessError)
            {
                Add(issues, "ARCHIVE-READY-001", ArchiveValidationSeverity.ERROR,
                    "TopologyStatus=Ready is inconsistent with incomplete physical Pin coverage.", component.ComponentId);
            }
        }
    }

    private static bool HasExplicitEvidence(ArchivePinChange pin)
    {
        if (!string.IsNullOrWhiteSpace(pin.SourcePage))
            return true;

        return pin.Evidence.Any(evidence =>
            !string.IsNullOrWhiteSpace(evidence.EvidenceId) ||
            !string.IsNullOrWhiteSpace(evidence.SourceUrl) ||
            !string.IsNullOrWhiteSpace(evidence.DocumentPath));
    }

    private static void ValidatePaths(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        foreach (var path in changeSet.DocumentPaths)
            ValidatePath(path, "changeset", issues);

        foreach (var component in changeSet.Components)
        {
            ValidatePath(component.DatasheetPath, component.ComponentId, issues);
            ValidatePath(component.ImagePath, component.ComponentId, issues);
            ValidatePath(component.DrawingPath, component.ComponentId, issues);

            foreach (var evidence in component.Evidence)
                ValidatePath(evidence.DocumentPath, component.ComponentId, issues);

            foreach (var port in component.Ports)
            foreach (var pin in port.Pins)
            foreach (var evidence in pin.Evidence)
                ValidatePath(evidence.DocumentPath, pin.PinId, issues);
        }
    }

    private static void ValidatePath(string? path, string owner, ICollection<ArchiveValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var normalized = path.Replace('\\', '/');
        var windowsRooted = normalized.Length >= 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/';
        var rooted = normalized.StartsWith("/", StringComparison.Ordinal) || windowsRooted || normalized.StartsWith("//", StringComparison.Ordinal);
        var escapes = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");
        var underDocuments = normalized.StartsWith("Documents/", StringComparison.OrdinalIgnoreCase);

        if (rooted || escapes || !underDocuments)
        {
            Add(issues, "ARCHIVE-PATH-001", ArchiveValidationSeverity.ERROR,
                "Archived file paths must be relative paths under Documents/<Manufacturer>/<Model>/.", owner, path);
        }
    }

    private static void ValidateDeclaredConflicts(ArchiveChangeSet changeSet, ICollection<ArchiveValidationIssue> issues)
    {
        if (changeSet.Conflicts.Count > 0)
        {
            Add(issues, "ARCHIVE-CONFLICT-001", ArchiveValidationSeverity.REVIEW,
                "Changeset contains unresolved source conflicts.", changeSet.Conflicts.ToArray());
        }

        if (changeSet.UnresolvedUnknowns.Count > 0)
        {
            Add(issues, "ARCHIVE-UNKNOWN-001", ArchiveValidationSeverity.WARNING,
                "Changeset records unresolved engineering unknowns.", changeSet.UnresolvedUnknowns.ToArray());
        }
    }

    private static void Add(
        ICollection<ArchiveValidationIssue> issues,
        string ruleId,
        ArchiveValidationSeverity severity,
        string message,
        params string[] objectIds)
    {
        issues.Add(new ArchiveValidationIssue
        {
            RuleId = ruleId,
            Severity = severity,
            Message = message,
            ObjectIds = objectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        });
    }
}
