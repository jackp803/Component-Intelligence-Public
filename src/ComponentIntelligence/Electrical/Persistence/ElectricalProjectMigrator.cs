using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Persistence;

public static class ElectricalProjectMigrator
{
    public const string CurrentSchemaVersion = "0.7";
    public const string CableWiringSchemaVersion = "0.8";
    public const string CableFunctionAssociationSchemaVersion = "0.9";
    public const string CadDisplayScaleSchemaVersion = "0.10";
    public const string ConnectorMatingSchemaVersion = "0.11";
    public const string PortFanoutSchemaVersion = "0.12";

    public static ElectricalProject Migrate(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.Equals(project.SchemaVersion, CurrentSchemaVersion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.SchemaVersion, CableWiringSchemaVersion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.SchemaVersion, CableFunctionAssociationSchemaVersion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.SchemaVersion, CadDisplayScaleSchemaVersion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.SchemaVersion, ConnectorMatingSchemaVersion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(project.SchemaVersion, PortFanoutSchemaVersion, StringComparison.OrdinalIgnoreCase))
        {
            ProjectLegacyAssemblyCompatibility(project);
            return ProtectCableWiring(project);
        }

        var migrated = project.SchemaVersion switch
        {
            "0.1" => UpgradeFrom04(UpgradeFrom03(UpgradeFrom02(UpgradeFrom01(project)))),
            "0.2" => UpgradeFrom04(UpgradeFrom03(UpgradeFrom02(project))),
            "0.3" => UpgradeFrom04(UpgradeFrom03(project)),
            "0.4" => UpgradeFrom04(project),
            "0.5" => Copy(project, CurrentSchemaVersion, project.DrawingPlan),
            "0.6" => Copy(project, CurrentSchemaVersion, project.DrawingPlan),
            _ => throw new NotSupportedException($"Electrical project schema '{project.SchemaVersion}' is not supported. Current schema is '{CurrentSchemaVersion}'.")
        };
        return ProtectCableWiring(migrated);
    }

    private static ElectricalProject ProtectCableWiring(ElectricalProject project) =>
        project.SchemaVersion == PortFanoutSchemaVersion ? project :
        project.Schematic?.Symbols.Any(s => s.EndpointDisplayPlacements.Count > 0) == true
            ? Copy(project, PortFanoutSchemaVersion, project.DrawingPlan) :
        project.SchemaVersion == ConnectorMatingSchemaVersion ? project :
        project.Connections.Any(c => c.MatingMap is not null) || Connectivity.ProjectPinFunctionService.Pins(project)
            .Any(p => p.InferredFunction is not null || p.FunctionManualOverride) ? Copy(project, ConnectorMatingSchemaVersion, project.DrawingPlan) :
        project.SchemaVersion == CadDisplayScaleSchemaVersion ? project :
        project.Schematic?.Symbols.Any(s => s.Geometry is { } g &&
            (s.Width != g.Width || s.Height != g.Height)) == true ? Copy(project, CadDisplayScaleSchemaVersion, project.DrawingPlan) :
        project.SchemaVersion != CableFunctionAssociationSchemaVersion && project.Cables.Any(c => c.ArchivedCable is { } cable &&
            (HasFunctions(cable.Manufacturing) || HasFunctions(cable.Template.Manufacturing)))
            ? Copy(project, CableFunctionAssociationSchemaVersion, project.DrawingPlan) :
        project.SchemaVersion != CableFunctionAssociationSchemaVersion && project.SchemaVersion != CableWiringSchemaVersion && project.Cables.Any(c => c.ArchivedCable is { } cable &&
            (cable.Manufacturing is { InventoryOnly: true } or { WiringDiagram: not null } ||
             cable.Template.Manufacturing is { InventoryOnly: true } or { WiringDiagram: not null }))
            ? Copy(project, CableWiringSchemaVersion, project.DrawingPlan) : project;

    private static bool HasFunctions(CableManufacturingDefinition? definition) => definition is not null &&
        (definition.FunctionLinks.Count > 0 || definition.FunctionOverrides.Count > 0);

    internal static ElectricalProject ForPortFanout(ElectricalProject project) =>
        Copy(Migrate(Editing.EngineeringReviewService.Clone(project)), PortFanoutSchemaVersion, project.DrawingPlan);

    private static ElectricalProject UpgradeFrom01(ElectricalProject source) => Copy(source, "0.2");
    private static ElectricalProject UpgradeFrom02(ElectricalProject source) => Copy(source, "0.3");

    private static ElectricalProject UpgradeFrom03(ElectricalProject source)
    {
        foreach (var assembly in source.CableAssemblies)
            assembly.CableConstructionType = assembly.IsCustom ? CableConstructionType.Custom : CableConstructionType.Unknown;
        var upgraded = Copy(source, "0.4"); ProjectLegacyAssemblyCompatibility(upgraded); return upgraded;
    }

    private static ElectricalProject UpgradeFrom04(ElectricalProject source)
    {
        // v0.5 adds presentation-only DrawingPlan project state. Older projects receive null;
        // migration must not infer page ownership, placement, routing, or representation from engineering/display data.
        var upgraded = Copy(source, CurrentSchemaVersion, drawingPlan: null); ProjectLegacyAssemblyCompatibility(upgraded); return upgraded;
    }

    private static ElectricalProject Copy(ElectricalProject source, string schemaVersion, ComponentIntelligence.Electrical.Drawing.DrawingPlanDocument? drawingPlan = null) => new()
    {
        SchemaVersion = schemaVersion,
        ProjectId = source.ProjectId,
        Name = source.Name,
        DrawingPlan = drawingPlan,
        Schematic = source.Schematic,
        BomItems = source.BomItems,
        Components = source.Components,
        Nets = source.Nets,
        Connections = source.Connections,
        Buses = source.Buses,
        Cables = source.Cables,
        CableAssemblies = source.CableAssemblies,
        TerminalBlocks = source.TerminalBlocks,
        LayoutContainers = source.LayoutContainers,
        DinRails = source.DinRails,
        CableDucts = source.CableDucts,
        CableRoutes = source.CableRoutes,
        EndpointReviews = source.EndpointReviews,
        TopologyPlacements = source.TopologyPlacements,
        TopologyRoutes = source.TopologyRoutes,
        TerminalStripSections = source.TerminalStripSections
    };

    private static void ProjectLegacyAssemblyCompatibility(ElectricalProject project)
    {
        foreach (var assembly in project.CableAssemblies)
            assembly.IsCustom = assembly.CableConstructionType == CableConstructionType.Custom;
    }
}
