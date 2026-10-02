using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableManufacturingRoundTripTests
{
    [Fact]
    public async Task OldProjectLoadsWithoutManufacturingFields()
    {
        using var files = new Files();
        var old = JsonNode.Parse(JsonSerializer.Serialize(ArchivedCableManufacturingDetailTests.Project()))!;
        var binding = old["Cables"]![0]!["ArchivedCable"]!;
        binding.AsObject().Remove("ManufacturingGeometry"); binding.AsObject().Remove("Manufacturing");
        binding["Template"]!.AsObject().Remove("Manufacturing");
        old["Schematic"]!["Pages"]![0]!.AsObject().Remove("InlineCableDetails");
        await files.Repository.InitializeAsync();
        using (var connection = new SqliteConnectionFactory().Open(files.Database))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO ElectricalProjects VALUES ('test', 'legacy', NULL, $json, '2026-01-01');";
            command.Parameters.AddWithValue("$json", old.ToJsonString()); command.ExecuteNonQuery();
        }
        var loaded = await files.Repository.GetAsync("test");
        Assert.Null(loaded!.Cables[0].ArchivedCable!.ManufacturingGeometry);
        Assert.Empty(loaded.Schematic!.Pages[0].InlineCableDetails);
    }

    [Fact]
    public async Task NewProjectReloadsWithAllDraftRowsAndSelections()
    {
        using var files = new Files(); var p = ArchivedCableManufacturingDetailTests.Project();
        var cable = p.Cables[0]; var geometry = cable.ArchivedCable!.ManufacturingGeometry! with { SelectionBounds = new(20, 0, 100, 20) };
        geometry = geometry with { SelectionSha256 = SchematicCadSelectionService.GeometryHash(geometry) };
        cable.ArchivedCable = cable.ArchivedCable with { ManufacturingGeometry = geometry };
        var editor = new CableManufacturingEditorService(); var draft = editor.Prepare(cable);
        draft.Rows.Add(new() { FromFunction = "unknown endpoint" });
        p = new SchematicAuthoringService().SetArchivedCableManufacturing(p, cable.CableInstanceId, draft);
        p = new SchematicCableDetailService().PlaceArchivedDetail(p, cable.CableInstanceId, "sheet", new(20, 30), new(230, 100));
        await files.Repository.SaveAsync(p);
        var loaded = (await files.Repository.GetAsync(p.ProjectId))!;
        Assert.Contains(editor.Prepare(loaded.Cables[0]).Rows, r => r.FromFunction == "unknown endpoint" && r.FromSourcePinId is null);
        Assert.Equal(geometry.SelectionSha256, loaded.Cables[0].ArchivedCable!.ManufacturingGeometry!.SelectionSha256);
        Assert.Equal(geometry.SelectionBounds, loaded.Cables[0].ArchivedCable!.ManufacturingGeometry!.SelectionBounds);
        Assert.Equal(new SchematicPoint(230, 100), loaded.Schematic!.Pages[0].InlineCableDetails[0].SketchPosition);
        Assert.False(loaded.Cables[0].ArchivedCable!.MappingConfirmed);
    }

    [Fact]
    public async Task ArchiveRelocationDoesNotChangeSavedGeometry()
    {
        using var files = new Files(); var p = ArchivedCableManufacturingDetailTests.Project();
        var service = new SchematicCableDetailService(); p = service.AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        var before = JsonSerializer.Serialize(service.Build(p, p.Schematic!.Pages.Last()));
        await files.Repository.SaveAsync(p);
        var reloaded = (await files.Repository.GetAsync(p.ProjectId))!;
        Assert.Equal(before, JsonSerializer.Serialize(service.Build(reloaded, reloaded.Schematic!.Pages.Last())));
        Assert.Single(reloaded.Cables);
    }

    [Fact]
    public void ChangedTemplateDoesNotUpgradeExistingProject()
    {
        var p = ArchivedCableManufacturingDetailTests.Project(); var original = JsonSerializer.Serialize(p);
        var source = CableManufacturingEditorTests.Template() with { TemplateRevision = "r2", DisplayName = "new version" };
        source.Ports[0].Pins[0].Function = "new function";
        Assert.Equal(original, JsonSerializer.Serialize(p));
        Assert.Equal("r1", p.Cables[0].ArchivedCable!.Template.TemplateRevision);
    }

    [Fact]
    public void FailedExportDoesNotMutateProject()
    {
        var p = ArchivedCableManufacturingDetailTests.Project();
        p = new SchematicCableDetailService().AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        p.Cables[0].Specification = new string('X', 4000);
        var original = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => new SchematicDxfExporter().Create(p));
        Assert.Equal(original, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void InvalidManufacturingSnapshotCannotBePersistedAsValid()
    {
        var p = ArchivedCableManufacturingDetailTests.Project();
        var binding = p.Cables[0].ArchivedCable!;
        p.Cables[0].ArchivedCable = binding with { ManufacturingGeometry = binding.ManufacturingGeometry! with { Width = -10 } };
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(p));
    }

    [Fact]
    public void ManufacturingGapsAreAvailableInDraftReview()
    {
        var p = ArchivedCableManufacturingDetailTests.Project();
        p = new SchematicCableDetailService().AddArchivedDetail(p, p.Cables[0].CableInstanceId);
        Assert.Contains(SchematicDraftReview.Inspect(p), i => i.Code == "CABLE_TABLE_INCOMPLETE" && i.PageId == p.Schematic!.Pages.Last().PageId);
    }

    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cable-roundtrip-" + Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(_root, "test.db");
        public ElectricalProjectRepository Repository => new(new SqliteConnectionFactory(), Database);
        public Files() => Directory.CreateDirectory(_root);
        public void Dispose() => Directory.Delete(_root, true);
    }
}
