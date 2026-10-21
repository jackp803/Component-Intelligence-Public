using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableWiringProjectPersistenceTests
{
    [Fact]
    public async Task DiagramBearingProjectIsSavedWithOldReaderRejectionAndExactLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "ci-wiring-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var template = CableManufacturingEditorTests.Template();
            var tables = new CableManufacturingEditorService(); var draft = tables.Prepare(template);
            var wiring = new CableWiringEditorService(); wiring.EnterVisualMode(draft); wiring.Connect(draft, "p1.1", "p2.2");
            wiring.MovePort(draft, "P2", new(280, 150));
            var project = new ElectricalProject { ProjectId = "diagram-project", Cables = [ArchivedCableInstanceFactory.Create(tables.Apply(template, draft), CableConstructionType.Unknown)] };
            var before = JsonSerializer.Serialize(project);
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "test.db"));
            await repository.SaveAsync(project);
            var readback = (await repository.GetAsync(project.ProjectId))!;
            Assert.Equal("0.8", readback.SchemaVersion);
            Assert.Equal(before, JsonSerializer.Serialize(project));
            Assert.Equal(JsonSerializer.Serialize(project.Cables), JsonSerializer.Serialize(readback.Cables));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OlderProjectWithoutDiagramKeepsItsSchema()
    {
        var project = new ElectricalProject { ProjectId = "legacy" };
        Assert.Equal("0.7", ElectricalProjectMigrator.Migrate(project).SchemaVersion);
    }
}
