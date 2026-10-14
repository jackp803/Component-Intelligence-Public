using System.Text.Json;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicLifecycleTests
{
    private readonly SchematicAuthoringService _service = new();

    [Fact]
    public void BatchWireDeletionRemovesWholeCircuitBranchesAndPairedMarkersPreservingInventory()
    {
        var project = Fixture();
        AddBranches(project);
        var before = Snapshot(project);
        var cables = Snapshot(project.Cables);
        var keep = Snapshot(project.Schematic!.Wires.Single(w => w.WireId == "KEEP"));

        var next = DeleteWires(project, ["WA", "WB", "BRANCH"]);

        Assert.Equal("KEEP", Assert.Single(next.Schematic!.Wires).WireId);
        Assert.Equal(keep, Snapshot(next.Schematic.Wires[0]));
        Assert.Empty(next.Schematic.Continuations);
        Assert.Equal("CD", Assert.Single(next.Connections).ConnectionId);
        Assert.Equal("CD", Assert.Single(next.TopologyRoutes).ConnectionId);
        Assert.Equal("CB", Assert.Single(next.CableRoutes).ConnectionOrCableId);
        Assert.Equal(cables, Snapshot(next.Cables));
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, Pins(next).Select(p => p.PinId));
        Assert.Null(Pins(next).Single(p => p.PinId == "B").InferredFunction);
        Assert.Equal("RETURN", Pins(next).Single(p => p.PinId == "D").DisplayFunction);
        Assert.Equal("START", Pins(next).Single(p => p.PinId == "A").Function);
        Assert.Equal(before, Snapshot(project));
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void DeletingOnlyBranchPreservesItsParentCircuitAndSiblingRoutes()
    {
        var project = Fixture();
        AddBranches(project);
        var before = Snapshot(project);

        var next = DeleteWires(project, ["BRANCH"]);

        Assert.Equal(new[] { "WA", "WB", "KEEP" }, next.Schematic!.Wires.Select(w => w.WireId));
        Assert.Equal(Snapshot(project.Connections), Snapshot(next.Connections));
        Assert.Equal(Snapshot(project.Schematic!.Continuations), Snapshot(next.Schematic.Continuations));
        Assert.Equal(before, Snapshot(project));
    }

    [Fact]
    public void DeletingPairedDraftCollectsRemoteDraftAndDependentBranches()
    {
        var project = Fixture();
        project.Connections.RemoveAll(c => c.ConnectionId == "AB");
        for (var i = 0; i < 2; i++) project.Schematic!.Wires[i] = project.Schematic.Wires[i] with { ConnectionId = null };
        AddBranches(project);

        var next = DeleteWires(project, ["WA"]);

        Assert.Equal("KEEP", Assert.Single(next.Schematic!.Wires).WireId);
        Assert.Empty(next.Schematic.Continuations);
        Assert.Equal("CD", Assert.Single(next.Connections).ConnectionId);
    }

    [Theory]
    [InlineData("WA")]
    [InlineData("WB")]
    [InlineData("BRANCH")]
    [InlineData("CHILD")]
    public void LockedCircuitOrDependentRouteRejectsWholeWireBatch(string lockedId)
    {
        var project = Fixture();
        AddBranches(project);
        LockWire(project, lockedId);
        RejectWithoutMutation(project, () => DeleteWires(project, ["KEEP", "WA"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownOrEmptyWireBatchRejectsWithoutMutation(bool empty)
    {
        var project = Fixture();
        RejectWithoutMutation(project, () => DeleteWires(project, empty ? [] : ["WA", "missing"]));
    }

    [Fact]
    public void RepeatedWireSelectionDeletesTheCircuitOnce()
    {
        var next = DeleteWires(Fixture(), ["WA", "WA", "WB"]);
        Assert.Equal("KEEP", Assert.Single(next.Schematic!.Wires).WireId);
        Assert.Equal("CD", Assert.Single(next.Connections).ConnectionId);
    }

    [Fact]
    public void RepresentationRemovalRequiresExplicitConnectedCircuitDeletion()
    {
        var project = Fixture();
        RejectWithoutMutation(project, () => DeleteRepresentations(project, ["SE", "SA"], false));
        var next = DeleteRepresentations(project, ["SE", "SA"], true);
        Assert.Equal(new[] { "SB", "SC", "SD" }, next.Schematic!.Symbols.Select(s => s.SymbolId));
        Assert.Equal("KEEP", Assert.Single(next.Schematic.Wires).WireId);
        Assert.Empty(next.Schematic.Continuations);
        Assert.Equal(5, next.Components.Count);
        Assert.Equal(Snapshot(project.Cables), Snapshot(next.Cables));
    }

    [Fact]
    public void RepresentationRemovalPreservesOtherViewsAndTheirExactPinConnections()
    {
        var project = Fixture();
        project.Schematic!.Symbols.Add(project.Schematic.Symbols[0] with { SymbolId = "SA-copy", PageId = "P3" });
        var before = Snapshot(project);

        var next = DeleteRepresentations(project, ["SA-copy", "SE"], false);

        Assert.Equal(Snapshot(project.Connections), Snapshot(next.Connections));
        Assert.Equal(Snapshot(project.Schematic.Wires), Snapshot(next.Schematic!.Wires));
        Assert.Contains(next.Schematic.Symbols, s => s.SymbolId == "SA");
        Assert.Equal(5, next.Components.Count);
        Assert.Equal(before, Snapshot(project));
    }

    [Theory]
    [InlineData("SA")]
    [InlineData("WB")]
    [InlineData("CHILD")]
    public void LockedRepresentationOrAffectedRouteRejectsRepresentationBatch(string lockedId)
    {
        var project = Fixture();
        AddBranches(project);
        if (lockedId == "SA") project.Schematic!.Symbols[0] = project.Schematic.Symbols[0] with { Locked = true };
        else LockWire(project, lockedId);
        RejectWithoutMutation(project, () => DeleteRepresentations(project, ["SE", "SA"], true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownOrEmptyRepresentationBatchRejectsWithoutMutation(bool empty)
    {
        var project = Fixture();
        RejectWithoutMutation(project, () => DeleteRepresentations(project, empty ? [] : ["SE", "missing"], true));
    }

    [Fact]
    public void EmptyPageCanBeDeletedButLastPageCannot()
    {
        var project = Fixture();
        var before = Snapshot(project);
        var next = DeletePage(project, "P3");
        Assert.Equal(new[] { "P1", "P2" }, next.Schematic!.Pages.Select(p => p.PageId));
        Assert.Equal(before, Snapshot(project));
        var single = new ElectricalProject { ProjectId = "single", Schematic = new()
        { Pages = [new() { PageId = "only", Title = "Only" }] } };
        RejectWithoutMutation(single, () => DeletePage(single, "only", deleteContents: true));
    }

    [Fact]
    public void PopulatedPageRequiresOneExplicitDispositionAndValidDestination()
    {
        var project = Fixture();
        RejectWithoutMutation(project, () => DeletePage(project, "P1"));
        RejectWithoutMutation(project, () => DeletePage(project, "P1", "P1"));
        RejectWithoutMutation(project, () => DeletePage(project, "P1", "missing"));
        RejectWithoutMutation(project, () => DeletePage(project, "missing"));
        RejectWithoutMutation(project, () => DeletePage(project, "P1", "P3", true));
    }

    [Fact]
    public void PageContentsDeletionCleansRemoteCircuitBranchesAndKeepsPhysicalInventory()
    {
        var project = Fixture();
        AddBranches(project);
        var before = Snapshot(project);

        var next = DeletePage(project, "P1", deleteContents: true);

        Assert.Equal(new[] { "P2", "P3" }, next.Schematic!.Pages.Select(p => p.PageId));
        Assert.Equal(new[] { "SB", "SC", "SD" }, next.Schematic.Symbols.Select(s => s.SymbolId));
        Assert.Equal("KEEP", Assert.Single(next.Schematic.Wires).WireId);
        Assert.Empty(next.Schematic.Continuations);
        Assert.Equal("CD", Assert.Single(next.Connections).ConnectionId);
        Assert.Equal(Snapshot(project.Cables), Snapshot(next.Cables));
        Assert.Equal(5, next.Components.Count);
        Assert.Equal(before, Snapshot(project));
    }

    [Fact]
    public void PageMoveKeepsCoordinatesRoutesConnectionsAndRemoteMarkers()
    {
        var project = Fixture();
        var before = Snapshot(project);
        var next = DeletePage(project, "P1", "P3");

        Assert.Equal(new[] { "P2", "P3" }, next.Schematic!.Pages.Select(p => p.PageId));
        foreach (var symbol in project.Schematic!.Symbols)
            Assert.Equal(Snapshot(symbol with { PageId = symbol.PageId == "P1" ? "P3" : symbol.PageId }),
                Snapshot(next.Schematic.Symbols.Single(s => s.SymbolId == symbol.SymbolId)));
        foreach (var wire in project.Schematic.Wires)
            Assert.Equal(Snapshot(wire with { PageId = wire.PageId == "P1" ? "P3" : wire.PageId }),
                Snapshot(next.Schematic.Wires.Single(w => w.WireId == wire.WireId)));
        var pair = Assert.Single(next.Schematic.Continuations);
        Assert.Equal(new SchematicPoint(100, 40), pair.Source.Position);
        Assert.Equal("P3", pair.Source.PageId);
        Assert.Equal("P2", pair.Destination.PageId);
        Assert.Equal(Snapshot(project.Connections), Snapshot(next.Connections));
        Assert.Equal(before, Snapshot(project));
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void MovingOntoPairedSheetMergesPathsPreservingElectricalEndpointsAndBranchCoordinates()
    {
        var project = Fixture();
        AddBranches(project);
        var before = Snapshot(project);
        var next = DeletePage(project, "P1", "P2");

        var merged = next.Schematic!.Wires.Single(w => w.ConnectionId == "AB");
        Assert.Equal(SchematicAttachment.Pin("SA", "A"), merged.Start);
        Assert.Equal(SchematicAttachment.Pin("SB", "B"), merged.End);
        Assert.Equal(new[] { new SchematicPoint(60, 40), new(100, 40), new(20, 40), new(120, 40) }, merged.Points);
        Assert.Empty(next.Schematic.Continuations);
        var branch = next.Schematic.Wires.Single(w => w.WireId == "BRANCH");
        Assert.Equal(SchematicAttachment.Junction(merged.WireId), branch.Start);
        Assert.Equal(new[] { new SchematicPoint(80, 40), new(80, 70) }, branch.Points);
        Assert.Equal(Snapshot(project.Connections), Snapshot(next.Connections));
        Assert.Equal(Snapshot(project.Schematic!.Wires.Single(w => w.WireId == "KEEP")),
            Snapshot(next.Schematic.Wires.Single(w => w.WireId == "KEEP")));
        Assert.Equal(before, Snapshot(project));
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void MovingPartialContinuationOntoItsOtherSheetKeepsDraftRouteAndRemovesPair()
    {
        var project = Fixture();
        project.Connections.RemoveAll(c => c.ConnectionId == "AB");
        project.Schematic!.Wires.RemoveAll(w => w.WireId == "WB");
        project.Schematic.Wires[0] = project.Schematic.Wires[0] with { ConnectionId = null };
        var points = Snapshot(project.Schematic.Wires[0].Points);

        var next = DeletePage(project, "P1", "P2");

        var draft = next.Schematic!.Wires.Single(w => w.WireId == "WA");
        Assert.Equal(points, Snapshot(draft.Points));
        Assert.Equal(SchematicAttachment.Free(), draft.End);
        Assert.Null(draft.ConnectionId);
        Assert.Empty(next.Schematic.Continuations);
    }

    [Theory]
    [InlineData(false, "SA")]
    [InlineData(false, "WA")]
    [InlineData(false, "WB")]
    [InlineData(false, "BRANCH")]
    [InlineData(true, "SA")]
    [InlineData(true, "WA")]
    [InlineData(true, "WB")]
    [InlineData(true, "BRANCH")]
    public void LockedLocalRemoteOrBranchObjectRejectsPageDeletionAtomically(bool deleteContents, string lockedId)
    {
        var project = Fixture();
        AddBranches(project);
        if (lockedId == "SA") project.Schematic!.Symbols[0] = project.Schematic.Symbols[0] with { Locked = true };
        else LockWire(project, lockedId);
        RejectWithoutMutation(project, () => DeletePage(project, "P1", deleteContents ? null : "P2", deleteContents));
    }

    [Fact]
    public void MarkerOnlyPageDeletionRemovesBothMarkersAndAttachedRemoteDraft()
    {
        var project = Fixture();
        project.Schematic!.Continuations.Add(new() { ContinuationId = "draft-pair", Signal = "DRAFT",
            Source = new() { MarkerId = "draft-source", PageId = "P3", Position = new(20, 60) },
            Destination = new() { MarkerId = "draft-destination", PageId = "P2", Position = new(20, 60) } });
        project.Schematic.Wires.Add(new() { WireId = "REMOTE-DRAFT", PageId = "P2",
            Start = SchematicAttachment.Marker("draft-destination"), Points = [new(20, 60), new(40, 60)] });
        RejectWithoutMutation(project, () => DeletePage(project, "P3"));

        var next = DeletePage(project, "P3", deleteContents: true);

        Assert.Equal(new[] { "WA", "WB", "KEEP" }, next.Schematic!.Wires.Select(w => w.WireId));
        Assert.Equal("PAIR", Assert.Single(next.Schematic.Continuations).ContinuationId);
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void CableDetailIsPageContentAndMovesWithItsSavedPlacement()
    {
        var project = Fixture();
        project.Schematic!.Pages[2] = project.Schematic.Pages[2] with { CableDetail = new("CB", null)
        { DetailId = "detail", TablePosition = new(30, 50), SketchPosition = new(30, 150) } };
        RejectWithoutMutation(project, () => DeletePage(project, "P3"));

        var next = DeletePage(project, "P3", "P2");

        var detail = Assert.Single(next.Schematic!.Pages.Single(p => p.PageId == "P2").InlineCableDetails);
        Assert.Equal(project.Schematic.Pages[2].CableDetail, detail);
        Assert.Equal(Snapshot(project.Cables), Snapshot(next.Cables));
    }

    [Fact]
    public void MergingPagesKeepsManualSpecificationFromTheRemoteSegment()
    {
        var project = Fixture();
        project.Schematic!.Wires[1] = project.Schematic.Wires[1] with
        { ManualSpecification = true, Awg = 20, ManualDesignation = true, Designation = "MANUAL-AB" };

        var next = DeletePage(project, "P1", "P2");

        var merged = next.Schematic!.Wires.Single(w => w.ConnectionId == "AB");
        Assert.True(merged.ManualSpecification);
        Assert.Equal(20, merged.Awg);
        Assert.True(merged.ManualDesignation);
        Assert.Equal("MANUAL-AB", merged.Designation);
        Assert.Equal(1.5, next.Connections.Single(c => c.ConnectionId == "AB").ConductorAreaMm2);
    }

    [Fact]
    public void CableDetailTransferRejectsAmbiguousExistingDetailIdentity()
    {
        var project = Fixture();
        project.Schematic!.Pages[2] = project.Schematic.Pages[2] with { CableDetail = new("CB", null) { DetailId = "same" } };
        project.Schematic.Pages[1] = project.Schematic.Pages[1] with { CableDetail = new("CB", null) { DetailId = "same" } };
        RejectWithoutMutation(project, () => DeletePage(project, "P3", "P2"));
    }

    private ElectricalProject DeleteWires(ElectricalProject p, IReadOnlyCollection<string> ids) =>
        _service.DeleteWires(p, ids);
    private ElectricalProject DeleteRepresentations(ElectricalProject p, IReadOnlyCollection<string> ids, bool deleteConnections) =>
        _service.DeleteRepresentations(p, ids, deleteConnections);
    private ElectricalProject DeletePage(ElectricalProject p, string id, string? destination = null, bool deleteContents = false) =>
        _service.DeletePage(p, id, destination, deleteContents);

    private static void RejectWithoutMutation(ElectricalProject project, Action action)
    {
        var before = Snapshot(project);
        Assert.Throws<InvalidOperationException>(action);
        Assert.Equal(before, Snapshot(project));
    }

    private static string Snapshot<T>(T value) => JsonSerializer.Serialize(value);
    private static IEnumerable<ComponentPin> Pins(ElectricalProject project) => project.Components.SelectMany(c => c.Ports).SelectMany(p => p.Pins);

    private static void LockWire(ElectricalProject project, string id)
    {
        var index = project.Schematic!.Wires.FindIndex(w => w.WireId == id);
        project.Schematic.Wires[index] = project.Schematic.Wires[index] with { Locked = true };
    }

    private static void AddBranches(ElectricalProject project)
    {
        project.Schematic!.Wires.Add(new() { WireId = "BRANCH", PageId = "P2",
            Start = SchematicAttachment.Junction("WB"), Points = [new(80, 40), new(80, 70)] });
        project.Schematic.Wires.Add(new() { WireId = "CHILD", PageId = "P2",
            End = SchematicAttachment.Junction("BRANCH"), Points = [new(110, 60), new(80, 60)] });
        SchematicAuthoringService.Validate(project);
    }

    private static ElectricalProject Fixture()
    {
        var project = new ElectricalProject { ProjectId = "lifecycle", Components =
        [Component("A", "START"), Component("B"), Component("C", "RETURN"), Component("D"), Component("E")] };
        project.Cables.Add(new() { CableInstanceId = "CB", CableDefinitionId = "synthetic-cable", ProvidedLengthMm = 1500,
            CoreAssignments = [new() { CoreId = "core", FromEndpointId = "A", ToEndpointId = "B", Status = "USED" }] });
        project.Connections.AddRange([new() { ConnectionId = "AB", FromEndpointId = "A", ToEndpointId = "B",
            Kind = ConnectionKind.Cable, CableInstanceId = "CB", CableCoreId = "core", ConductorAreaMm2 = 1.5 },
            new() { ConnectionId = "CD", FromEndpointId = "C", ToEndpointId = "D" }]);
        project.TopologyRoutes.AddRange([new() { ConnectionId = "AB" }, new() { ConnectionId = "CD" }]);
        project.CableRoutes.AddRange([new() { CableRouteId = "conductor-route", ConnectionOrCableId = "AB" },
            new() { CableRouteId = "physical-route", ConnectionOrCableId = "CB" }]);
        project.Schematic = new()
        {
            Pages = [new() { PageId = "P1", Title = "Source" }, new() { PageId = "P2", Title = "Receiver" },
                new() { PageId = "P3", Title = "Spare" }],
            Symbols = [Symbol("A", "P1", new(20, 20), new(40, 20)), Symbol("B", "P2", new(120, 20), new(0, 20)),
                Symbol("C", "P2", new(20, 100), new(40, 20)), Symbol("D", "P2", new(120, 100), new(0, 20)),
                Symbol("E", "P1", new(20, 170), new(40, 20))],
            Continuations = [new() { ContinuationId = "PAIR", Signal = "START",
                Source = new() { MarkerId = "MA", PageId = "P1", Position = new(100, 40) },
                Destination = new() { MarkerId = "MB", PageId = "P2", Position = new(20, 40) } }],
            Wires = [new() { WireId = "WA", PageId = "P1", ConnectionId = "AB", ManualRoute = true,
                Start = SchematicAttachment.Pin("SA", "A"), End = SchematicAttachment.Marker("MA"),
                Points = [new(60, 40), new(100, 40)] },
                new() { WireId = "WB", PageId = "P2", ConnectionId = "AB", ManualRoute = true,
                    Start = SchematicAttachment.Marker("MB"), End = SchematicAttachment.Pin("SB", "B"),
                    Points = [new(20, 40), new(120, 40)] },
                new() { WireId = "KEEP", PageId = "P2", ConnectionId = "CD", ManualRoute = true,
                    Start = SchematicAttachment.Pin("SC", "C"), End = SchematicAttachment.Pin("SD", "D"),
                    Points = [new(60, 120), new(120, 120)] }]
        };
        SchematicAuthoringService.Validate(project);
        ProjectPinFunctionService.Synchronize(project);
        SchematicWireIdentification.Reconcile(project);
        return project;
    }

    private static ComponentInstance Component(string pinId, string? function = null) => new()
    {
        ComponentInstanceId = "C" + pinId, ComponentDefinitionId = "synthetic", TypeKey = "TEST",
        Ports = [new() { PortId = "PORT-" + pinId, Name = "IO",
            Pins = [new() { PinId = pinId, PinNumber = "1", SourcePinId = "source-" + pinId, Function = function }] }]
    };

    private static SchematicSymbol Symbol(string pinId, string page, SchematicPoint position, SchematicPoint anchor) => new()
    {
        SymbolId = "S" + pinId, ComponentInstanceId = "C" + pinId, PageId = page, Position = position,
        Width = 40, Height = 40, Anchors = [new() { EndpointId = pinId, Position = anchor, Confirmed = true }]
    };
}
