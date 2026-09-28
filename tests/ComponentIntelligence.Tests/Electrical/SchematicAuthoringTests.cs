using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicAuthoringTests
{
    private readonly SchematicAuthoringService _service = new();

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(90, -1, 0)]
    [InlineData(180, 0, -1)]
    [InlineData(270, 1, 0)]
    public void MovingCadContactKeepsAnOutwardLeadIncludingRotation(int rotation, int dx, int dy)
    {
        var p = PlacedProject();
        var symbol = p.Schematic!.Symbols[0];
        p.Schematic.Symbols[0] = symbol with
        {
            Anchors = [new() { EndpointId = "A", Position = new(20, 40), Direction = "Bottom", Confirmed = true }]
        };
        p = _service.DrawWire(p, symbol.PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(40, 60), new(41, 60), new(41, 100)]);
        var next = _service.TransformSymbol(p, "S1", new(80, 20), rotation);
        var wire = Assert.Single(next.Schematic!.Wires);
        var anchor = SchematicAuthoringService.AnchorPoint(next.Schematic.Symbols[0], "A");
        Assert.Equal(anchor, wire.Points[0]);
        Assert.Equal(new SchematicPoint(anchor.X + dx * 5, anchor.Y + dy * 5), wire.Points[1]);
        Assert.Equal(new SchematicPoint(41, 100), wire.Points[^1]);
        Assert.Equal("A", wire.Start.EndpointId);
        Assert.Empty(next.Connections);
        SchematicAuthoringService.Validate(next);
        next = _service.TransformSymbol(next, "S1", new(110, 10), (rotation + 90) % 360);
        var route = next.Schematic!.Wires[0].Points;
        for (var i = 1; i < route.Count - 1; i++)
        {
            var a = route[i - 1]; var b = route[i]; var c = route[i + 1];
            Assert.False(a.X == b.X && b.X == c.X && (b.Y - a.Y) * (c.Y - b.Y) < 0);
            Assert.False(a.Y == b.Y && b.Y == c.Y && (b.X - a.X) * (c.X - b.X) < 0);
        }
    }

    [Fact]
    public void ClearingAwgDoesNotDeleteExistingMetricSpecification()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(100, 40)]);
        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Free(), Pin("S2", "B"), [new(20, 40), new(120, 40)]);
        p = _service.ConnectAcrossPages(p, p.Schematic!.Wires[0].WireId, false, p.Schematic.Wires[1].WireId, true, "S");
        p.Connections[0].ConductorAreaMm2 = 2.5;
        var next = _service.SetWireAwg(p, p.Schematic!.Wires[0].WireId, null);
        Assert.Equal(2.5, next.Connections[0].ConductorAreaMm2);
        Assert.All(next.Schematic!.Wires, w => Assert.Null(w.Awg));
    }

    [Fact]
    public void CompletingCrossPageDraftPropagatesExplicitWireSizeAndRejectsConflict()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(100, 40)]);
        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Free(), Pin("S2", "B"), [new(20, 40), new(120, 40)]);
        var first = p.Schematic!.Wires[0].WireId; var second = p.Schematic.Wires[1].WireId;
        p = _service.SetWireAwg(p, first, 18);
        var conflicting = _service.SetWireAwg(p, second, 22);
        Assert.Throws<InvalidOperationException>(() => _service.ConnectAcrossPages(conflicting, first, false, second, true, "S"));
        Assert.Empty(conflicting.Connections);
        var connected = _service.ConnectAcrossPages(p, first, false, second, true, "S");
        Assert.All(connected.Schematic!.Wires, w => Assert.Equal(18, w.Awg));
        Assert.InRange(Assert.Single(connected.Connections).ConductorAreaMm2!.Value, .82, .83);
        var changed = _service.SetWireAwg(connected, second, 20);
        Assert.All(changed.Schematic!.Wires, w => Assert.Equal(20, w.Awg));
    }

    [Fact]
    public void LegacyProjectLoadsWithoutInventingPagesOrConnections()
    {
        var legacy = new ElectricalProject { ProjectId = "OLD", SchemaVersion = "0.5" };
        var migrated = ElectricalProjectMigrator.Migrate(legacy);
        Assert.Equal("0.6", migrated.SchemaVersion);
        Assert.Null(migrated.Schematic);
        Assert.Empty(migrated.Connections);
    }

    [Fact]
    public void PageOrderChangesReferenceButKeepsStablePageAndEndpointIdentity()
    {
        var p = Project();
        p = _service.AddPage(p, "Source");
        p = _service.AddPage(p, "Receiver");
        var pages = p.Schematic!.Pages.Select(x => x.PageId).ToArray();
        p = _service.AddContinuation(p, "SUPPLY", pages[0], new(30, 40), pages[1], new(80, 90));
        var pair = p.Schematic!.Continuations.Single();
        var old = _service.ReferenceFor(p.Schematic, pair.Source.MarkerId);
        var reordered = _service.ReorderPages(p, pages.Reverse().ToArray());
        Assert.Contains("2 /", old);
        Assert.Contains("1 /", _service.ReferenceFor(reordered.Schematic!, pair.Source.MarkerId));
        Assert.Equal(pair, reordered.Schematic!.Continuations.Single());
        Assert.Empty(reordered.Connections);
        Assert.Equal(pages, p.Schematic.Pages.Select(x => x.PageId));
    }

    [Fact]
    public async Task DraftWithFreeEndPersistsWithoutCreatingFakeElectricalEndpoint()
    {
        var p = PlacedProject(); var page = p.Schematic!.Pages[0].PageId;
        p = _service.DrawWire(p, page, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(90, 40), new(90, 80)]);
        Assert.Empty(p.Connections);
        Assert.Null(p.Schematic!.Wires.Single().ConnectionId);
        var path = Path.Combine(Path.GetTempPath(), $"schematic-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repository.SaveAsync(p);
            var reloaded = await repository.GetAsync(p.ProjectId);
            Assert.Equal(JsonSerializer.Serialize(p.Schematic), JsonSerializer.Serialize(reloaded!.Schematic));
            Assert.Empty(reloaded.Connections);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void CompletingPairedContinuationCreatesOneConnectionWithOriginalPins()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.AddContinuation(p, "SIGNAL-1", pages[0].PageId, new(100, 40), pages[1].PageId, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Marker(pair.Source.MarkerId),
            [new(60, 40), new(100, 40)]);
        Assert.Empty(p.Connections);
        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Marker(pair.Destination.MarkerId), Pin("S2", "B"),
            [new(20, 40), new(120, 40)]);
        var connection = Assert.Single(p.Connections);
        Assert.Equal("A", connection.FromEndpointId);
        Assert.Equal("B", connection.ToEndpointId);
        Assert.Equal(ConnectionKind.Wire, connection.Kind);
        Assert.Null(connection.CableInstanceId);
        Assert.All(p.Schematic!.Wires, w => Assert.Equal(connection.ConnectionId, w.ConnectionId));
        Assert.Equal(2, p.Components.Count);
        Assert.Equal(2, p.Components.SelectMany(c => c.Ports).SelectMany(x => x.Pins).Count());
    }

    [Fact]
    public void InvalidBindingOrDiagonalRouteLeavesOriginalUntouched()
    {
        var p = PlacedProject(); var snapshot = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.DrawWire(p, p.Schematic!.Pages[0].PageId,
            Pin("S1", "NOT-A"), SchematicAttachment.Free(), [new(60, 40), new(80, 40)]));
        Assert.Throws<InvalidOperationException>(() => _service.DrawWire(p, p.Schematic!.Pages[0].PageId,
            Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(80, 60)]));
        Assert.Equal(snapshot, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void ResumingSavedDraftPreservesWireIdentityAndCreatesConnectionOnlyOnCompletion()
    {
        var p = PlacedProject(); var first = p.Schematic!.Pages[0].PageId;
        var second = p.Schematic.Pages[1].PageId;
        p = _service.AddContinuation(p, "S", first, new(100, 40), second, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, first, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(80, 40)]);
        var wire = p.Schematic!.Wires.Single();
        p = _service.CompleteDraft(p, wire.WireId, wire.Start, SchematicAttachment.Marker(pair.Source.MarkerId), [new(60, 40), new(100, 40)]);
        Assert.Empty(p.Connections);
        Assert.Equal(wire.WireId, p.Schematic!.Wires.Single().WireId);
        p = _service.DrawWire(p, second, SchematicAttachment.Marker(pair.Destination.MarkerId), Pin("S2", "B"), [new(20, 40), new(120, 40)]);
        Assert.Single(p.Connections);
        Assert.Equal(wire.WireId, p.Schematic!.Wires[0].WireId);
    }

    [Fact]
    public void DraftCompletionCannotReplaceAnExistingBoundPin()
    {
        var p = PlacedProject(); var page = p.Schematic!.Pages[0].PageId;
        p = _service.DrawWire(p, page, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(80, 40)]);
        var w = p.Schematic!.Wires.Single();
        Assert.Throws<InvalidOperationException>(() => _service.CompleteDraft(p, w.WireId, SchematicAttachment.Free(), SchematicAttachment.Free(), w.Points));
    }

    [Fact]
    public void SymbolMoveAndRotationKeepExactAnchorAndWireOrthogonal()
    {
        var p = PlacedProject();
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(90, 40), new(90, 80)]);
        var moved = _service.TransformSymbol(p, "S1", new(30, 50), 90);
        var symbol = moved.Schematic!.Symbols.Single(s => s.SymbolId == "S1");
        Assert.Equal(new SchematicPoint(50, 90), SchematicAuthoringService.AnchorPoint(symbol, "A"));
        Assert.Equal(new SchematicPoint(50, 90), moved.Schematic.Wires[0].Points[0]);
        Assert.Equal(new SchematicPoint(90, 80), moved.Schematic.Wires[0].Points[^1]);
        Assert.All(moved.Schematic.Wires[0].Points.Zip(moved.Schematic.Wires[0].Points.Skip(1)),
            pair => Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        Assert.Equal(new SchematicPoint(60, 40), p.Schematic!.Wires[0].Points[0]);
    }

    [Fact]
    public void LockedIncidentRouteRejectsMoveAtomically()
    {
        var p = PlacedProject();
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(90, 40)]);
        p.Schematic!.Wires[0] = p.Schematic.Wires[0] with { Locked = true };
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.TransformSymbol(p, "S1", new(30, 30), 0));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void HistoryRestoresEngineeringAndDrawingTogether()
    {
        var p = PlacedProject(); var history = new ProjectMutationHistory();
        history.RecordBeforeMutation(p, "Draw");
        var next = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(90, 40)]);
        Assert.True(history.TryUndo(next, out var restored, out _));
        Assert.Empty(restored.Schematic!.Wires);
        Assert.True(history.TryRedo(restored, out var redone, out _));
        Assert.Single(redone.Schematic!.Wires);
        Assert.Contains("SchematicRouteChanges", ProjectChangeSummary.Compare(p, next).VisualChanges);
    }

    [Fact]
    public void SegmentDragAndDetourKeepBothAnchorsAndConnectionIdentity()
    {
        var p = PlacedProject();
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(100, 40)]);
        var wire = p.Schematic!.Wires.Single();
        p = _service.MoveSegment(p, wire.WireId, 0, new(80, 60));
        Assert.Equal(new SchematicPoint(60, 40), p.Schematic!.Wires[0].Points[0]);
        Assert.Equal(new SchematicPoint(100, 40), p.Schematic.Wires[0].Points[^1]);
        Assert.Contains(new SchematicPoint(60, 60), p.Schematic.Wires[0].Points);
        p = _service.AddDetour(p, wire.WireId, 1, new(80, 80), 5);
        Assert.Contains(new SchematicPoint(75, 80), p.Schematic!.Wires[0].Points);
        Assert.Equal(wire.WireId, p.Schematic.Wires[0].WireId);
        Assert.Equal(wire.Start, p.Schematic.Wires[0].Start);
        Assert.Equal(wire.End, p.Schematic.Wires[0].End);
        Assert.Empty(p.Connections);
        SchematicAuthoringService.Validate(p);
    }

    [Fact]
    public void RemovingBendKeepsAnchorsAndLockedRouteRejectsAllEdits()
    {
        var p = PlacedProject();
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(80, 40), new(80, 60), new(100, 60), new(100, 40)]);
        var id = p.Schematic!.Wires[0].WireId;
        var shortened = _service.RemoveBend(p, id, 2);
        Assert.True(shortened.Schematic!.Wires[0].Points.Count < p.Schematic.Wires[0].Points.Count);
        Assert.Equal(p.Schematic.Wires[0].Points[0], shortened.Schematic.Wires[0].Points[0]);
        Assert.Equal(p.Schematic.Wires[0].Points[^1], shortened.Schematic.Wires[0].Points[^1]);
        p = _service.SetLocked(p, id, true);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.MoveSegment(p, id, 0, new(80, 70)));
        Assert.Throws<InvalidOperationException>(() => _service.AddDetour(p, id, 0, new(70, 50), 5));
        Assert.Throws<InvalidOperationException>(() => _service.RemoveBend(p, id, 2));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void AnchorCannotClaimAnotherCatalogSourceIdentity()
    {
        var p = PlacedProject();
        p.Components[0].Ports[0].SourcePortId = "CATALOG-PORT";
        p.Components[0].Ports[0].Pins[0].SourcePinId = "CATALOG-PIN";
        var symbol = p.Schematic!.Symbols[0];
        Assert.Throws<InvalidOperationException>(() => _service.SetSymbolDetails(p, symbol.SymbolId, null,
            [symbol.Anchors[0] with { SourcePortId = "WRONG", SourcePinId = "WRONG" }]));
    }

    [Fact]
    public void ImportedGeometryIsDraftAndDoesNotInventBindingsOrApproval()
    {
        var p = PlacedProject();
        var asset = new SchematicCadAsset { SourceSha256 = new string('A', 64), MillimetresPerUnit = 1, Width = 40, Height = 30,
            Primitives = [new() { Kind = "LINE", Start = new(0, 0), End = new(40, 30) }] };
        p = _service.SetSymbolGeometry(p, "S1", asset, "candidate.dxf");
        var symbol = p.Schematic!.Symbols[0];
        Assert.Equal(asset.SourceSha256, symbol.AssetSha256);
        Assert.Null(symbol.AssetRevision);
        Assert.All(symbol.Anchors, a => Assert.False(a.Confirmed));
        Assert.Empty(p.Connections);
        p = _service.SetPageTemplate(p, p.Schematic.Pages[0].PageId, asset, "template.dxf", 420, 297, 10, 8, 5);
        Assert.Equal(asset.SourceSha256, p.Schematic!.Pages[0].TemplateSha256);
        Assert.Equal(420, p.Schematic.Pages[0].Width);
        Assert.Equal(40, p.Schematic.Pages[0].TemplateGeometry!.Width);
    }

    [Fact]
    public void AssetReplacementCannotSilentlyMoveAlreadyWiredAnchors()
    {
        var p = PlacedProject();
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(80, 40)]);
        var asset = new SchematicCadAsset { SourceSha256 = new string('A', 64), MillimetresPerUnit = 1, Width = 60, Height = 30 };
        Assert.Throws<InvalidOperationException>(() => _service.SetSymbolGeometry(p, "S1", asset, "candidate.dxf"));
    }

    [Fact]
    public void MoveToAnotherPageAndBackPreservesConnectionAndPinMapping()
    {
        var p = PlacedProject(); var first = p.Schematic!.Pages[0].PageId; var second = p.Schematic.Pages[1].PageId;
        p = _service.MoveSymbolsToPage(p, ["S2"], first);
        p = _service.DrawWire(p, first, Pin("S1", "A"), Pin("S2", "B"), [new(60, 40), new(120, 40)]);
        var before = JsonSerializer.Serialize(p.Connections); var wireId = p.Schematic!.Wires[0].WireId;
        p = _service.MoveSymbolsToPage(p, ["S2"], second);
        Assert.Equal(before, JsonSerializer.Serialize(p.Connections));
        Assert.Single(p.Schematic!.Continuations); Assert.Equal(2, p.Schematic.Wires.Count);
        Assert.Equal(2, p.Schematic.Wires.Select(w => w.PageId).Distinct().Count());
        p = _service.MoveSymbolsToPage(p, ["S2"], first);
        Assert.Empty(p.Schematic!.Continuations);
        Assert.Single(p.Schematic.Wires);
        Assert.Equal(wireId, p.Schematic.Wires[0].WireId);
        Assert.Equal(before, JsonSerializer.Serialize(p.Connections));
        Assert.Equal(new SchematicPoint(60, 40), p.Schematic.Wires[0].Points[0]);
        Assert.Equal(new SchematicPoint(120, 40), p.Schematic.Wires[0].Points[^1]);
    }

    [Fact]
    public void MovingPairedMarkerUpdatesItsRouteAndReferenceWithoutChangingEngineering()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.AddContinuation(p, "SIGNAL", pages[0].PageId, new(100, 40), pages[1].PageId, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Marker(pair.Source.MarkerId), [new(60, 40), new(100, 40)]);
        var oldCaption = _service.ReferenceFor(p.Schematic!, pair.Destination.MarkerId);
        p = _service.MoveContinuationMarker(p, pair.Source.MarkerId, new(240, 200));
        Assert.Equal(new SchematicPoint(240, 200), p.Schematic!.Wires[0].Points[^1]);
        Assert.NotEqual(oldCaption, _service.ReferenceFor(p.Schematic, pair.Destination.MarkerId));
        Assert.Empty(p.Connections);
        p = _service.SetLocked(p, p.Schematic.Wires[0].WireId, true);
        Assert.Throws<InvalidOperationException>(() => _service.MoveContinuationMarker(p, pair.Source.MarkerId, new(250, 200)));
    }

    [Fact]
    public void PersistedWireCannotClaimAnUnrelatedElectricalConnection()
    {
        var p = PlacedProject();
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(80, 40)]);
        p.Connections.Add(new() { ConnectionId = "FORGED", FromEndpointId = "A", ToEndpointId = "B", Kind = ConnectionKind.Wire });
        p.Schematic!.Wires[0] = p.Schematic.Wires[0] with { ConnectionId = "FORGED" };
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(p));
    }

    [Fact]
    public void ContinuationCaptionIncludesActualRemoteReferencePortAndPin()
    {
        var p = PlacedProject(); p.Components[1].ReferenceDesignator = "-K2";
        var pages = p.Schematic!.Pages;
        p = _service.AddContinuation(p, "CONFIRMED-SIGNAL", pages[0].PageId, new(100, 40), pages[1].PageId, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Marker(pair.Destination.MarkerId), Pin("S2", "B"), [new(20, 40), new(120, 40)]);
        var caption = _service.ReferenceFor(p, pair.Source.MarkerId);
        Assert.Contains("-K2", caption); Assert.Contains("IO", caption); Assert.Contains("1", caption);
        Assert.Contains("CONFIRMED-SIGNAL", caption); Assert.Contains("2 /", caption);
    }

    [Fact]
    public void RenamePagePreservesIdentityAndDoesNotMutateOriginal()
    {
        var p = PlacedProject(); var id = p.Schematic!.Pages[0].PageId;
        var original = JsonSerializer.Serialize(p);
        var renamed = _service.RenamePage(p, id, "  Power input  ");
        Assert.Equal("Power input", renamed.Schematic!.Pages[0].Title);
        Assert.Equal(id, renamed.Schematic.Pages[0].PageId);
        Assert.Equal(original, JsonSerializer.Serialize(p));
        Assert.Equal(JsonSerializer.Serialize(p.Schematic.Symbols), JsonSerializer.Serialize(renamed.Schematic.Symbols));
        Assert.Throws<InvalidOperationException>(() => _service.RenamePage(p, id, "  "));
    }

    [Fact]
    public void DeleteOrdinaryConnectionRemovesAllItsPageSegmentsAtomically()
    {
        var p = PlacedProject(); var first = p.Schematic!.Pages[0].PageId;
        var second = p.Schematic.Pages[1].PageId;
        p = _service.MoveSymbolsToPage(p, ["S2"], first);
        p = _service.DrawWire(p, first, Pin("S1", "A"), Pin("S2", "B"), [new(60, 40), new(120, 40)]);
        p = _service.MoveSymbolsToPage(p, ["S2"], second);
        var before = JsonSerializer.Serialize(p);
        var result = _service.DeleteWire(p, p.Schematic!.Wires[0].WireId);
        Assert.Empty(result.Schematic!.Wires); Assert.Empty(result.Connections);
        Assert.Equal(2, result.Components.Count); Assert.Equal(2, result.Schematic.Symbols.Count);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void DeleteRejectsCableOwnershipOrAnyLockedSegmentWithoutMutation()
    {
        var p = PlacedProject(); var first = p.Schematic!.Pages[0].PageId;
        p = _service.MoveSymbolsToPage(p, ["S2"], first);
        p = _service.DrawWire(p, first, Pin("S1", "A"), Pin("S2", "B"), [new(60, 40), new(120, 40)]);
        var id = p.Schematic!.Wires[0].WireId;
        var locked = _service.SetLocked(p, id, true);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteWire(locked, id));
        p.Connections[0].CableInstanceId = "EXISTING-CABLE";
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteWire(p, id));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void PairTwoExistingDraftLinesAcrossSheetsWithoutChangingPinIdentityOrWireIds()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(100, 40)]);
        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Free(), Pin("S2", "B"), [new(30, 40), new(120, 40)]);
        var wires = p.Schematic!.Wires.ToArray(); var before = JsonSerializer.Serialize(p);
        var next = _service.ConnectAcrossPages(p, wires[0].WireId, false, wires[1].WireId, true, "TEST SIGNAL");
        var connection = Assert.Single(next.Connections);
        Assert.Equal("A", connection.FromEndpointId); Assert.Equal("B", connection.ToEndpointId);
        Assert.Null(connection.NetId); Assert.Null(connection.CableInstanceId);
        Assert.Equal(wires.Select(w => w.WireId), next.Schematic!.Wires.Select(w => w.WireId));
        Assert.Equal(wires[0].Points, next.Schematic.Wires[0].Points);
        Assert.Equal(wires[1].Points, next.Schematic.Wires[1].Points);
        Assert.Single(next.Schematic.Continuations);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        Assert.Throws<InvalidOperationException>(() => _service.ConnectAcrossPages(next, wires[0].WireId, false, wires[1].WireId, true, "OTHER"));
    }

    [Fact]
    public void CrossSheetPairingRejectsBoundEndSameSheetOrLockedWire()
    {
        var p = PlacedProject(); var page = p.Schematic!.Pages[0].PageId;
        p = _service.DrawWire(p, page, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(100, 40)]);
        p = _service.DrawWire(p, page, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(30, 80), new(120, 80)]);
        var a = p.Schematic!.Wires[0].WireId; var b = p.Schematic.Wires[1].WireId;
        Assert.Throws<InvalidOperationException>(() => _service.ConnectAcrossPages(p, a, false, b, true, "TEST"));
        var second = p.Schematic.Pages[1].PageId;
        p = _service.DrawWire(p, second, SchematicAttachment.Free(), Pin("S2", "B"), [new(30, 40), new(120, 40)]);
        b = p.Schematic!.Wires[2].WireId;
        Assert.Throws<InvalidOperationException>(() => _service.ConnectAcrossPages(p, a, true, b, true, "TEST"));
        p = _service.SetLocked(p, a, true);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.ConnectAcrossPages(p, a, false, b, true, "TEST"));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    private ElectricalProject PlacedProject()
    {
        var p = _service.AddPage(_service.AddPage(Project(), "One"), "Two");
        p = _service.PlaceSymbol(p, new SchematicSymbol
        {
            SymbolId = "S1", ComponentInstanceId = "C1", PageId = p.Schematic!.Pages[0].PageId,
            Position = new(20, 20), Width = 40, Height = 40,
            Anchors = [new() { EndpointId = "A", Position = new(40, 20), Confirmed = true }]
        });
        return _service.PlaceSymbol(p, new SchematicSymbol
        {
            SymbolId = "S2", ComponentInstanceId = "C2", PageId = p.Schematic!.Pages[1].PageId,
            Position = new(120, 20), Width = 40, Height = 40,
            Anchors = [new() { EndpointId = "B", Position = new(0, 20), Confirmed = true }]
        });
    }

    private static SchematicAttachment Pin(string symbol, string endpoint) => SchematicAttachment.Pin(symbol, endpoint);
    private static ElectricalProject Project() => new()
    {
        ProjectId = "NEW",
        Components = [Component("C1", "A"), Component("C2", "B")]
    };
    private static ComponentInstance Component(string id, string pin) => new()
    {
        ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = "TEST",
        Ports = [new() { PortId = $"{id}:PORT", Name = "IO", Pins = [new() { PinId = pin, PinNumber = "1" }] }]
    };
}
