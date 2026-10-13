using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ProjectMutationHistoryMemoryTests
{
    [Fact]
    public void DefaultHistoryRetainsAtMostOneHundredRecentEdits()
    {
        var history = new ProjectMutationHistory();
        var current = new ElectricalProject { ProjectId = "history" };
        for (var i = 0; i < 150; i++)
        {
            current.Name = i.ToString();
            history.RecordBeforeMutation(current, "edit " + i);
        }
        var count = 0;
        while (history.TryUndo(current, out var previous, out _)) { current = previous; count++; }
        Assert.Equal(100, count);
        Assert.Equal("50", current.Name);
        while (history.TryRedo(current, out var next, out _)) current = next;
        Assert.Equal("149", current.Name);
    }

    [Fact]
    public void SnapshotsRemainDetachedAndNewEditClearsRedo()
    {
        var history = new ProjectMutationHistory();
        var current = new ElectricalProject { ProjectId = "history", Name = new string('x', 200_000) };
        history.RecordBeforeMutation(current, "before");
        current.Name = "after";
        Assert.True(history.TryUndo(current, out var previous, out var description));
        Assert.Equal("before", description);
        Assert.Equal(200_000, previous.Name.Length);
        history.RecordBeforeMutation(previous, "branch");
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void CompressedSnapshotsAvoidRetainingLargeJsonStrings()
    {
        var history = new ProjectMutationHistory();
        history.RecordBeforeMutation(new() { ProjectId = "history", Name = new string('x', 200_000) }, "large");
        Assert.InRange(history.RetainedBytes, 1, 10_000);
        history.Clear();
        Assert.Equal(0, history.RetainedBytes);
    }

    [Fact]
    public void ByteBudgetKeepsNewestUsableSnapshotEvenIfOneSnapshotExceedsBudget()
    {
        var history = new ProjectMutationHistory(maxBytes: 1);
        var project = new ElectricalProject { ProjectId = "history", Name = "old" };
        history.RecordBeforeMutation(project, "old");
        project.Name = "new";
        history.RecordBeforeMutation(project, "new");
        Assert.True(history.TryUndo(project, out var restored, out _));
        Assert.Equal("new", restored.Name);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }
}
