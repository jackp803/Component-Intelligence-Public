using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Persistence;

public static class ElectricalProjectMigrator
{
    public const string CurrentSchemaVersion = "0.3";

    public static ElectricalProject Migrate(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.Equals(project.SchemaVersion, CurrentSchemaVersion, StringComparison.OrdinalIgnoreCase)) return project;

        return project.SchemaVersion switch
        {
            "0.1" => UpgradeFrom02(UpgradeFrom01(project)),
            "0.2" => UpgradeFrom02(project),
            _ => throw new NotSupportedException(
                $"Electrical project schema '{project.SchemaVersion}' is not supported. Current schema is '{CurrentSchemaVersion}'.")
        };
    }

    private static ElectricalProject UpgradeFrom01(ElectricalProject source)
    {
        // v0.2 introduced TopologyPlacements. Existing engineering objects are retained by identity;
        // an empty placement list simply means the UI can create deterministic visual defaults later.
        return new ElectricalProject
        {
            SchemaVersion = "0.2",
            ProjectId = source.ProjectId,
            Name = source.Name,
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
    }

    private static ElectricalProject UpgradeFrom02(ElectricalProject source)
    {
        // v0.3 adds explicit 2.5D mounting-surface/depth facts and authoritative external cable-length
        // fields. New fields intentionally remain Unknown/null when loading older projects; migration
        // must not infer mounting faces, component depth or cable length from old 2D geometry/routes.
        return new ElectricalProject
        {
            SchemaVersion = CurrentSchemaVersion,
            ProjectId = source.ProjectId,
            Name = source.Name,
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
    }
}
