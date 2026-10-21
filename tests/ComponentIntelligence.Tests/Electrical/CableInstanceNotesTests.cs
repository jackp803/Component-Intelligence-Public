using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableInstanceNotesTests
{
    [Fact]
    public void ClearingNotesDoesNotChangeInternalWiringOrOtherCableMetadata()
    {
        var project = ArchivedCableManufacturingDetailTests.Project(); var author = new SchematicAuthoringService();
        var id = project.Cables[0].CableInstanceId;
        project = author.SetArchivedCableDetails(project, id, "C1", 500, "spec", CableConstructionType.Custom,
            project.Cables[0].ArchivedCable!.Mapping, " Original\r\nSecond line ");
        Assert.Equal("Original\nSecond line", project.Cables[0].Notes);
        var mapping = JsonSerializer.Serialize(project.Cables[0].ArchivedCable);
        var cleared = author.SetArchivedCableDetails(project, id, "C1", 500, "spec", CableConstructionType.Custom,
            project.Cables[0].ArchivedCable!.Mapping, "  ");
        Assert.Null(cleared.Cables[0].Notes); Assert.Equal(500, cleared.Cables[0].ProvidedLengthMm);
        Assert.Equal(mapping, JsonSerializer.Serialize(cleared.Cables[0].ArchivedCable));
        Assert.Equal("Original\nSecond line", project.Cables[0].Notes);
    }

    [Fact]
    public void NotesCannotSilentlyOverflowAnExistingManufacturingTable()
    {
        var project = ArchivedCableManufacturingDetailTests.Project(); var details = new SchematicCableDetailService();
        var cable = project.Cables[0];
        project = details.AddArchivedDetail(project, cable.CableInstanceId, layout: new(cable.CableInstanceId, null) { SketchVisible = false });
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => new SchematicAuthoringService().SetArchivedCableDetails(project,
            cable.CableInstanceId, "C1", 500, "spec", CableConstructionType.Custom, cable.ArchivedCable!.Mapping, new string('X', 4000)));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void InstanceNotesSurviveReopenAndUnrelatedPropertyEdits()
    {
        var project = ArchivedCableManufacturingDetailTests.Project();
        var node = JsonSerializer.SerializeToNode(project)!;
        node["Cables"]![0]!["Notes"] = "Upper sensor\nKeep service loop";
        var reopened = node.Deserialize<ElectricalProject>()!;
        var cable = reopened.Cables[0];
        var updated = new SchematicAuthoringService().SetArchivedCableDetails(reopened, cable.CableInstanceId,
            "C1", 500, "6 cores", CableConstructionType.Custom, cable.ArchivedCable!.Mapping);
        var saved = JsonSerializer.SerializeToNode(updated)!;
        Assert.Equal("Upper sensor\nKeep service loop", saved["Cables"]![0]!["Notes"]?.GetValue<string>());
        var details = new SchematicCableDetailService();
        updated = details.AddArchivedDetail(updated, cable.CableInstanceId, layout: new(cable.CableInstanceId, null) { SketchVisible = false });
        var text = string.Join("\n", details.Build(updated, updated.Schematic!.Pages.Last()).Primitives.Select(p => p.Text));
        Assert.Contains("Upper sensor", text);
        Assert.Contains("Keep service loop", text);
    }
}
