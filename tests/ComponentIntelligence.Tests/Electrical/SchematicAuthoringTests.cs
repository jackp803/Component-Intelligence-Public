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

    [Fact]
    public void CatalogFallbackRowsAreIndependentOnEachSideAndRemainUnconfirmed()
    {
        var component = new ComponentInstance { ComponentInstanceId = "module", ComponentDefinitionId = "catalog", TypeKey = "IO" };
        foreach (var (side, count) in new[] { ("Right", 32), ("Left", 16) })
            component.Ports.Add(new ComponentPort { PortId = side, SourcePortId = "source-" + side, Name = side,
                PhysicalLocation = new PhysicalPortLocation { Side = side },
                Pins = Enumerable.Range(1, count).Select(i => new ComponentPin {
                    PinId = side + i, SourcePinId = "source-" + side + i, PinNumber = i.ToString() }).ToList() });
        var before = JsonSerializer.Serialize(component);
        var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(30, 80));
        Assert.Equal(165, symbol.Height);
        Assert.Equal(new SchematicPoint(0, 5), symbol.Anchors.Single(a => a.EndpointId == "Left1").Position);
        Assert.Equal(new SchematicPoint(50, 5), symbol.Anchors.Single(a => a.EndpointId == "Right1").Position);
        Assert.Equal(48, symbol.Anchors.Count);
        Assert.All(symbol.Anchors, a => { Assert.False(a.Confirmed); Assert.Equal("source-" + a.EndpointId, a.SourcePinId); });
        Assert.Equal(before, JsonSerializer.Serialize(component));
    }

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
        Assert.Equal("0.7", migrated.SchemaVersion);
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
        Assert.Contains("2.", old);
        Assert.Contains("1.", _service.ReferenceFor(reordered.Schematic!, pair.Source.MarkerId));
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
    public void MovingBottomPinRecalculatesOutwardRoutePastItsLabelWithoutChangingIdentity()
    {
        var project = PlacedProject();
        var symbol = project.Schematic!.Symbols.Single(s => s.SymbolId == "S1");
        project.Schematic.Symbols[0] = symbol with { Anchors = [new SchematicAnchor
        { EndpointId = "A", Position = new(25, 30), Direction = "Bottom", Label = "OUTPUT / 2 DC OUTPUT +V" }] };
        project = _service.DrawWire(project, symbol.PageId, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(45, 50), new(45, 60), new(130, 60)]);
        var before = JsonSerializer.Serialize(project);
        var original = project.Schematic!.Wires.Single();
        var moved = _service.TransformSymbol(project, "S1", new(60, 35), 0);
        var wire = Assert.Single(moved.Schematic!.Wires);
        Assert.Equal(original.WireId, wire.WireId);
        Assert.Equal(original.Start, wire.Start);
        Assert.Equal(original.End, wire.End);
        Assert.Equal(new SchematicPoint(85, 65), wire.Points[0]);
        Assert.True(wire.Points[1].Y >= wire.Points[0].Y + 35);
        Assert.All(wire.Points.Zip(wire.Points.Skip(1)), pair =>
            Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ExplicitDraftBranchFollowsMovedParentWithoutInventingElectricalConnection()
    {
        var project = PlacedProject();
        var page = project.Schematic!.Pages[0].PageId;
        project = _service.DrawWire(project, page, Pin("S1", "A"), SchematicAttachment.Free(),
            [new(60, 40), new(100, 40)]);
        var parentId = project.Schematic!.Wires.Single().WireId;
        project = _service.DrawWire(project, page, SchematicAttachment.Junction(parentId),
            SchematicAttachment.Free(), [new(80, 40), new(80, 70)]);
        var branchId = project.Schematic!.Wires[1].WireId;
        Assert.Empty(project.Connections);
        Assert.Empty(project.Nets);
        var moved = _service.TransformSymbol(project, "S1", new(50, 50), 0);
        var parent = moved.Schematic!.Wires.Single(w => w.WireId == parentId);
        var branch = moved.Schematic.Wires.Single(w => w.WireId == branchId);
        Assert.Equal(parentId, branch.Start.WireId);
        Assert.NotEqual(new SchematicPoint(80, 40), branch.Points[0]);
        Assert.True(parent.Points.Zip(parent.Points.Skip(1)).Any(pair =>
            pair.First.X == pair.Second.X && pair.First.X == branch.Points[0].X &&
            branch.Points[0].Y >= Math.Min(pair.First.Y, pair.Second.Y) &&
            branch.Points[0].Y <= Math.Max(pair.First.Y, pair.Second.Y) ||
            pair.First.Y == pair.Second.Y && pair.First.Y == branch.Points[0].Y &&
            branch.Points[0].X >= Math.Min(pair.First.X, pair.Second.X) &&
            branch.Points[0].X <= Math.Max(pair.First.X, pair.Second.X)));
        Assert.Equal(new SchematicPoint(80, 70), branch.Points[^1]);
        Assert.Empty(moved.Connections);
        Assert.Empty(moved.Nets);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteWire(project, parentId));
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
    public void DeleteDraftContinuationDetachesBothWireEndsWithoutChangingEngineering()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.AddContinuation(p, "DRAFT", pages[0].PageId, new(100, 40), pages[1].PageId, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Marker(pair.Source.MarkerId), [new(60, 40), new(100, 40)]);
        var before = JsonSerializer.Serialize(p);
        var result = _service.DeleteContinuation(p, pair.Source.MarkerId);
        Assert.Empty(result.Schematic!.Continuations);
        Assert.Single(result.Schematic.Wires);
        Assert.Equal(SchematicAttachmentKind.Free, result.Schematic.Wires[0].End.Kind);
        Assert.Equal(new SchematicPoint(100, 40), result.Schematic.Wires[0].Points[^1]);
        Assert.Empty(result.Connections);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void DeleteCompletedContinuationRequiresExplicitCircuitRemovalAndPreservesUnrelatedPair()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.AddContinuation(p, "CONNECTED", pages[0].PageId, new(100, 40), pages[1].PageId, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Marker(pair.Source.MarkerId), [new(60, 40), new(100, 40)]);
        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Marker(pair.Destination.MarkerId), Pin("S2", "B"), [new(20, 40), new(120, 40)]);
        p = _service.AddContinuation(p, "UNRELATED", pages[0].PageId, new(200, 60), pages[1].PageId, new(200, 60));
        var original = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteContinuation(p, pair.Source.MarkerId));
        Assert.Equal(original, JsonSerializer.Serialize(p));
        var result = _service.DeleteContinuation(p, pair.Source.MarkerId, deleteCompletedCircuit: true);
        Assert.Empty(result.Connections);
        Assert.Empty(result.Schematic!.Wires);
        Assert.Single(result.Schematic.Continuations);
        Assert.Equal("UNRELATED", result.Schematic.Continuations[0].Signal);
        Assert.Equal(2, result.Components.Count);
        Assert.Equal(original, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void DeleteContinuationRejectsLockedRoutesAndOwnedCableWithoutMutation()
    {
        var p = PlacedProject(); var pages = p.Schematic!.Pages;
        p = _service.AddContinuation(p, "SIGNAL", pages[0].PageId, new(100, 40), pages[1].PageId, new(20, 40));
        var pair = p.Schematic!.Continuations.Single();
        p = _service.DrawWire(p, pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Marker(pair.Source.MarkerId), [new(60, 40), new(100, 40)]);
        var locked = _service.SetLocked(p, p.Schematic.Wires[0].WireId, true);
        var lockedJson = JsonSerializer.Serialize(locked);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteContinuation(locked, pair.Source.MarkerId));
        Assert.Equal(lockedJson, JsonSerializer.Serialize(locked));

        p = _service.DrawWire(p, pages[1].PageId, SchematicAttachment.Marker(pair.Destination.MarkerId), Pin("S2", "B"), [new(20, 40), new(120, 40)]);
        p.Connections.Single().CableInstanceId = "OWNED";
        var original = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteContinuation(p, pair.Source.MarkerId, deleteCompletedCircuit: true));
        Assert.Equal(original, JsonSerializer.Serialize(p));
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

    [Fact]
    public void SeparateRepresentationsSharePhysicalIdentityAndReferenceWithoutInferringConnections()
    {
        var p = PlacedProject();
        p.Components[0].Ports[0].Pins.Add(new() { PinId = "A2", PinNumber = "2" });
        var before = JsonSerializer.Serialize(p);
        var next = _service.PlaceSymbol(p, new SchematicSymbol
        {
            SymbolId = "S1-other", ComponentInstanceId = "C1",
            PageId = p.Schematic!.Pages[1].PageId, Position = new(30, 90),
            Width = 40, Height = 30,
            Anchors = [new() { EndpointId = "A2", Position = new(0, 15), Confirmed = true }]
        });
        next = _service.SetSymbolDetails(next, "S1", "K1", next.Schematic!.Symbols[0].Anchors);
        var restored = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        var representations = restored.Schematic!.Symbols.Where(s => s.ComponentInstanceId == "C1").ToArray();
        Assert.Equal(2, representations.Length);
        Assert.Equal(2, restored.Components.Count);
        Assert.All(representations, s => Assert.Equal("K1",
            restored.Components.Single(c => c.ComponentInstanceId == s.ComponentInstanceId).ReferenceDesignator));
        Assert.Equal(new[] { "A", "A2" }, representations.SelectMany(s => s.Anchors).Select(a => a.EndpointId));
        Assert.Empty(restored.Connections);
        Assert.Empty(restored.Nets);
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void DeleteRepresentationPreservesPhysicalInstanceAndOtherRepresentations()
    {
        var p = PlacedProject();
        p = _service.PlaceSymbol(p, p.Schematic!.Symbols[0] with { SymbolId = "S1-copy", PageId = p.Schematic.Pages[1].PageId });
        var before = JsonSerializer.Serialize(p);
        var next = _service.DeleteRepresentation(p, "S1");
        Assert.Equal(p.Components.Select(c => c.ComponentInstanceId), next.Components.Select(c => c.ComponentInstanceId));
        Assert.DoesNotContain(next.Schematic!.Symbols, s => s.SymbolId == "S1");
        Assert.Contains(next.Schematic.Symbols, s => s.SymbolId == "S1-copy");
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void DeleteRepresentationRejectsAttachedDraftWireOrLockWithoutMutation()
    {
        var p = PlacedProject();
        var locked = _service.SetLocked(p, "S1", true);
        var lockedBefore = JsonSerializer.Serialize(locked);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteRepresentation(locked, "S1"));
        Assert.Equal(lockedBefore, JsonSerializer.Serialize(locked));
        p = _service.DrawWire(p, p.Schematic!.Pages[0].PageId, Pin("S1", "A"), SchematicAttachment.Free(), [new(60, 40), new(90, 40)]);
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => _service.DeleteRepresentation(p, "S1"));
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
