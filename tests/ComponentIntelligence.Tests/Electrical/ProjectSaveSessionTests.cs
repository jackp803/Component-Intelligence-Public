using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ProjectSaveSessionTests
{
    [Fact]
    public void Clone_IsolatesNestedContentInBothDirections()
    {
        var project = Project();
        project.Schematic = new SchematicDocument { Pages = [new SchematicPage { PageId = "PAGE", Title = "sheet" }] };
        var clone = ProjectSaveSession.Clone(project);
        clone.Components[0].Ports[0].Capabilities.Add("clone");
        clone.Buses[0].NodeAddresses["node"] = 99;
        Assert.IsType<SchematicDocument>(clone.Schematic).Pages.Clear();
        project.Components[0].Ports[0].Name = "original changed";

        Assert.Equal(["24V"], project.Components[0].Ports[0].Capabilities);
        Assert.Equal(1, project.Buses[0].NodeAddresses["node"]);
        Assert.Single(project.Schematic.Pages);
        Assert.Equal("port", clone.Components[0].Ports[0].Name);
        Assert.Equal(ProjectSaveSession.Fingerprint(Project()), ProjectSaveSession.Fingerprint(ProjectSaveSession.Clone(Project())));
    }

    [Fact]
    public void CompletionOfOlderSave_LeavesNewerCurrentDirty()
    {
        var project = Project();
        var session = new ProjectSaveSession();
        session.Begin(project, persisted: true);
        project.Name = "save captured";
        var capture = session.Capture(project);
        project.Name = "newer edit";

        Assert.False(session.Complete(capture, ProjectSaveSession.Clone(capture.Snapshot), project));
        Assert.True(session.IsDirty(project));
        project.Name = "save captured";
        Assert.False(session.IsDirty(project));
    }

    [Fact]
    public void NewEpochWithSameProjectId_RejectsOldCompletionAndPreservesBaseline()
    {
        var session = new ProjectSaveSession();
        var old = Project();
        session.Begin(old, persisted: true);
        var capture = session.Capture(old);
        var reopened = Project();
        reopened.Name = "reopened baseline";
        session.Begin(reopened, persisted: true);

        Assert.True(session.Epoch > capture.Epoch);
        Assert.False(session.Complete(capture, capture.Snapshot, reopened));
        Assert.False(session.IsDirty(reopened));
    }

    [Fact]
    public void UndoToExactSavedContent_ClearsDirtyAcrossDifferentObjects()
    {
        var baseline = Project();
        var session = new ProjectSaveSession();
        session.Begin(baseline, persisted: true);
        var history = new ProjectMutationHistory();
        history.RecordBeforeMutation(baseline, "rename");
        baseline.Name = "edited";
        Assert.True(session.IsDirty(baseline));

        Assert.True(history.TryUndo(baseline, out var restored, out _));
        Assert.False(session.IsDirty(restored));
    }

    [Fact]
    public void FreshUnsavedProject_IsCleanUntilModified()
    {
        var project = new ElectricalProject { ProjectId = "NEW" };
        var session = new ProjectSaveSession();
        session.Begin(project, persisted: false);
        Assert.False(session.IsDirty(project));
        project.Name = "first edit";
        Assert.True(session.IsDirty(project));
    }

    [Fact]
    public void Capture_IsolatedFromLaterEditsAndContainsCurrentEpochAndFingerprint()
    {
        var project = Project();
        var session = new ProjectSaveSession();
        session.Begin(project, persisted: true);
        var capture = session.Capture(project);
        project.Components[0].Ports[0].Name = "changed";

        Assert.Equal("port", capture.Snapshot.Components[0].Ports[0].Name);
        Assert.Equal(session.Epoch, capture.Epoch);
        Assert.Equal(ProjectSaveSession.Fingerprint(capture.Snapshot), capture.Fingerprint);
        Assert.NotEqual(ProjectSaveSession.Fingerprint(project), capture.Fingerprint);
    }

    [Fact]
    public void EqualPayloadInDifferentObject_IsSafeToReplaceAndUsesPersistedBaseline()
    {
        var project = Project();
        var session = new ProjectSaveSession();
        session.Begin(project, persisted: false);
        var capture = session.Capture(project);
        var persisted = ProjectSaveSession.Clone(project);
        persisted.Name = "normalized saved name";

        Assert.True(session.Complete(capture, persisted, ProjectSaveSession.Clone(project)));
        Assert.False(session.IsDirty(persisted));
        persisted.Name = "later mutation";
        Assert.True(session.IsDirty(persisted));
    }

    [Fact]
    public void DifferentProjectCompletion_CannotOverwriteSessionBaseline()
    {
        var project = Project();
        var session = new ProjectSaveSession();
        session.Begin(project, persisted: true);
        var capture = session.Capture(project);
        Assert.False(session.Complete(capture, new ElectricalProject { ProjectId = "OTHER" }, project));
        Assert.False(session.IsDirty(project));
    }

    private static ElectricalProject Project() => new()
    {
        ProjectId = "P1", Name = "baseline",
        Components = [new ComponentInstance
        {
            ComponentInstanceId = "C1", ComponentDefinitionId = "D1", TypeKey = "device",
            Ports = [new ComponentPort { PortId = "PORT", Name = "port", Capabilities = ["24V"] }]
        }],
        Buses = [new CommunicationBus { BusId = "BUS", Protocol = "CAN", NodeAddresses = new() { ["node"] = 1 } }]
    };
}
