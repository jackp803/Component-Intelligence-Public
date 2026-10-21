using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ProjectPersistenceCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ci-save-capture-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory = new();
    private string Database => Path.Combine(_root, "synthetic.db");

    [Fact]
    public async Task Save_CapturesNameAndNestedContentBeforeDatabaseInitializationWaits()
    {
        var repository = new ElectricalProjectRepository(_factory, Database);
        var project = Project();
        await WhileInitializationIsBlocked(
            () => repository.SaveAsync(project),
            () => Mutate(project));

        var saved = Assert.IsType<ElectricalProject>(await repository.GetAsync("P1"));
        AssertCapturedContent(saved);
    }

    [Fact]
    public async Task Checkpoint_CapturesSnapshotAndSummaryBeforeRevisionLookupWaits()
    {
        var service = new ProjectRevisionService(new ProjectRevisionRepository(_factory, Database));
        var project = Project();
        ProjectRevisionRow? checkpoint = null;
        await WhileInitializationIsBlocked(
            async () => checkpoint = await service.CreateCheckpointAsync(project, ProjectRevisionTrigger.Save),
            () => Mutate(project));

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var saved = JsonSerializer.Deserialize<ElectricalProject>(Assert.IsType<ProjectRevisionRow>(checkpoint).SnapshotJson, options);
        AssertCapturedContent(Assert.IsType<ElectricalProject>(saved));
    }

    [Fact]
    public async Task Save_DoesNotMigrateCallerOwnedMutableObjects()
    {
        var project = new ElectricalProject
        {
            ProjectId = "P1",
            CableAssemblies = [new CableAssembly
            {
                CableAssemblyId = "A1", IsCustom = false, CableConstructionType = CableConstructionType.Custom
            }]
        };

        var persisted = await new ElectricalProjectRepository(_factory, Database).SaveSnapshotAsync(project);

        Assert.False(project.CableAssemblies[0].IsCustom);
        Assert.True((await new ElectricalProjectRepository(_factory, Database).GetAsync("P1"))!.CableAssemblies[0].IsCustom);
        Assert.True(persisted.CableAssemblies[0].IsCustom);
        Assert.Equal(JsonSerializer.Serialize(persisted), JsonSerializer.Serialize(await new ElectricalProjectRepository(_factory, Database).GetAsync("P1")));
    }

    private async Task WhileInitializationIsBlocked(Func<Task> operation, Action mutate)
    {
        using var blocker = _factory.Open(Database);
        using (var setup = blocker.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE CaptureGate (Value INTEGER);";
            setup.ExecuteNonQuery();
        }
        using var transaction = blocker.BeginTransaction();
        using (var write = blocker.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = "INSERT INTO CaptureGate VALUES (1);";
            write.ExecuteNonQuery();
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Run(async () =>
        {
            started.SetResult();
            await operation();
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            Assert.False(pending.IsCompleted, "The synthetic write lock must delay initialization.");
            mutate();
        }
        finally
        {
            transaction.Rollback();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static ElectricalProject Project() => new()
    {
        ProjectId = "P1", Name = "captured",
        Components = [new ComponentInstance
        {
            ComponentInstanceId = "C1", ComponentDefinitionId = "D1", TypeKey = "device",
            Ports = [new ComponentPort { PortId = "PORT-1", Name = "original", Capabilities = ["24V"] }]
        }]
    };

    private static void Mutate(ElectricalProject project)
    {
        project.Name = "newer";
        project.Components[0].Ports[0].Name = "changed";
        project.Components[0].Ports[0].Capabilities.Add("newer");
    }

    private static void AssertCapturedContent(ElectricalProject project)
    {
        Assert.Equal("captured", project.Name);
        Assert.Equal("original", project.Components[0].Ports[0].Name);
        Assert.Equal(["24V"], project.Components[0].Ports[0].Capabilities);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
