using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class UiRetirementCompatibilityTests
{
    [Fact]
    public async Task RetiredUiDoesNotRemoveHistoricalAuthorityDuringSqliteRoundTrip()
    {
        var project = MultiEndCableEditorServiceTests.Fixture(2);
        var editor = new MultiEndCableEditorService();
        var draft = editor.PrepareNew(project, project.Connections.Select(c => c.ConnectionId).ToArray());
        editor.ConfirmEnds(project, draft, "common");
        draft.ConstructionType = CableConstructionType.Custom;
        editor.Apply(project, draft).IsCustom = true;
        project.Components.Add(new() { ComponentInstanceId = "notion:opaque-instance", ComponentDefinitionId = "notion:opaque-definition", TypeKey = "legacy", Footprint = new() { WidthMm = 20, HeightMm = 30 }, Placement = new() { ParentContainerId = "cab", XMm = 15, YMm = 25, Surface = MountingSurface.Backplate } });
        project.LayoutContainers.Add(new() { ContainerId = "cab", Name = "Cabinet", WidthMm = 400, HeightMm = 500 });
        project.TopologyPlacements.Add(new() { ObjectId = "notion:opaque-instance", ObjectKind = "COMPONENT", X = 125, Y = 250 });
        project.TopologyRoutes.Add(new() { ConnectionId = "wire1-1", Points = [new() { X = 1, Y = 2 }, new() { X = 3, Y = 2 }] });
        project.TerminalBlocks.Add(new() { TerminalBlockId = "legacy-tb", ReferenceDesignator = "TB-OLD", Jumpers = [new() { JumperId = "legacy-jumper", PartNumber = "keep-exact", ConnectionPointIds = ["old-cp-1", "old-cp-2"] }] });
        project.Cables.Add(new() { CableInstanceId = "legacy-cable", CableDefinitionId = "notion:legacy-cable", ProvidedLengthMm = 1234, LengthSource = CableLengthSource.Imported, CoreAssignments = [new() { CoreId = "core-1", FromEndpointId = "legacy-a", ToEndpointId = "legacy-b", Signal = "KEEP" }] });
        project.Connections.Add(new() { ConnectionId = "legacy-connection", FromEndpointId = "legacy-a", ToEndpointId = "legacy-b", CableInstanceId = "legacy-cable", Kind = ConnectionKind.Cable });
        project.CableAssemblies.Add(new() { CableAssemblyId = "legacy-assembly", Members = [new() { CableInstanceId = "legacy-cable", Purpose = "legacy-purpose", SegmentRoleType = CableAssemblySegmentRoleType.Unknown }] });
        project.DrawingPlan = new() { ProjectId = project.ProjectId, SourcePlanningInputHash = "preserve-input", SourcePagePlanHash = "preserve-pages" };
        var before = JsonSerializer.Serialize(project);
        var path = Path.Combine(Path.GetTempPath(), $"task013-compat-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repo.SaveAsync(project);
            var loaded = await repo.GetAsync(project.ProjectId);
            Assert.NotNull(loaded);
            Assert.Equal(before, JsonSerializer.Serialize(loaded));
            await repo.SaveAsync(loaded!);
            Assert.Equal(before, JsonSerializer.Serialize(await repo.GetAsync(project.ProjectId)));
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
