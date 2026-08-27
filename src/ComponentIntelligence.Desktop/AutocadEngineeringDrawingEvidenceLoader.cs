using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Reads engineer-approved drawing evidence without creating, repairing, inferring, or persisting it.
/// Every identity in the sidecar is checked against the currently open project before it can reach
/// the AutoCAD staging graph builder.
/// </summary>
public static class AutocadEngineeringDrawingEvidenceLoader
{
    public const string SchemaVersion = "ci-autocad-engineering-drawing-evidence.v1";
    public const string DefaultFileName = "autocad-engineering-drawing-evidence.v1.json";
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentIntelligence",
        DefaultFileName);

    public static AutocadEngineeringDrawingEvidenceLoadResult Load(
        ElectricalProject project,
        string? path = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var sidecarPath = path ?? DefaultPath;
        if (!File.Exists(sidecarPath))
            return Failure(sidecarPath, "EngineeringDrawingEvidenceSidecarMissing",
                $"Engineer-approved AutoCAD drawing evidence sidecar was not found: {sidecarPath}");

        try
        {
            var document = JsonSerializer.Deserialize<SidecarDocument>(File.ReadAllText(sidecarPath), JsonOptions);
            if (document is null || !string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(document.ProjectId) || document.ComponentRoles is null ||
                document.PowerFlowOrientations is null || document.CrossPageContinuations is null ||
                document.CableInstanceOverrides is null)
            {
                return Failure(sidecarPath, "EngineeringDrawingEvidenceSidecarInvalid",
                    $"Engineer-approved drawing evidence must use schema '{SchemaVersion}' and include projectId, componentRoles, powerFlowOrientations, crossPageContinuations, and cableInstanceOverrides.");
            }

            var issues = new List<AutocadReviewIssue>();
            if (!string.Equals(document.ProjectId.Trim(), project.ProjectId, StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("EngineeringDrawingEvidenceProjectMismatch",
                    $"Drawing evidence projectId '{document.ProjectId}' does not match the open project '{project.ProjectId}'.",
                    document.ProjectId, project.ProjectId));

            var componentIds = project.Components.Select(component => component.ComponentInstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var netComponents = AutocadMachineNetIdentityResolver.Analyze(project);
            foreach (var component in netComponents.Where(component => component.IsAmbiguous))
            {
                issues.Add(Error(
                    "EngineeringDrawingEvidenceConflictingNetIdentity",
                    $"Connected endpoint component [{string.Join(", ", component.ConnectedEndpointIds)}] declares conflicting explicit net identities [{string.Join(", ", component.ExplicitNetIds)}]; drawing evidence cannot select one.",
                    component.ConnectedEndpointIds.Concat(component.ExplicitNetIds).ToArray()));
            }
            var netIds = project.Nets.Select(net => net.NetId)
                .Concat(netComponents
                    .Where(component => !component.IsAmbiguous)
                    .Select(component => component.NetIdentity!))
                .ToHashSet(StringComparer.Ordinal);
            var cableIds = project.Cables.Select(cable => cable.CableInstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var endpointIds = project.Components.SelectMany(component => component.Ports)
                .SelectMany(port => port.Pins.Select(pin => pin.PinId).Append(port.PortId))
                .Concat(project.TerminalBlocks.SelectMany(block => block.Positions)
                    .SelectMany(position => position.Levels)
                    .SelectMany(level => level.ConnectionPoints)
                    .Select(point => point.ConnectionPointId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var roles = LoadComponentRoles(document.ComponentRoles, componentIds, issues);
            var power = LoadPowerFlow(document.PowerFlowOrientations, netIds, endpointIds, issues);
            var crossPage = LoadCrossPage(document.CrossPageContinuations, endpointIds, issues);
            var cables = LoadCableOverrides(document.CableInstanceOverrides, cableIds, issues);
            var pageHint = LoadPageHint(document.PageArchetypeHint, issues);

            return new AutocadEngineeringDrawingEvidenceLoadResult(
                sidecarPath,
                new AutocadEngineeringDrawingEvidence
                {
                    ComponentRoles = roles,
                    PageArchetypeHint = pageHint,
                    PowerFlowOrientations = power,
                    CrossPageContinuations = crossPage,
                    CableInstanceOverrides = cables
                },
                issues);
        }
        catch (JsonException exception)
        {
            return Failure(sidecarPath, "EngineeringDrawingEvidenceSidecarInvalid",
                $"Engineer-approved drawing evidence is not valid '{SchemaVersion}' JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            return Failure(sidecarPath, "EngineeringDrawingEvidenceSidecarUnreadable",
                $"Engineer-approved drawing evidence could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(sidecarPath, "EngineeringDrawingEvidenceSidecarUnreadable",
                $"Engineer-approved drawing evidence could not be read: {exception.Message}");
        }
    }

    private static IReadOnlyList<AutocadComponentDrawingRoleEvidence> LoadComponentRoles(
        IEnumerable<ComponentRoleDocument?> documents,
        IReadOnlySet<string> componentIds,
        ICollection<AutocadReviewIssue> issues)
    {
        var result = new List<AutocadComponentDrawingRoleEvidence>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.ComponentInstanceId) ||
                document.Role is null || document.Status is null)
            {
                issues.Add(Error("EngineeringDrawingEvidenceSidecarInvalid",
                    "Each component role requires componentInstanceId, role, and status."));
                continue;
            }

            var componentId = document.ComponentInstanceId.Trim();
            if (!identities.Add(componentId))
            {
                issues.Add(Error("EngineeringDrawingEvidenceDuplicateComponentRole",
                    $"Drawing evidence has more than one component role for '{componentId}'.", componentId));
                continue;
            }
            if (!componentIds.Contains(componentId))
                issues.Add(Error("EngineeringDrawingEvidenceUnknownComponent",
                    $"Drawing evidence references unknown component '{componentId}'.", componentId));
            result.Add(new AutocadComponentDrawingRoleEvidence
            {
                ComponentInstanceId = componentId,
                Role = document.Role.Value,
                Status = document.Status.Value,
                EvidenceSource = NormalizeOptional(document.EvidenceSource)
            });
        }
        return result;
    }

    private static IReadOnlyList<AutocadPowerFlowOrientationEvidence> LoadPowerFlow(
        IEnumerable<PowerFlowDocument?> documents,
        IReadOnlySet<string> netIds,
        IReadOnlySet<string> endpointIds,
        ICollection<AutocadReviewIssue> issues)
    {
        var result = new List<AutocadPowerFlowOrientationEvidence>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.NetIdentity) ||
                string.IsNullOrWhiteSpace(document.SourceEndpointId) ||
                string.IsNullOrWhiteSpace(document.DestinationEndpointId) ||
                document.Orientation is null || document.Status is null)
            {
                issues.Add(Error("EngineeringDrawingEvidenceSidecarInvalid",
                    "Each power-flow orientation requires netIdentity, orientation, sourceEndpointId, destinationEndpointId, and status."));
                continue;
            }

            var netIdentity = document.NetIdentity.Trim();
            var source = document.SourceEndpointId.Trim();
            var destination = document.DestinationEndpointId.Trim();
            if (!identities.Add(netIdentity))
            {
                issues.Add(Error("EngineeringDrawingEvidenceDuplicatePowerFlow",
                    $"Drawing evidence has more than one power-flow orientation for net '{netIdentity}'.", netIdentity));
                continue;
            }
            if (!netIds.Contains(netIdentity))
                issues.Add(Error("EngineeringDrawingEvidenceUnknownNet",
                    $"Drawing evidence references unknown net '{netIdentity}'.", netIdentity));
            AddUnknownEndpointIssues(endpointIds, issues, source, destination);
            result.Add(new AutocadPowerFlowOrientationEvidence
            {
                NetIdentity = netIdentity,
                Orientation = document.Orientation.Value,
                SourceEndpointId = source,
                DestinationEndpointId = destination,
                Status = document.Status.Value,
                EvidenceSource = NormalizeOptional(document.EvidenceSource)
            });
        }
        return result;
    }

    private static IReadOnlyList<AutocadCrossPageContinuationEvidence> LoadCrossPage(
        IEnumerable<CrossPageDocument?> documents,
        IReadOnlySet<string> endpointIds,
        ICollection<AutocadReviewIssue> issues)
    {
        var result = new List<AutocadCrossPageContinuationEvidence>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpointPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.PairIdentity) ||
                string.IsNullOrWhiteSpace(document.SourceEndpointId) ||
                string.IsNullOrWhiteSpace(document.DestinationEndpointId) ||
                string.IsNullOrWhiteSpace(document.SourcePageId) ||
                string.IsNullOrWhiteSpace(document.DestinationPageId) || document.Status is null)
            {
                issues.Add(Error("EngineeringDrawingEvidenceSidecarInvalid",
                    "Each cross-page continuation requires pairIdentity, sourceEndpointId, destinationEndpointId, sourcePageId, destinationPageId, and status."));
                continue;
            }

            var pairIdentity = document.PairIdentity.Trim();
            var source = document.SourceEndpointId.Trim();
            var destination = document.DestinationEndpointId.Trim();
            var sourcePage = document.SourcePageId.Trim();
            var destinationPage = document.DestinationPageId.Trim();
            var endpointPair = string.Join('\u001f', source, destination);
            if (!identities.Add(pairIdentity) || !endpointPairs.Add(endpointPair))
            {
                issues.Add(Error("EngineeringDrawingEvidenceDuplicateCrossPageContinuation",
                    $"Drawing evidence has a duplicate cross-page continuation '{pairIdentity}'.", pairIdentity));
                continue;
            }
            AddUnknownEndpointIssues(endpointIds, issues, source, destination);
            result.Add(new AutocadCrossPageContinuationEvidence
            {
                PairIdentity = pairIdentity,
                SourceEndpointId = source,
                DestinationEndpointId = destination,
                SourcePageId = sourcePage,
                DestinationPageId = destinationPage,
                Status = document.Status.Value,
                EvidenceSource = NormalizeOptional(document.EvidenceSource)
            });
        }
        return result;
    }

    private static IReadOnlyList<AutocadCableInstanceOverride> LoadCableOverrides(
        IEnumerable<CableOverrideDocument?> documents,
        IReadOnlySet<string> cableIds,
        ICollection<AutocadReviewIssue> issues)
    {
        var result = new List<AutocadCableInstanceOverride>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.CableInstanceId) ||
                string.IsNullOrWhiteSpace(document.SpecificationOverride) && string.IsNullOrWhiteSpace(document.CatalogOverride))
            {
                issues.Add(Error("EngineeringDrawingEvidenceSidecarInvalid",
                    "Each cable instance override requires cableInstanceId and at least one of specificationOverride or catalogOverride."));
                continue;
            }

            var cableId = document.CableInstanceId.Trim();
            if (!identities.Add(cableId))
            {
                issues.Add(Error("EngineeringDrawingEvidenceDuplicateCableOverride",
                    $"Drawing evidence has more than one override for cable '{cableId}'.", cableId));
                continue;
            }
            if (!cableIds.Contains(cableId))
                issues.Add(Error("EngineeringDrawingEvidenceUnknownCable",
                    $"Drawing evidence references unknown cable '{cableId}'.", cableId));
            result.Add(new AutocadCableInstanceOverride
            {
                CableInstanceId = cableId,
                SpecificationOverride = NormalizeOptional(document.SpecificationOverride),
                CatalogOverride = NormalizeOptional(document.CatalogOverride)
            });
        }
        return result;
    }

    private static AutocadPageArchetypeHint? LoadPageHint(
        PageHintDocument? document,
        ICollection<AutocadReviewIssue> issues)
    {
        if (document is null) return null;
        if (document.Archetype is null || document.Status is null)
        {
            issues.Add(Error("EngineeringDrawingEvidenceSidecarInvalid",
                "pageArchetypeHint requires archetype and status."));
            return null;
        }
        return new AutocadPageArchetypeHint
        {
            Archetype = document.Archetype.Value,
            Status = document.Status.Value,
            EvidenceSource = NormalizeOptional(document.EvidenceSource)
        };
    }

    private static void AddUnknownEndpointIssues(
        IReadOnlySet<string> endpointIds,
        ICollection<AutocadReviewIssue> issues,
        params string[] candidates)
    {
        foreach (var endpointId in candidates.Where(endpointId => !endpointIds.Contains(endpointId)).Distinct(StringComparer.OrdinalIgnoreCase))
            issues.Add(Error("EngineeringDrawingEvidenceUnknownEndpoint",
                $"Drawing evidence references unknown endpoint '{endpointId}'.", endpointId));
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AutocadEngineeringDrawingEvidenceLoadResult Failure(
        string sidecarPath,
        string code,
        string message) => new(
            sidecarPath,
            new AutocadEngineeringDrawingEvidence(),
            [Error(code, message)]);

    private static AutocadReviewIssue Error(string code, string message, params string[] sourceIds) =>
        new("Error", code, message, sourceIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray());

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private sealed record SidecarDocument(
        string? SchemaVersion,
        string? ProjectId,
        List<ComponentRoleDocument?>? ComponentRoles,
        PageHintDocument? PageArchetypeHint,
        List<PowerFlowDocument?>? PowerFlowOrientations,
        List<CrossPageDocument?>? CrossPageContinuations,
        List<CableOverrideDocument?>? CableInstanceOverrides);

    private sealed record ComponentRoleDocument(
        string? ComponentInstanceId,
        ComponentDrawingRole? Role,
        DrawingEvidenceStatus? Status,
        string? EvidenceSource);

    private sealed record PageHintDocument(
        DrawingPageArchetype? Archetype,
        DrawingEvidenceStatus? Status,
        string? EvidenceSource);

    private sealed record PowerFlowDocument(
        string? NetIdentity,
        PowerFlowOrientation? Orientation,
        string? SourceEndpointId,
        string? DestinationEndpointId,
        DrawingEvidenceStatus? Status,
        string? EvidenceSource);

    private sealed record CrossPageDocument(
        string? PairIdentity,
        string? SourceEndpointId,
        string? DestinationEndpointId,
        string? SourcePageId,
        string? DestinationPageId,
        DrawingEvidenceStatus? Status,
        string? EvidenceSource);

    private sealed record CableOverrideDocument(
        string? CableInstanceId,
        string? SpecificationOverride,
        string? CatalogOverride);
}

public sealed record AutocadEngineeringDrawingEvidenceLoadResult(
    string SidecarPath,
    AutocadEngineeringDrawingEvidence Evidence,
    IReadOnlyList<AutocadReviewIssue> Issues)
{
    public bool Succeeded => Issues.All(issue => !string.Equals(issue.Severity, "Error", StringComparison.Ordinal));
}
