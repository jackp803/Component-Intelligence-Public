using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableManufacturingInstanceEditTests
{
    [Fact]
    public void InstanceDraftEditDoesNotModifyTemplateOrSibling()
    {
        var template = CableManufacturingEditorTests.Template();
        var p = Project(template); var before = JsonSerializer.Serialize(p);
        var service = new CableManufacturingEditorService();
        var draft = service.Prepare(p.Cables[0]);
        draft.Rows[0].ToSourcePinId = "p2.1"; draft.Rows[0].FromFunction = "+24V";
        var next = new SchematicAuthoringService().SetArchivedCableManufacturing(p, p.Cables[0].CableInstanceId, draft);
        Assert.Single(next.Cables[0].ArchivedCable!.Mapping);
        Assert.False(next.Cables[0].ArchivedCable!.MappingConfirmed);
        Assert.True(next.Cables[0].ArchivedCable!.HasMappingOverride);
        Assert.Empty(next.Cables[0].ArchivedCable!.Template.Mapping);
        Assert.Empty(next.Cables[1].ArchivedCable!.Mapping);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        Assert.Equal(p.Cables[0].ArchivedCable!.Ports[0].Pins[0].PinId, next.Cables[0].ArchivedCable!.Ports[0].Pins[0].PinId);
    }

    [Fact]
    public void ConnectedPinRemovalRejectedAtomically()
    {
        var p = Project(CableManufacturingEditorTests.Template());
        var before = JsonSerializer.Serialize(p);
        var draft = new CableManufacturingEditorService().Prepare(p.Cables[0]);
        draft.Rows.Clear(); draft.Ends[0].Pins.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => new SchematicAuthoringService().SetArchivedCableManufacturing(p, p.Cables[0].CableInstanceId, draft));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void CancelledTableEditChangesNothing()
    {
        var p = Project(CableManufacturingEditorTests.Template()); var original = JsonSerializer.Serialize(p);
        var draft = new CableManufacturingEditorService().Prepare(p.Cables[0]);
        draft.Rows[0].FromFunction = "edited"; draft.Ends[0].Name = "another end";
        Assert.Equal(original, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void IncompleteTableRowsRoundTrip()
    {
        var p = Project(CableManufacturingEditorTests.Template());
        var editor = new CableManufacturingEditorService(); var draft = editor.Prepare(p.Cables[0]);
        draft.Rows.Add(new() { FromFunction = "unknown endpoint" });
        var next = new SchematicAuthoringService().SetArchivedCableManufacturing(p, p.Cables[0].CableInstanceId, draft);
        var loaded = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        Assert.Contains(editor.Prepare(loaded.Cables[0]).Rows, r => r.FromFunction == "unknown endpoint" && r.FromSourcePinId is null);
        Assert.Empty(loaded.Cables[0].ArchivedCable!.Mapping);
    }

    [Fact]
    public void AllNewCommandsHaveNonConflictingShortcuts()
    {
        Assert.Contains(SchematicShortcutCatalog.All, s => s.Command == "CableArchive" && s.Gesture == "Ctrl+Alt+C");
        Assert.Equal(SchematicShortcutCatalog.All.Count, SchematicShortcutCatalog.All.Select(s => s.Gesture).Distinct().Count());
    }

    private static ElectricalProject Project(ArchivedCableTemplate template) => new()
    {
        ProjectId = "test", Cables = [ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom),
            ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom)],
        Schematic = new() { Pages = [new() { PageId = "sheet", Title = "sheet" }] }
    };
}
