using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;
using Microsoft.Data.Sqlite;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ProjectRecoveryCheckpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ci-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory = new();
    private string Database => Path.Combine(_root, "synthetic.db");
    private ProjectRevisionRepository Repository => new(_factory, Database);

    [Fact]
    public async Task TwentyOneAutomaticCheckpoints_KeepNewestTwentyAndPreserveOtherTriggersAndProjects()
    {
        var repository = Repository;
        var service = new ProjectRevisionService(repository);
        var project = new ElectricalProject { ProjectId = "P1" };
        foreach (var trigger in Enum.GetValues<ProjectRevisionTrigger>().Where(t => t != ProjectRevisionTrigger.AutoRecovery))
            await service.CreateCheckpointAsync(project, trigger, "preserved " + trigger);
        var other = await service.CreateRecoveryCheckpointAsync(new ElectricalProject { ProjectId = "P2", Name = "other" });
        var automaticIds = new List<string>();
        for (var i = 0; i < 21; i++)
        {
            project.Name = "edit " + i;
            automaticIds.Add((await service.CreateRecoveryCheckpointAsync(project, "recovery " + i)).RevisionId);
        }

        var all = await repository.ListAsync("P1");
        Assert.Equal(20, all.Count(row => row.Trigger == "AutoRecovery"));
        Assert.DoesNotContain(all, row => row.RevisionId == automaticIds[0]);
        foreach (var id in automaticIds.Skip(1)) Assert.Contains(all, row => row.RevisionId == id);
        foreach (var trigger in Enum.GetValues<ProjectRevisionTrigger>().Where(t => t != ProjectRevisionTrigger.AutoRecovery))
            Assert.Contains(all, row => row.Trigger == trigger.ToString());
        Assert.Equal(other, Assert.Single(await repository.ListAsync("P2")));
        var summaries = await service.ListRecoveryAsync("P1");
        Assert.Equal(20, summaries.Count);
        Assert.Equal(automaticIds[^1], summaries[0].RevisionId);
        Assert.Equal("recovery 20", summaries[0].Label);
        Assert.All(summaries, summary => Assert.Equal("AutoRecovery", summary.Trigger));
    }

    [Fact]
    public async Task RecoveryInsertAndPrune_RollsBackTogetherWhenPruningFails()
    {
        var repository = Repository;
        for (var i = 0; i < 20; i++) await repository.InsertAsync(Row(i));
        using (var connection = _factory.Open(Database))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER RejectRecoveryPrune BEFORE DELETE ON ElectricalProjectRevisions
                WHEN OLD.Trigger = 'AutoRecovery'
                BEGIN SELECT RAISE(ABORT, 'synthetic prune failure'); END;
                """;
            command.ExecuteNonQuery();
        }

        await Assert.ThrowsAsync<SqliteException>(() => repository.InsertAsync(Row(20)));
        Assert.Equal(20, (await repository.ListAsync("P1")).Count);
        Assert.Null(await repository.GetAsync("REV-020"));
    }

    [Fact]
    public async Task Retention_UsesTimestampThenRevisionIdRatherThanInsertionOrder()
    {
        var repository = Repository;
        for (var i = 20; i >= 0; i--) await repository.InsertAsync(Row(i) with { CreatedUtc = DateTimeOffset.UnixEpoch });
        Assert.Null(await repository.GetAsync("REV-000"));
        Assert.Equal("REV-020", (await repository.LatestAsync("P1"))!.RevisionId);
        Assert.Equal(20, (await repository.ListRecoveryAsync("P1")).Count);
    }

    [Fact]
    public async Task Latest_ReturnsOnlyNewestRevisionOfRequestedProject()
    {
        var repository = Repository;
        Assert.Null(await repository.LatestAsync("missing"));
        Assert.Empty(await repository.ListRecoveryAsync("missing"));
        await repository.InsertAsync(Row(1) with { Trigger = "Save" });
        await repository.InsertAsync(Row(2) with { Trigger = "MajorImport" });
        await repository.InsertAsync(Row(3) with { ProjectId = "P2" });
        Assert.Equal("REV-002", (await repository.LatestAsync("P1"))!.RevisionId);
    }

    [Fact]
    public async Task RecoverySummaries_DoNotDeserializeSnapshotPayloads()
    {
        await Repository.InsertAsync(Row(0) with { SnapshotJson = "invalid snapshot", SummaryJson = "{\"engineeringChanges\":[\"ProjectCreated\"]}" });
        var summary = Assert.Single(await Repository.ListRecoveryAsync("P1"));
        Assert.Equal("REV-000", summary.RevisionId);
        Assert.Equal("{\"engineeringChanges\":[\"ProjectCreated\"]}", summary.SummaryJson);
    }

    [Fact]
    public async Task RecoveryCheckpoint_CapturesImmediatelyBeforeDatabaseWaits()
    {
        using var blocker = _factory.Open(Database);
        using (var command = blocker.CreateCommand())
        {
            command.CommandText = "CREATE TABLE CaptureGate (Value INTEGER);";
            command.ExecuteNonQuery();
        }
        using var transaction = blocker.BeginTransaction();
        var project = new ElectricalProject { ProjectId = "P1", Name = "captured" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Run(async () =>
        {
            started.SetResult();
            return await new ProjectRevisionService(Repository).CreateRecoveryCheckpointAsync(project);
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            Assert.False(pending.IsCompleted);
            project.Name = "newer";
        }
        finally
        {
            transaction.Rollback();
        }
        var checkpoint = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        using var payload = JsonDocument.Parse(checkpoint.SnapshotJson);
        Assert.Equal("captured", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal("AutoRecovery", checkpoint.Trigger);
    }

    private static ProjectRevisionRow Row(int number) => new(
        $"REV-{number:D3}", "P1", DateTimeOffset.UnixEpoch.AddSeconds(number), "AutoRecovery", null, "{}", "{}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
