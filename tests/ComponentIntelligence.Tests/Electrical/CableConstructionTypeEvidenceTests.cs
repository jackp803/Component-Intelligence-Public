using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Export;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableConstructionTypeEvidenceTests
{
    [Fact]
    public async Task OldProjectJsonWithoutConstructionType_LoadsAsUnknown()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var connectionFactory = new SqliteConnectionFactory();
            var repository = new ElectricalProjectRepository(connectionFactory, path);
            await repository.InitializeAsync();
            using (var connection = connectionFactory.Open(path))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO ElectricalProjects (ProjectId, SchemaVersion, Name, SnapshotJson, UpdatedUtc)
                    VALUES ($projectId, $schemaVersion, $name, $snapshotJson, $updatedUtc);
                    """;
                command.Parameters.AddWithValue("$projectId", "legacy-cable-project");
                command.Parameters.AddWithValue("$schemaVersion", "0.3");
                command.Parameters.AddWithValue("$name", "Legacy cable project");
                command.Parameters.AddWithValue("$snapshotJson", """
                    {"schemaVersion":"0.3","projectId":"legacy-cable-project","cables":[{"cableInstanceId":"cable-legacy","cableDefinitionId":"PURCHASED-LOOKING-PN-123"}]}
                    """);
                command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            var loaded = await repository.GetAsync("legacy-cable-project");

            Assert.NotNull(loaded);
            Assert.Equal(CableConstructionType.Unknown, Assert.Single(loaded!.Cables).CableConstructionType);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Theory]
    [InlineData(CableConstructionType.Unknown)]
    [InlineData(CableConstructionType.Purchased)]
    [InlineData(CableConstructionType.Custom)]
    public async Task ExplicitConstructionType_RoundTripsThroughExistingRepository(CableConstructionType constructionType)
    {
        var path = TemporaryDatabasePath();
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            var project = CableProject(constructionType);

            await repository.SaveAsync(project);
            var loaded = await repository.GetAsync(project.ProjectId);

            Assert.NotNull(loaded);
            Assert.Equal(constructionType, Assert.Single(loaded!.Cables).CableConstructionType);
            Assert.Equal("0.3", loaded.SchemaVersion);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Theory]
    [InlineData(CableConstructionType.Unknown)]
    [InlineData(CableConstructionType.Purchased)]
    [InlineData(CableConstructionType.Custom)]
    public void ExplicitConstructionType_IsExportedByStableCableInstanceId(CableConstructionType constructionType)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ci-cp2d-v2-{Guid.NewGuid():N}");
        try
        {
            var project = CableProject(constructionType);
            var result = new AutocadStagingGraphV2Exporter().PrepareAndWrite(
                project,
                Bindings(),
                DrawingEvidence(project),
                root);

            var graph = Assert.IsType<AutocadStagingGraphV2Contract>(result.Preparation.Graph);
            var cable = Assert.Single(graph.CableInstances);
            Assert.Equal("cable-1", cable.CableId);
            Assert.Equal(constructionType, cable.CableConstructionType);
            Assert.Equal("lrdu-staging-route.v2", graph.SchemaVersion);

            using var document = JsonDocument.Parse(File.ReadAllBytes(Assert.IsType<string>(result.GraphPath)));
            var exportedCable = Assert.Single(document.RootElement.GetProperty("cableInstances").EnumerateArray());
            Assert.Equal("cable-1", exportedCable.GetProperty("cableId").GetString());
            Assert.Equal(constructionType.ToString(), exportedCable.GetProperty("cableConstructionType").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AssemblyAndCatalogEvidence_DoNotInferConstructionType()
    {
        var project = CableProject(CableConstructionType.Unknown);
        project.CableAssemblies.Add(new CableAssembly
        {
            CableAssemblyId = "assembly-1",
            IsCustom = true,
            Members = { new CableAssemblyMember { CableInstanceId = "cable-1" } }
        });

        var graph = Assert.IsType<AutocadStagingGraphV2Contract>(
            new AutocadStagingGraphV2Builder().Prepare(project, Bindings(), DrawingEvidence(project)).Graph);

        var cable = Assert.Single(graph.CableInstances);
        Assert.Equal("PURCHASED-LOOKING-PN-123", cable.CableDefinitionId);
        Assert.Equal(CableConstructionType.Unknown, cable.CableConstructionType);
    }

    [Fact]
    public void ExistingV1StagingContract_DoesNotAcquireConstructionTypeField()
    {
        var project = CableProject(CableConstructionType.Custom);

        var graph = Assert.IsType<AutocadStagingGraphContract>(
            new AutocadStagingGraphBuilder().Prepare(project, Bindings(), DrawingEvidence(project)).Graph);

        Assert.DoesNotContain("cableConstructionType", JsonSerializer.Serialize(graph), StringComparison.Ordinal);
        Assert.Equal("lrdu-staging-route.v1", graph.SchemaVersion);
    }

    [Theory]
    [InlineData(CableConstructionType.Unknown)]
    [InlineData(CableConstructionType.Purchased)]
    [InlineData(CableConstructionType.Custom)]
    public void CableSegmentEditor_PersistsOnlyExplicitConstructionType(CableConstructionType constructionType)
    {
        var project = CableProject(CableConstructionType.Unknown);

        var cable = new TopologyConnectionEditor().AssignCableSegment(
            project,
            "connection-1",
            new CableSegmentOptions("CBL-01", "PURCHASED-LOOKING-PN-123", "Cable", constructionType));

        Assert.Equal(constructionType, cable.CableConstructionType);
        Assert.Equal(constructionType, Assert.Single(project.Cables).CableConstructionType);
    }

    private static ElectricalProject CableProject(CableConstructionType constructionType)
    {
        var project = new ElectricalProject { ProjectId = "cp2d-cable-project" };
        project.Components.Add(Component("left", "left:pin"));
        project.Components.Add(Component("right", "right:pin"));
        project.Nets.Add(new NetDefinition { NetId = "net-signal", Label = "SIGNAL", Layer = ElectricalLayer.Digital });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "connection-1",
            FromEndpointId = "left:pin",
            ToEndpointId = "right:pin",
            NetId = "net-signal",
            Kind = ConnectionKind.Cable,
            CableInstanceId = "cable-1",
            CableCoreId = "core-1"
        });
        project.Cables.Add(new CableInstance
        {
            CableInstanceId = "cable-1",
            CableDefinitionId = "PURCHASED-LOOKING-PN-123",
            CableConstructionType = constructionType,
            CoreAssignments =
            {
                new CoreAssignment
                {
                    CoreId = "core-1",
                    Status = "ASSIGNED",
                    FromEndpointId = "left:pin",
                    ToEndpointId = "right:pin"
                }
            }
        });
        return project;
    }

    private static ComponentInstance Component(string id, string pinId) => new()
    {
        ComponentInstanceId = id,
        ComponentDefinitionId = $"definition:{id}",
        TypeKey = "INLINE_CONNECTOR",
        ResponsibilityScope = ResponsibilityScope.InScope,
        Ports =
        {
            new ComponentPort
            {
                PortId = $"{id}:port",
                Name = "X1",
                Connector = new ConnectorDefinition
                {
                    ConnectorId = $"{id}:connector",
                    Family = "M12",
                    PinCount = 1
                },
                Pins =
                {
                    new ComponentPin
                    {
                        PinId = pinId,
                        PinNumber = "1",
                        Layer = ElectricalLayer.Digital,
                        Status = PinStatus.Normal
                    }
                }
            }
        }
    };

    private static IReadOnlyList<AutocadConnectionPointBinding> Bindings() =>
    [
        new AutocadConnectionPointBinding { EndpointId = "left:pin", SymbolKey = "SYM:LEFT", ConnectionPointId = "XTERM01" },
        new AutocadConnectionPointBinding { EndpointId = "right:pin", SymbolKey = "SYM:RIGHT", ConnectionPointId = "XTERM01" }
    ];

    private static AutocadEngineeringDrawingEvidence DrawingEvidence(ElectricalProject project) => new()
    {
        ComponentRoles = project.Components.Select(component => new AutocadComponentDrawingRoleEvidence
        {
            ComponentInstanceId = component.ComponentInstanceId,
            Role = ComponentDrawingRole.CableOrConnector,
            Status = DrawingEvidenceStatus.Confirmed,
            EvidenceSource = "test-engineer"
        }).ToArray()
    };

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"component-intelligence-cp2d-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }
}
