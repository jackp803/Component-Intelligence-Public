using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ElectricalMigrationV03Tests
{
    [Fact]
    public void Migrator_UpgradesV02ToV03WithoutInventingLengthOrMountingSurface()
    {
        var oldProject = new ElectricalProject
        {
            SchemaVersion = "0.2",
            ProjectId = "legacy-v02",
            Name = "Legacy electrical project",
            LayoutContainers =
            {
                new LayoutContainer { ContainerId = "cab", Name = "CAB", WidthMm = 600, HeightMm = 800 }
            },
            Components =
            {
                new ComponentInstance
                {
                    ComponentInstanceId = "c1",
                    ComponentDefinitionId = "def1",
                    TypeKey = "PLC",
                    Footprint = new PhysicalFootprint { WidthMm = 100, HeightMm = 120 },
                    Placement = new PhysicalPlacement { ParentContainerId = "cab", XMm = 10, YMm = 20 }
                }
            },
            Connections =
            {
                new ElectricalConnection { ConnectionId = "w1", FromEndpointId = "a", ToEndpointId = "b" }
            },
            Cables =
            {
                new CableInstance { CableInstanceId = "cab1", CableDefinitionId = "def-cable" }
            },
            CableRoutes =
            {
                new CableRoute
                {
                    CableRouteId = "route1",
                    ConnectionOrCableId = "cab1",
                    Segments = { new RouteSegment { SegmentId = "s1", StartXMm = 0, StartYMm = 0, EndXMm = 5000, EndYMm = 0 } }
                }
            }
        };

        var migrated = ElectricalProjectMigrator.Migrate(oldProject);

        Assert.Equal(ElectricalProjectMigrator.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Same(oldProject.Components, migrated.Components);
        Assert.Same(oldProject.Connections, migrated.Connections);
        Assert.Same(oldProject.Cables, migrated.Cables);
        Assert.Equal(MountingSurface.Unknown, migrated.Components.Single().Placement!.Surface);
        Assert.Null(migrated.Connections.Single().ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Unknown, migrated.Connections.Single().LengthSource);
        Assert.Null(migrated.Cables.Single().ProvidedLengthMm);
        Assert.Equal(CableLengthSource.Unknown, migrated.Cables.Single().LengthSource);
        Assert.Single(migrated.CableRoutes);
    }

    [Fact]
    public void Migrator_UpgradesV01ThroughV02ToV03()
    {
        var oldProject = new ElectricalProject
        {
            SchemaVersion = "0.1",
            ProjectId = "legacy-v01",
            Nets = { new NetDefinition { NetId = "n1", Label = "24V", Layer = ElectricalLayer.Power } }
        };

        var migrated = ElectricalProjectMigrator.Migrate(oldProject);

        Assert.Equal(ElectricalProjectMigrator.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Single(migrated.Nets);
    }
}
