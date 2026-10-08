using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableWiringEditorTests
{
    private readonly CableManufacturingEditorService _table = new();
    private readonly CableWiringEditorService _wiring = new();

    [Fact]
    public void InventoryRowsSortIndependentlyAndNeverCreateConnections()
    {
        var template = CableManufacturingEditorTests.Template();
        template.Ports[0].Pins.Add(new() { PinId = "p1.10", PinNumber = "10" });
        template.Ports[0].Pins.Reverse();
        template.Ports[1].Pins[1].PinNumber = "7";
        var draft = _table.Prepare(template);
        _wiring.EnterVisualMode(draft);
        Assert.Equal(new[] { "p1.1", "p1.2", "p1.10" }, draft.Rows.Select(r => r.FromSourcePinId));
        Assert.Equal(new[] { "p2.1", "p2.2", null }, draft.Rows.Select(r => r.ToSourcePinId));
        Assert.Empty(_table.Apply(template, draft).Mapping);
        Assert.Equal("7", draft.Ends[1].Pins.Single(p => p.PinId == "p2.2").PinNumber);
    }

    [Fact]
    public void LegacyBranchMigrationPreservesEveryExactPair()
    {
        var template = CableManufacturingEditorTests.Template(3);
        var old = _table.Prepare(template);
        old.Rows = [new() { FromSourcePinId = "p1.1", ToSourcePinId = "p2.2",
            AdditionalEnds = new() { ["P3"] = new() { SourcePinId = "p3.1" } } }];
        var saved = _table.Apply(template, old);
        var draft = _table.Prepare(saved);
        _wiring.EnterVisualMode(draft);
        Assert.Equal(saved.Mapping.OrderBy(CableWiringEditorService.EdgeKey),
            _table.Apply(saved, draft).Mapping.OrderBy(CableWiringEditorService.EdgeKey));
        Assert.True(draft.InventoryOnly);
    }

    [Fact]
    public void AmbiguousOldUnboundTextIsNotLostDuringMigration()
    {
        var draft = _table.Prepare(CableManufacturingEditorTests.Template());
        draft.Rows[0].ToFunction = "unassigned destination";
        var before = JsonSerializer.Serialize(draft);
        Assert.Throws<InvalidOperationException>(() => _wiring.EnterVisualMode(draft));
        Assert.Equal(before, JsonSerializer.Serialize(draft));
    }

    [Fact]
    public void SamePortShortAndBranchAreExplicitAndNcCannotBeWired()
    {
        var draft = _table.Prepare(CableManufacturingEditorTests.Template());
        _wiring.EnterVisualMode(draft);
        _wiring.Connect(draft, "p1.1", "p1.2");
        _wiring.Connect(draft, "p1.1", "p2.1");
        Assert.Equal(2, draft.ExplicitMapping!.Count);
        Assert.Throws<InvalidOperationException>(() => _wiring.Connect(draft, "p2.1", "p1.1"));
        Assert.Throws<InvalidOperationException>(() => _wiring.SetUsage(draft, "p1.1", CablePinUsage.Nc));
        _wiring.SetUsage(draft, "p2.2", CablePinUsage.Nc);
        Assert.Throws<InvalidOperationException>(() => _wiring.Connect(draft, "p2.2", "p1.1"));
        Assert.Throws<InvalidOperationException>(() => _wiring.Connect(draft, "missing", "p1.1"));
        _wiring.Disconnect(draft, "p1.2", "p1.1");
        Assert.Single(draft.ExplicitMapping);
    }

    [Fact]
    public void LayoutOnlyMovePreservesMappingAndConfirmationAfterInstanceSave()
    {
        var template = CableManufacturingEditorTests.Template() with
        { Mapping = [new("p1.1", "p2.1"), new("p1.2", "p2.2")], MappingConfirmed = true,
            MappingRevision = "r1", MappingEvidence = "synthetic verified source" };
        var draft = _table.Prepare(template);
        _wiring.EnterVisualMode(draft);
        var visual = _table.Apply(template, draft);
        Assert.True(visual.MappingConfirmed);
        var instance = ArchivedCableInstanceFactory.Create(visual, CableConstructionType.Unknown);
        var sibling = ArchivedCableInstanceFactory.Create(visual, CableConstructionType.Unknown);
        var siblingBefore = JsonSerializer.Serialize(sibling);
        var edit = _table.Prepare(instance);
        _wiring.MovePort(edit, "P1", new(70, 60));
        var changed = _table.ApplyToInstance(instance, edit);
        Assert.True(changed.ArchivedCable!.MappingConfirmed);
        Assert.False(changed.ArchivedCable.HasMappingOverride);
        Assert.Equal(instance.ArchivedCable!.Mapping, changed.ArchivedCable.Mapping);
        Assert.Equal(siblingBefore, JsonSerializer.Serialize(sibling));
        _wiring.Disconnect(edit, "p1.1", "p2.1");
        Assert.False(_table.ApplyToInstance(instance, edit).ArchivedCable!.MappingConfirmed);
    }

    [Fact]
    public void MovedPinDiscardsObsoleteAutomaticBendsAndKeepsExactInventory()
    {
        var draft = _table.Prepare(CableManufacturingEditorTests.Template());
        _wiring.EnterVisualMode(draft);
        _wiring.Connect(draft, "p1.1", "p2.1");
        _wiring.EnsureLayout(draft);
        var before = draft.Ends.SelectMany(p => p.Pins).Select(p => p.PinId).ToArray();
        _wiring.MovePin(draft, "p2.1", new(220, 120));
        var diagram = _wiring.Build(draft);
        Assert.Equal(before, draft.Ends.SelectMany(p => p.Pins).Select(p => p.PinId));
        var bounds = diagram.PortBounds["P2"];
        Assert.Equal(new SchematicPoint(220, bounds.Y + bounds.Height), diagram.PinPositions["p2.1"]);
        Assert.True(diagram.EdgePaths.Values.Single().Count <= 6);
        Assert.Equal(diagram.PinPositions["p2.1"], diagram.EdgePaths.Values.Single()[^1]);
    }

    [Fact]
    public void InvalidSavedGeometryAndStaleEdgeRoutesAreRejected()
    {
        var template = CableManufacturingEditorTests.Template();
        var draft = _table.Prepare(template); _wiring.EnterVisualMode(draft);
        draft.WiringDiagram!.PinPositions["p1.1"] = new(double.NaN, 2);
        Assert.NotEmpty(_table.Validate(draft));
        Assert.Throws<InvalidOperationException>(() => _table.Apply(template, draft));
    }

    [Fact]
    public void DraggingPinCannotOverlapAnotherLabelInItsConnector()
    {
        var draft = _table.Prepare(CableManufacturingEditorTests.Template()); _wiring.EnterVisualMode(draft);
        var other = draft.WiringDiagram!.PinPositions["p1.2"];
        Assert.Throws<InvalidOperationException>(() => _wiring.MovePin(draft, "p1.1", new(other.X + 100, other.Y)));
    }

    [Fact]
    public void DraggedGroundPinSnapsToItsOwnPortAndReroutesAfterSave()
    {
        var template = CableManufacturingEditorTests.Template(3);
        var draft = _table.Prepare(template); _wiring.EnterVisualMode(draft);
        _wiring.Connect(draft, "p2.1", "p3.1");
        var bounds = draft.WiringDiagram!.PortBounds["P3"];
        var ground = draft.WiringDiagram.PinPositions["p3.1"];
        _wiring.MovePin(draft, "p3.1", new(bounds.X + bounds.Width + 30, ground.Y));
        var reopened = _table.Prepare(_table.Apply(template, draft));
        var diagram = _wiring.Build(reopened);
        var expected = new SchematicPoint(bounds.X + bounds.Width, ground.Y);
        Assert.Equal(expected, diagram.PinPositions["p3.1"]);
        Assert.Equal(expected, diagram.EdgePaths.Values.Single()[^1]);
        Assert.Single(reopened.ExplicitMapping!);
    }

    [Fact]
    public void RenderingOlderDetachedPinRepairsPresentationWithoutChangingSource()
    {
        var draft = _table.Prepare(CableManufacturingEditorTests.Template(3)); _wiring.EnterVisualMode(draft);
        var bounds = draft.WiringDiagram!.PortBounds["P3"];
        var pin = draft.WiringDiagram.PinPositions["p3.1"];
        draft.WiringDiagram.PinPositions["p3.1"] = pin with { X = pin.X + 30 };
        var before = JsonSerializer.Serialize(draft);
        Assert.Equal(new SchematicPoint(bounds.X + bounds.Width, pin.Y), _wiring.Build(draft).PinPositions["p3.1"]);
        Assert.Equal(before, JsonSerializer.Serialize(draft));
    }
}
