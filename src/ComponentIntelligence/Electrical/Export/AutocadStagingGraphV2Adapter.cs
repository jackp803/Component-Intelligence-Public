using System.Globalization;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Validation;

namespace ComponentIntelligence.Electrical.Export;

/// <summary>
/// Contract-only adapter from the existing evidence-preserving staging graph into the downstream
/// lrdu-staging-route.v2 boundary. It deliberately does not invent page allocation, Power DAG,
/// terminal drawing geometry, cable-detail capacity, Heavy Duty templates, or wire-layer policy.
/// Missing downstream-owned evidence remains explicit and blocking.
/// </summary>
public sealed class AutocadStagingGraphV2Builder
{
    public const string SchemaVersion = "lrdu-staging-route.v2";
    private readonly AutocadStagingGraphBuilder _sourceBuilder = new();

    public AutocadStagingGraphV2PreparationResult Prepare(
        ElectricalProject project,
        IEnumerable<AutocadConnectionPointBinding> auditedBindings,
        AutocadEngineeringDrawingEvidence? drawingEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(auditedBindings);

        drawingEvidence ??= new AutocadEngineeringDrawingEvidence();
        var source = _sourceBuilder.Prepare(project, auditedBindings, drawingEvidence);
        if (source.Graph is null)
            return new AutocadStagingGraphV2PreparationResult { Preflight = source.Preflight };

        return new AutocadStagingGraphV2PreparationResult
        {
            Preflight = source.Preflight,
            Graph = AutocadStagingGraphV2Contract.Create(source.Graph, drawingEvidence, project)
        };
    }
}

public sealed record AutocadStagingGraphV2PreparationResult
{
    public required AutocadExportPreflightReport Preflight { get; init; }
    public AutocadStagingGraphV2Contract? Graph { get; init; }
}

public sealed record AutocadStagingGraphV2Contract
{
    public const string SupportedSchemaVersion = AutocadStagingGraphV2Builder.SchemaVersion;

    [JsonPropertyName("schemaVersion")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("sourceGraphSchemaVersion")] public required string SourceGraphSchemaVersion { get; init; }
    [JsonPropertyName("projectId")] public required string ProjectId { get; init; }
    [JsonPropertyName("exportMode")] public string ExportMode { get; init; } = "ValidateOnly";
    [JsonPropertyName("pageArchetypeHint")] public AutocadStagingPageArchetypeHint PageArchetypeHint { get; init; } = new();
    [JsonPropertyName("routes")] public required IReadOnlyList<AutocadStagingRoute> Routes { get; init; }

    // These eight arrays are the pinned downstream v2 structural boundary. They are never omitted.
    [JsonPropertyName("pageIntents")] public required IReadOnlyList<AutocadStagingV2PageIntent> PageIntents { get; init; }
    [JsonPropertyName("powerFlowOrientation")] public required IReadOnlyList<AutocadStagingV2PowerFlowEvidence> PowerFlowOrientation { get; init; }
    [JsonPropertyName("powerEvidence")] public ElectricalPowerEvidenceV1Contract PowerEvidence { get; init; } = new();
    [JsonPropertyName("cableFamilies")] public required IReadOnlyList<AutocadStagingV2CableFamily> CableFamilies { get; init; }
    [JsonPropertyName("cableInstances")] public required IReadOnlyList<AutocadStagingV2CableInstance> CableInstances { get; init; }
    [JsonPropertyName("terminalContinuities")] public required IReadOnlyList<AutocadStagingV2TerminalContinuity> TerminalContinuities { get; init; }
    [JsonPropertyName("crossPageContinuations")] public required IReadOnlyList<AutocadStagingV2CrossPageContinuation> CrossPageContinuations { get; init; }
    [JsonPropertyName("deviceRoles")] public required IReadOnlyList<AutocadStagingV2DeviceRole> DeviceRoles { get; init; }
    [JsonPropertyName("heavyDutyConnectors")] public required IReadOnlyList<AutocadStagingV2HeavyDutyConnector> HeavyDutyConnectors { get; init; }

