using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Persistence;

public static class ElectricalProjectMigrator
{
    public const string CurrentSchemaVersion = "0.5";

    public static ElectricalProject Migrate(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.Equals(project.SchemaVersion, CurrentSchemaVersion, StringComparison.OrdinalIgnoreCase))
        {
            ProjectLegacyAssemblyCompatibility(project);
            return project;
        }

        return project.SchemaVersion switch
        {
            "0.1" => UpgradeFrom04(UpgradeFrom03(UpgradeFrom02(UpgradeFrom01(project)))),
            "0.2" => UpgradeFrom04(UpgradeFrom03(UpgradeFrom02(project))),
            "0.3" => UpgradeFrom04(UpgradeFrom03(project)),
            "0.4" => UpgradeFrom04(project),
            _ => throw new NotSupportedException($"Electrical project schema '{project.SchemaVersion}' is not supported. Current schema is '{CurrentSchemaVersion}'.")
        };
    }

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