    [JsonPropertyName("wireLayerPolicy")] public AutocadStagingV2WireLayerPolicy WireLayerPolicy { get; init; } = new();
    [JsonPropertyName("sourceEvidence")] public required AutocadStagingV2SourceEvidence SourceEvidence { get; init; }
    [JsonPropertyName("interventions")] public required IReadOnlyList<AutocadStagingIntervention> Interventions { get; init; }
    [JsonPropertyName("writerInterface")] public AutocadStagingWriterInterface WriterInterface { get; init; } = new();

    public static void EnsureSupportedSchema(string? schemaVersion)
    {
        if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"Engineering Graph schema '{schemaVersion ?? "<missing>"}' is unsupported; expected '{SupportedSchemaVersion}'.");
    }

    internal static AutocadStagingGraphV2Contract Create(
        AutocadStagingGraphContract source,
        AutocadEngineeringDrawingEvidence drawingEvidence,
        ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(drawingEvidence);
        ArgumentNullException.ThrowIfNull(project);

        var routes = source.Routes
            .Select(route => route with
            {
                Nodes = route.Nodes.OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray(),
                Segments = route.Segments.OrderBy(segment => segment.SegmentId, StringComparer.Ordinal).ToArray()
            })
            .OrderBy(route => route.RouteId, StringComparer.Ordinal)
            .ToArray();
        var pageIntents = BuildPageIntents(drawingEvidence);
        var powerFlow = BuildPowerFlowEvidence(drawingEvidence);
        var powerEvidence = ElectricalPowerEvidenceV1Builder.Build(project);
        var cableFamilies = BuildCableFamilies(source.CableFamilies);
        var cableInstances = BuildCableInstances(source.CableInstances, routes, project.Cables);
        var terminalContinuities = BuildTerminalContinuities(source.TerminalContinuities);
        var crossPage = BuildCrossPageContinuations(source.CrossPageContinuations, routes);
        var deviceRoles = BuildDeviceRoles(drawingEvidence);

        return new AutocadStagingGraphV2Contract
        {
            SchemaVersion = SupportedSchemaVersion,
            SourceGraphSchemaVersion = source.SchemaVersion,
            ProjectId = source.ProjectId,
            ExportMode = source.ExportMode,
            PageArchetypeHint = source.PageArchetypeHint,
            Routes = routes,
            PageIntents = pageIntents,
            PowerFlowOrientation = powerFlow,
            PowerEvidence = powerEvidence,
            CableFamilies = cableFamilies,
            CableInstances = cableInstances,
            TerminalContinuities = terminalContinuities,
            CrossPageContinuations = crossPage,
            DeviceRoles = deviceRoles,
            HeavyDutyConnectors = [],
            WireLayerPolicy = new AutocadStagingV2WireLayerPolicy(),
            SourceEvidence = new AutocadStagingV2SourceEvidence
            {
                CableFamilies = source.CableFamilies.OrderBy(item => item.CableFamilyId, StringComparer.Ordinal).ToArray(),
                CableInstances = source.CableInstances.OrderBy(item => item.CableInstanceId, StringComparer.Ordinal).ToArray(),
                TerminalContinuities = source.TerminalContinuities.OrderBy(item => item.ContinuityId, StringComparer.Ordinal).ToArray(),
                CrossPageContinuations = source.CrossPageContinuations
                    .OrderBy(item => item.PairIdentity, StringComparer.Ordinal)
                    .ThenBy(item => item.SourceEndpointId, StringComparer.Ordinal)
                    .ThenBy(item => item.DestinationEndpointId, StringComparer.Ordinal)
                    .ThenBy(item => item.SourcePageId, StringComparer.Ordinal)
                    .ThenBy(item => item.DestinationPageId, StringComparer.Ordinal)
                    .ThenBy(item => item.EvidenceStatus)
                    .ThenBy(item => item.EvidenceSource, StringComparer.Ordinal)
                    .ToArray()
            },
            Interventions = source.Interventions.OrderBy(item => item.InterventionId, StringComparer.Ordinal).ToArray(),
            WriterInterface = source.WriterInterface
        };
    }

    private static IReadOnlyList<AutocadStagingV2PageIntent> BuildPageIntents(
        AutocadEngineeringDrawingEvidence drawingEvidence)
    {
        var hint = drawingEvidence.PageArchetypeHint;
        if (hint is null) return [];

        // A page archetype hint is not page identity or member assignment evidence. Preserve it as a
        // blocking record rather than inventing a pageId, drawingRole, or node membership.
        return
        [
            new AutocadStagingV2PageIntent
            {
                PageId = string.Empty,
                DrawingRole = string.Empty,
                PageArchetypeHint = PlannerArchetype(hint.Archetype),
                MemberNodeIds = [],
                EvidenceStatus = hint.Status,
                EvidenceSource = hint.EvidenceSource,
                BlockingReason = "PAGE_ID_AND_MEMBER_ASSIGNMENT_EVIDENCE_REQUIRED"
            }
        ];
    }

    private static IReadOnlyList<AutocadStagingV2PowerFlowEvidence> BuildPowerFlowEvidence(
        AutocadEngineeringDrawingEvidence drawingEvidence) => drawingEvidence.PowerFlowOrientations
        .OrderBy(item => item.NetIdentity, StringComparer.Ordinal)
        .ThenBy(item => item.SourceEndpointId, StringComparer.Ordinal)
        .ThenBy(item => item.DestinationEndpointId, StringComparer.Ordinal)
        .Select(item => new AutocadStagingV2PowerFlowEvidence
        {
            // The pinned planner requires page-level source-trunk/conversion evidence. Existing CI
            // evidence is net-level orientation only, so page semantics remain explicitly unknown.
            PageId = string.Empty,
            NetIdentity = item.NetIdentity,
            Orientation = "Unknown",
            SourceDirectionStatus = "Unknown",
            ConfirmedSourceTrunks = [],
            VerticalDrops = [],
            Conversions = [],
            SourceEndpointId = item.SourceEndpointId,
            DestinationEndpointId = item.DestinationEndpointId,
            SourceOrientation = item.Orientation,
            EvidenceStatus = item.Status,
            EvidenceSource = item.EvidenceSource,
            BlockingReason = "PAGE_LEVEL_POWER_SOURCE_TRUNK_EVIDENCE_REQUIRED"
        }).ToArray();

    private static IReadOnlyList<AutocadStagingV2CableFamily> BuildCableFamilies(
        IReadOnlyList<CableFamilySignature> sourceFamilies) => sourceFamilies
        .OrderBy(item => item.CableFamilyId, StringComparer.Ordinal)
        .Select(item => new AutocadStagingV2CableFamily
        {
            CableFamilyId = item.CableFamilyId,
            EndAInterface = InterfaceText(item.EndA),
            EndBInterface = InterfaceText(item.EndB),
            MaxInstancesPerPage = null,
            Pins = [],
            EvidenceStatus = "BlockingUnknown",
            BlockingReason = "CABLE_PAGE_CAPACITY_AND_PLANNER_PIN_DISPOSITION_EVIDENCE_REQUIRED"
        }).ToArray();

    private static IReadOnlyList<AutocadStagingV2CableInstance> BuildCableInstances(
        IReadOnlyList<AutocadStagingCableInstance> sourceInstances,
        IReadOnlyList<AutocadStagingRoute> routes,
        IReadOnlyList<CableInstance> projectInstances)
    {
        var segments = routes.SelectMany(route => route.Segments)
            .Where(segment => string.Equals(segment.TopologyStatus, "Confirmed", StringComparison.Ordinal))
            .ToArray();
        var constructionTypeByCableId = projectInstances
            .GroupBy(item => item.CableInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().CableConstructionType,
                StringComparer.OrdinalIgnoreCase);

        return sourceInstances.OrderBy(item => item.CableInstanceId, StringComparer.Ordinal)
            .Select(item =>
            {
                var matchingSegments = segments
                    .Where(segment => string.Equals(segment.CableInstanceId, item.CableInstanceId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(segment => segment.SegmentId, StringComparer.Ordinal)
                    .ToArray();
                var exactSegment = matchingSegments.Length == 1 ? matchingSegments[0] : null;
                var specCatalog = FirstNonBlank(item.SpecificationOverride, item.CatalogOverride) ?? "TBD";
                return new AutocadStagingV2CableInstance
                {
                    CableId = item.CableInstanceId,
                    CableFamilyId = item.CableFamilyId,
                    Source = exactSegment?.FromNodeId ?? string.Empty,
                    Destination = exactSegment?.ToNodeId ?? string.Empty,
                    Quantity = 1,
                    Length = item.ProvidedLengthMm is null
                        ? "TBD"
                        : item.ProvidedLengthMm.Value.ToString("0.###", CultureInfo.InvariantCulture) + " mm",
                    SpecCatalog = specCatalog,
                    Note = string.Empty,
                    PinCoreMap = [],
                    CableDefinitionId = item.CableDefinitionId,
                    LengthSource = item.LengthSource,
                    CableConstructionType = constructionTypeByCableId.GetValueOrDefault(
                        item.CableInstanceId,
                        CableConstructionType.Unknown),
                    EvidenceStatus = exactSegment is null ? "BlockingUnknown" : "Partial",
                    BlockingReason = "PLANNER_PIN_CORE_FUNCTION_MAP_REQUIRED"
                };
            }).ToArray();
    }

    private static IReadOnlyList<AutocadStagingV2TerminalContinuity> BuildTerminalContinuities(
        IReadOnlyList<AutocadStagingTerminalContinuity> sourceContinuities) => sourceContinuities
        .OrderBy(item => item.ContinuityId, StringComparer.Ordinal)
        .Select(item => new AutocadStagingV2TerminalContinuity
        {
            // CI proves conductive continuity, but the pinned planner additionally requires a
            // classified terminal node, directed input/output segments, and schematic point.
            TerminalNodeId = string.Empty,
            Classification = "Unknown",
            InputSegmentId = string.Empty,
            OutputSegmentIds = [],
            CrossoverSegmentIds = [],
            PhysicalProcurementStatus = "TBD",
            SchematicPoint = null,
            SourceContinuityId = item.ContinuityId,
            TerminalBlockId = item.TerminalBlockId,
            TerminalPositionId = item.TerminalPositionId,
            LevelId = item.LevelId,
            FromConnectionPointId = item.FromConnectionPointId,
            ToConnectionPointId = item.ToConnectionPointId,
            EvidenceStatus = item.EvidenceStatus,
            BlockingReason = "PLANNER_TERMINAL_CLASSIFICATION_AND_SCHEMATIC_POINT_REQUIRED"
        }).ToArray();

    private sealed record CrossPageCandidate(string RouteId, string NetIdentity, string SegmentId);

    private static IReadOnlyList<AutocadStagingV2CrossPageContinuation> BuildCrossPageContinuations(
        IReadOnlyList<AutocadStagingCrossPageContinuation> sourceContinuations,
        IReadOnlyList<AutocadStagingRoute> routes)
    {
        return sourceContinuations
            .GroupBy(item => item.PairIdentity, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var items = group
                    .OrderBy(item => CrossPageSourceCanonicalKey(item), StringComparer.Ordinal)
                    .ToArray();
                if (items.Length != 1)
                    return DuplicateCrossPageContinuation(group.Key, items);

                var item = items[0];
                var sourceNodeId = $"node:{item.SourceEndpointId}";
                var destinationNodeId = $"node:{item.DestinationEndpointId}";
                var candidates = routes
                    .SelectMany(route => route.Segments
                        .Where(segment =>
                            string.Equals(segment.TopologyStatus, "Confirmed", StringComparison.Ordinal) &&
                            UnorderedNodePairMatches(segment.FromNodeId, segment.ToNodeId, sourceNodeId, destinationNodeId) &&
                            !string.IsNullOrWhiteSpace(route.RouteId) &&
                            !string.IsNullOrWhiteSpace(route.NetIdentity) &&
                            !string.IsNullOrWhiteSpace(segment.SegmentId))
                        .Select(segment => new CrossPageCandidate(route.RouteId, route.NetIdentity, segment.SegmentId)))
                    .OrderBy(candidate => candidate.RouteId, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.NetIdentity, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.SegmentId, StringComparer.Ordinal)
                    .ToArray();

                var exact = candidates.Length == 1 ? candidates[0] : null;
                var blockingReason = candidates.Length switch
                {
                    0 => "EXACT_CONFIRMED_ROUTE_NET_SEGMENT_REQUIRED",
                    > 1 => "EXACT_CONFIRMED_ROUTE_NET_SEGMENT_AMBIGUOUS",
                    _ when item.EvidenceStatus != DrawingEvidenceStatus.Confirmed =>
                        "CONFIRMED_CROSS_PAGE_CONTINUATION_EVIDENCE_REQUIRED",
                    _ => null
                };

                return new AutocadStagingV2CrossPageContinuation
                {
                    PairIdentity = item.PairIdentity,
                    RouteId = exact?.RouteId ?? string.Empty,
                    NetIdentity = exact?.NetIdentity ?? string.Empty,
                    SegmentId = exact?.SegmentId ?? string.Empty,
                    SourceEndpointId = item.SourceEndpointId,
                    DestinationEndpointId = item.DestinationEndpointId,
                    SourcePageId = item.SourcePageId,
                    DestinationPageId = item.DestinationPageId,
                    SourceNodeId = sourceNodeId,
                    DestinationNodeId = destinationNodeId,
                    EvidenceStatus = item.EvidenceStatus,
                    EvidenceSource = item.EvidenceSource,
                    BlockingReason = blockingReason
                };
            }).ToArray();
    }

    private static AutocadStagingV2CrossPageContinuation DuplicateCrossPageContinuation(
        string pairIdentity,
        IReadOnlyList<AutocadStagingCrossPageContinuation> items)
    {
        var sourceEndpointId = UnanimousRequired(items.Select(item => item.SourceEndpointId));
        var destinationEndpointId = UnanimousRequired(items.Select(item => item.DestinationEndpointId));
        var sourcePageId = UnanimousRequired(items.Select(item => item.SourcePageId));
        var destinationPageId = UnanimousRequired(items.Select(item => item.DestinationPageId));
        var evidenceStatus = items.Select(item => item.EvidenceStatus).Distinct().Count() == 1
            ? items[0].EvidenceStatus
            : DrawingEvidenceStatus.Unknown;
        var evidenceSource = UnanimousOptional(items.Select(item => item.EvidenceSource));
        return new AutocadStagingV2CrossPageContinuation
        {
            PairIdentity = pairIdentity,
            RouteId = string.Empty,
            NetIdentity = string.Empty,
            SegmentId = string.Empty,
            SourceEndpointId = sourceEndpointId,
            DestinationEndpointId = destinationEndpointId,
            SourcePageId = sourcePageId,
            DestinationPageId = destinationPageId,
            SourceNodeId = sourceEndpointId.Length == 0 ? string.Empty : $"node:{sourceEndpointId}",
            DestinationNodeId = destinationEndpointId.Length == 0 ? string.Empty : $"node:{destinationEndpointId}",
            EvidenceStatus = evidenceStatus,
            EvidenceSource = evidenceSource,
            BlockingReason = "DUPLICATE_CROSS_PAGE_PAIR_IDENTITY"
        };
    }

    private static bool UnorderedNodePairMatches(
        string first,
        string second,
        string sourceNodeId,
        string destinationNodeId) =>
        string.Equals(first, sourceNodeId, StringComparison.Ordinal) &&
        string.Equals(second, destinationNodeId, StringComparison.Ordinal) ||
        string.Equals(first, destinationNodeId, StringComparison.Ordinal) &&
        string.Equals(second, sourceNodeId, StringComparison.Ordinal);

    private static string CrossPageSourceCanonicalKey(AutocadStagingCrossPageContinuation item) => string.Join("\u001f",
        item.PairIdentity,
        item.SourceEndpointId,
        item.DestinationEndpointId,
        item.SourcePageId,
        item.DestinationPageId,
        item.EvidenceStatus.ToString(),
        item.EvidenceSource ?? string.Empty);

    private static string UnanimousRequired(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 1 ? distinct[0] : string.Empty;
    }

    private static string? UnanimousOptional(IEnumerable<string?> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static IReadOnlyList<AutocadStagingV2DeviceRole> BuildDeviceRoles(
        AutocadEngineeringDrawingEvidence drawingEvidence) => drawingEvidence.ComponentRoles
        .Where(item => item.Role is not ComponentDrawingRole.TransparentTerminal and not ComponentDrawingRole.Unknown)
        .OrderBy(item => item.ComponentInstanceId, StringComparer.Ordinal)
        .Select(item => new AutocadStagingV2DeviceRole
        {
            ComponentInstanceId = item.ComponentInstanceId,
            DeviceRole = PlannerRole(item.Role),
            SourceDrawingRole = item.Role,
            EvidenceStatus = item.Status,
            EvidenceSource = item.EvidenceSource,
            RepresentationEvidence = item.Role == ComponentDrawingRole.CableOrConnector ? "Unknown" : null
        }).ToArray();

    private static string PlannerRole(ComponentDrawingRole role) => role switch
    {
        ComponentDrawingRole.PowerSource or ComponentDrawingRole.ConsumerOrConverter => "FunctionalPowerDevice",
        ComponentDrawingRole.SensorOrControlDevice => "SensorOrControlDevice",
        ComponentDrawingRole.ValveOrPump => "ValveOrPump",
        ComponentDrawingRole.InterfaceModule => "InterfaceModule",
        ComponentDrawingRole.CableOrConnector => "CableOrConnector",
        _ => "Unknown"
    };

    private static string PlannerArchetype(DrawingPageArchetype archetype) => archetype switch
    {
        DrawingPageArchetype.TerminalContinuation => "TerminalDistribution",
        DrawingPageArchetype.Unknown => "Unknown",
        _ => archetype.ToString()
    };

    private static string InterfaceText(CableInterfaceSignature value)
    {
        var pieces = new[]
        {
            value.Family,
            value.Series,
            value.PinCount is null ? null : $"{value.PinCount.Value}-position",
            value.Coding,
            value.Gender.ToString()
        };
        return string.Join(" ", pieces.Where(piece => !string.IsNullOrWhiteSpace(piece)));
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record AutocadStagingV2PageIntent
{
    [JsonPropertyName("pageId")] public required string PageId { get; init; }
    [JsonPropertyName("drawingRole")] public required string DrawingRole { get; init; }
    [JsonPropertyName("pageArchetypeHint")] public required string PageArchetypeHint { get; init; }
    [JsonPropertyName("memberNodeIds")] public required IReadOnlyList<string> MemberNodeIds { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; }
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
    [JsonPropertyName("blockingReason")] public required string BlockingReason { get; init; }
}

public sealed record AutocadStagingV2PowerFlowEvidence
{
    [JsonPropertyName("pageId")] public required string PageId { get; init; }
    [JsonPropertyName("netIdentity")] public required string NetIdentity { get; init; }
    [JsonPropertyName("orientation")] public required string Orientation { get; init; }
    [JsonPropertyName("sourceDirectionStatus")] public required string SourceDirectionStatus { get; init; }
    [JsonPropertyName("confirmedSourceTrunks")] public required IReadOnlyList<object> ConfirmedSourceTrunks { get; init; }
    [JsonPropertyName("verticalDrops")] public required IReadOnlyList<object> VerticalDrops { get; init; }
    [JsonPropertyName("conversions")] public required IReadOnlyList<object> Conversions { get; init; }
    [JsonPropertyName("sourceEndpointId")] public required string SourceEndpointId { get; init; }
    [JsonPropertyName("destinationEndpointId")] public required string DestinationEndpointId { get; init; }
    [JsonPropertyName("sourceOrientation")] public PowerFlowOrientation SourceOrientation { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; }
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
    [JsonPropertyName("blockingReason")] public required string BlockingReason { get; init; }
}

public sealed record AutocadStagingV2CableFamily
{
    [JsonPropertyName("cableFamilyId")] public required string CableFamilyId { get; init; }
    [JsonPropertyName("maxInstancesPerPage")] public int? MaxInstancesPerPage { get; init; }
    [JsonPropertyName("endAInterface")] public required string EndAInterface { get; init; }
    [JsonPropertyName("endBInterface")] public required string EndBInterface { get; init; }
    [JsonPropertyName("pins")] public required IReadOnlyList<object> Pins { get; init; }
    [JsonPropertyName("evidenceStatus")] public required string EvidenceStatus { get; init; }
    [JsonPropertyName("blockingReason")] public required string BlockingReason { get; init; }
}

public sealed record AutocadStagingV2CableInstance
{
    [JsonPropertyName("cableId")] public required string CableId { get; init; }
    [JsonPropertyName("cableFamilyId")] public required string CableFamilyId { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("destination")] public required string Destination { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
    [JsonPropertyName("length")] public required string Length { get; init; }
    [JsonPropertyName("specCatalog")] public required string SpecCatalog { get; init; }
    [JsonPropertyName("note")] public required string Note { get; init; }
    [JsonPropertyName("pinCoreMap")] public required IReadOnlyList<object> PinCoreMap { get; init; }
    [JsonPropertyName("cableDefinitionId")] public required string CableDefinitionId { get; init; }
    [JsonPropertyName("lengthSource")] public CableLengthSource LengthSource { get; init; }
    [JsonPropertyName("cableConstructionType"), JsonConverter(typeof(JsonStringEnumConverter))]
    public CableConstructionType CableConstructionType { get; init; } = CableConstructionType.Unknown;
    [JsonPropertyName("evidenceStatus")] public required string EvidenceStatus { get; init; }
    [JsonPropertyName("blockingReason")] public required string BlockingReason { get; init; }
}

public sealed record AutocadStagingV2TerminalContinuity
{
    [JsonPropertyName("terminalNodeId")] public required string TerminalNodeId { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("inputSegmentId")] public required string InputSegmentId { get; init; }
    [JsonPropertyName("outputSegmentIds")] public required IReadOnlyList<string> OutputSegmentIds { get; init; }
    [JsonPropertyName("crossoverSegmentIds")] public required IReadOnlyList<string> CrossoverSegmentIds { get; init; }
    [JsonPropertyName("physicalProcurementStatus")] public required string PhysicalProcurementStatus { get; init; }
    [JsonPropertyName("schematicPoint")] public IReadOnlyList<double>? SchematicPoint { get; init; }
    [JsonPropertyName("sourceContinuityId")] public required string SourceContinuityId { get; init; }
    [JsonPropertyName("terminalBlockId")] public required string TerminalBlockId { get; init; }
    [JsonPropertyName("terminalPositionId")] public required string TerminalPositionId { get; init; }
    [JsonPropertyName("levelId")] public required string LevelId { get; init; }
    [JsonPropertyName("fromConnectionPointId")] public required string FromConnectionPointId { get; init; }
    [JsonPropertyName("toConnectionPointId")] public required string ToConnectionPointId { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; }
    [JsonPropertyName("blockingReason")] public required string BlockingReason { get; init; }
}

public sealed record AutocadStagingV2CrossPageContinuation
{
    [JsonPropertyName("pairIdentity")] public required string PairIdentity { get; init; }
    [JsonPropertyName("routeId")] public string RouteId { get; init; } = string.Empty;
    [JsonPropertyName("netIdentity")] public string NetIdentity { get; init; } = string.Empty;
    [JsonPropertyName("segmentId")] public required string SegmentId { get; init; }
    [JsonPropertyName("sourceEndpointId")] public string SourceEndpointId { get; init; } = string.Empty;
    [JsonPropertyName("destinationEndpointId")] public string DestinationEndpointId { get; init; } = string.Empty;
    [JsonPropertyName("sourcePageId")] public required string SourcePageId { get; init; }
    [JsonPropertyName("destinationPageId")] public required string DestinationPageId { get; init; }
    [JsonPropertyName("sourceNodeId")] public required string SourceNodeId { get; init; }
    [JsonPropertyName("destinationNodeId")] public required string DestinationNodeId { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; }
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
    [JsonPropertyName("blockingReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BlockingReason { get; init; }
}

public sealed record AutocadStagingV2DeviceRole
{
    [JsonPropertyName("componentInstanceId")] public required string ComponentInstanceId { get; init; }
    [JsonPropertyName("deviceRole")] public required string DeviceRole { get; init; }
    [JsonPropertyName("sourceDrawingRole")] public ComponentDrawingRole SourceDrawingRole { get; init; }
    [JsonPropertyName("evidenceStatus")] public DrawingEvidenceStatus EvidenceStatus { get; init; }
    [JsonPropertyName("evidenceSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceSource { get; init; }
    [JsonPropertyName("representationEvidence"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RepresentationEvidence { get; init; }
}

public sealed record AutocadStagingV2HeavyDutyConnector
{
    [JsonPropertyName("physicalConnectorId")] public required string PhysicalConnectorId { get; init; }
    [JsonPropertyName("componentInstanceId")] public required string ComponentInstanceId { get; init; }
    [JsonPropertyName("pageIdPrefix")] public required string PageIdPrefix { get; init; }
    [JsonPropertyName("drawingFilePrefix")] public required string DrawingFilePrefix { get; init; }
    [JsonPropertyName("rowsPerPage")] public required int RowsPerPage { get; init; }
    [JsonPropertyName("contactNodeIds")] public required IReadOnlyList<string> ContactNodeIds { get; init; }
}

public sealed record AutocadStagingV2WireLayerPolicy
{
    [JsonPropertyName("approvalStatus")] public string ApprovalStatus { get; init; } = "Missing";
    [JsonPropertyName("policyId")] public string PolicyId { get; init; } = string.Empty;
    [JsonPropertyName("segmentLayers")] public IReadOnlyList<object> SegmentLayers { get; init; } = [];
}

public sealed record AutocadStagingV2SourceEvidence
{
    [JsonPropertyName("cableFamilies")] public required IReadOnlyList<CableFamilySignature> CableFamilies { get; init; }
    [JsonPropertyName("cableInstances")] public required IReadOnlyList<AutocadStagingCableInstance> CableInstances { get; init; }
    [JsonPropertyName("terminalContinuities")] public required IReadOnlyList<AutocadStagingTerminalContinuity> TerminalContinuities { get; init; }
    [JsonPropertyName("crossPageContinuations")] public required IReadOnlyList<AutocadStagingCrossPageContinuation> CrossPageContinuations { get; init; }
}
