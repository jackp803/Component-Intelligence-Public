using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCableIsolationTests
{
    [Fact]
    public void EveryCablePlacementEntryCreatesAnIndependentPhysicalCable()
    {
        var project = ArchivedCableManufacturingDetailTests.Project(); var author = new SchematicAuthoringService();
        var symbol = project.Schematic!.Symbols[0]; var cable = project.Cables[0];
        project = author.SetArchivedCableDetails(project, cable.CableInstanceId, "C1", 500, "spec", CableConstructionType.Custom, cable.ArchivedCable!.Mapping, "first note");
        project = author.PlaceExistingRepresentation(project, symbol.SymbolId, "sheet", new(120, 20));
        project = author.PlaceCableRepresentation(project, cable.CableInstanceId, symbol.Geometry!, new Dictionary<string, string>(), "sheet", new(220, 20));
        Assert.Equal(3, project.Cables.Count);
        Assert.Equal(3, project.Schematic!.Symbols.Select(s => s.CableInstanceId).Distinct().Count());
        Assert.All(project.Cables.Skip(1), c => { Assert.Null(c.ReferenceDesignator); Assert.Null(c.ProvidedLengthMm); Assert.Null(c.Notes); });
    }

    [Fact]
    public void LegacyViewsBecomeIndependentWithoutMovingOrDisconnectingExternalWires()
    {
        var project = Legacy(); var before = JsonSerializer.Serialize(project); var oldDoc = project.Schematic!;
        var next = new SchematicCableIsolationService().Separate(project);
        Assert.Equal(2, next.Cables.Count);
        Assert.Equal(2, next.Schematic!.Symbols.Where(s => s.CableInstanceId is not null).Select(s => s.CableInstanceId).Distinct().Count());
        Assert.Equal(oldDoc.Wires.Select(w => w.WireId), next.Schematic.Wires.Select(w => w.WireId));
        Assert.Equal(oldDoc.Wires.Select(w => JsonSerializer.Serialize(w.Points)), next.Schematic.Wires.Select(w => JsonSerializer.Serialize(w.Points)));
        Assert.Equal(oldDoc.Wires.Select(w => w.Locked), next.Schematic.Wires.Select(w => w.Locked));
        Assert.Equal(oldDoc.Symbols.Select(s => s.SymbolId), next.Schematic.Symbols.Select(s => s.SymbolId));
        foreach (var wire in next.Schematic.Wires)
        {
            var connection = next.Connections.Single(c => c.ConnectionId == wire.ConnectionId);
            Assert.Equal(wire.Start.EndpointId, connection.FromEndpointId);
            Assert.Equal(wire.End.EndpointId, connection.ToEndpointId);
            Assert.Equal(SchematicAuthoringService.AnchorPoint(next.Schematic.Symbols.Single(s => s.SymbolId == wire.Start.SymbolId), wire.Start.EndpointId!), wire.Points[0]);
        }
        Assert.NotEqual(next.Connections[0].FromEndpointId, next.Connections[1].FromEndpointId);
        Assert.Equal(project.Connections.Select(c => c.ConnectionId), next.Connections.Select(c => c.ConnectionId));
        Assert.Equal(project.Connections.Select(c => c.ToEndpointId), next.Connections.Select(c => c.ToEndpointId));
        Assert.All(next.Cables, c => { Assert.Equal(500, c.ProvidedLengthMm); Assert.Equal("Existing notes", c.Notes); });
        var copy = next.Cables[1];
        next = new SchematicAuthoringService().SetArchivedCableDetails(next, copy.CableInstanceId, "C2", 1200, "new spec", CableConstructionType.Custom, copy.ArchivedCable!.Mapping, "Lower sensor");
        Assert.Equal("C1", next.Cables[0].ReferenceDesignator); Assert.Equal(500, next.Cables[0].ProvidedLengthMm); Assert.Equal("Existing notes", next.Cables[0].Notes);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        SchematicAuthoringService.Validate(next);
        Assert.Same(next, new SchematicCableIsolationService().Separate(next));
    }

    [Fact]
    public void CrossPageMatingPinMapAndInferenceFollowTheCorrectLegacyView()
    {
        var (project, a, b) = ConnectorMatingTests.Drawing();
        var alias = b with { SymbolId = "legacy-alias", Position = new(20, 100) };
        project.Schematic!.Symbols.Add(alias);
        var source = SchematicSymbolOwner.Resolve(project, a).Ports[1].Pins[0]; source.Function = "COM";
        var from = ConnectorMatingTests.Port(project, a); var to = ConnectorMatingTests.Port(project, alias);
        project = new SchematicAuthoringService().MatePorts(project, SchematicAttachment.Port(a.SymbolId, from.PortId),
            SchematicAttachment.Port(alias.SymbolId, to.PortId), ConnectorMatingService.Prepare(project, from.PortId, to.PortId), "verified fixture", "J2 接頭對接");
        var points = JsonSerializer.Serialize(project.Schematic!.Wires.Select(w => w.Points));
        var next = new SchematicCableIsolationService().Separate(project);
        var target = SchematicSymbolOwner.Resolve(next, next.Schematic!.Symbols.Single(s => s.SymbolId == alias.SymbolId)).Ports[0];
        var original = SchematicSymbolOwner.Resolve(next, next.Schematic.Symbols.Single(s => s.SymbolId == b.SymbolId)).Ports[0];
        var connection = Assert.Single(next.Connections);
        Assert.Equal(target.PortId, connection.ToEndpointId); Assert.NotEqual(original.PortId, connection.ToEndpointId);
        Assert.All(connection.MatingMap!.Pairs, p => Assert.Contains(target.Pins, pin => pin.PinId == p.ToPinId));
        Assert.Equal("COM", target.Pins[0].DisplayFunction); Assert.Null(original.Pins[0].DisplayFunction);
        Assert.True(connection.MatingMap.Confirmed); Assert.Equal("verified fixture", connection.MatingMap.Evidence);
        Assert.Equal(points, JsonSerializer.Serialize(next.Schematic.Wires.Select(w => w.Points)));
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void LegacyInlineDetailFollowsTheNearestPlacedCableAndSurvivesReopenUndo()
    {
        var project = Legacy(); var details = new SchematicCableDetailService();
        project = details.PlaceArchivedDetail(project, project.Cables[0].CableInstanceId, "sheet", new(90, 120), new(90, 200),
            new(project.Cables[0].CableInstanceId, null) { SketchVisible = false });
        var session = new ProjectSaveSession(); session.Begin(project, true);
        var history = new ProjectMutationHistory(); history.RecordBeforeMutation(project, "Separate cables");
        var next = new SchematicCableIsolationService().Separate(project);
        Assert.True(session.IsDirty(next));
        Assert.Equal(next.Schematic!.Symbols.Single(s => s.SymbolId == "alias").CableInstanceId, next.Schematic.Pages[0].InlineCableDetails[0].CableInstanceId);
        var reopened = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(next))!;
        Assert.Equal(JsonSerializer.Serialize(next), JsonSerializer.Serialize(reopened));
        Assert.True(history.TryUndo(next, out var undo, out _)); Assert.False(session.IsDirty(undo));
        Assert.True(history.TryRedo(undo, out var redo, out _)); Assert.Equal(JsonSerializer.Serialize(next), JsonSerializer.Serialize(redo));
    }

    [Fact]
    public void AmbiguousUndrawnConnectionIsRejectedWithoutLosingTheOriginalProject()
    {
        var project = Legacy(); project.Schematic!.Wires.Clear();
        var before = JsonSerializer.Serialize(project);
        var error = Assert.Throws<InvalidOperationException>(() => new SchematicCableIsolationService().Separate(project));
        Assert.Contains("歸屬", error.Message); Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void SharedCarrierOwnershipIsRejectedEvenWithoutCablePinEndpoints()
    {
        var project = Legacy(); project.Connections.Clear(); project.Schematic!.Wires.Clear();
        project.Connections.Add(new() { ConnectionId = "carrier", FromEndpointId = "sensor11", ToEndpointId = "sensor21",
            Kind = ConnectionKind.Cable, CableInstanceId = project.Cables[0].CableInstanceId });
        SchematicAuthoringService.Validate(project);
        var before = JsonSerializer.Serialize(project);
        var error = Assert.Throws<InvalidOperationException>(() => new SchematicCableIsolationService().Separate(project));
        Assert.Contains("實體線材歸屬", error.Message); Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing-symbol")]
    public void UnresolvedStandaloneDetailsAreRejectedAtomically(string? owner)
    {
        var project = Legacy();
        project.Schematic!.Pages.Add(new() { PageId = "details", Title = "Details", CableDetail =
            new(project.Cables[0].CableInstanceId, null) { SourceSymbolId = owner, SketchVisible = false } });
        var before = JsonSerializer.Serialize(project);
        var error = Assert.Throws<InvalidOperationException>(() => new SchematicCableIsolationService().Separate(project));
        Assert.Contains("明細", error.Message); Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(270)]
    public void LegacyDetailOwnershipUsesTheVisiblePartAndRotatedSymbolBounds(int rotation)
    {
        var project = Legacy(); project.Connections.Clear(); project.Schematic!.Wires.Clear();
        var original = project.Schematic.Symbols[0];
        if (rotation != 0)
        {
            project.Schematic.Symbols[0] = original with { Rotation = rotation };
            var aliasIndex = project.Schematic.Symbols.FindIndex(s => s.SymbolId == "alias");
            project.Schematic.Symbols[aliasIndex] = project.Schematic.Symbols[aliasIndex] with { Position = new(20, 100) };
        }
        var binding = new SchematicCableDetailBinding(project.Cables[0].CableInstanceId, null) {
            DetailId = "legacy-visible-detail",
            TablePosition = rotation == 0 ? new(20, 20) : new(20, 85),
            SketchPosition = rotation == 0 ? new(20, 100) : new(200, 200), TableVisible = rotation != 0, SketchVisible = rotation == 0 };
        project.Schematic.Pages[0] = project.Schematic.Pages[0] with { InlineCableDetails = [binding] };
        var next = new SchematicCableIsolationService().Separate(project);
        var expected = next.Schematic!.Symbols.Single(s => s.SymbolId == (rotation == 0 ? "alias" : original.SymbolId));
        Assert.Equal(expected.CableInstanceId, next.Schematic.Pages[0].InlineCableDetails[0].CableInstanceId);
    }

    internal static ElectricalProject Legacy()
    {
        var project = ArchivedCableManufacturingDetailTests.Project(); var author = new SchematicAuthoringService();
        var source = project.Schematic!.Symbols[0];
        project.Schematic.Symbols[0] = source with { Geometry = source.Geometry! with { ConnectionPoints = [new("B1", "", new(60, 5), Direction: "Right"), new("B2", "", new(60, 15), Direction: "Right")] } };
        project = author.SetCableContactBindings(project, source.SymbolId, new Dictionary<string, string> { ["p2.1"] = "B1", ["p2.2"] = "B2" });
        project = author.SetArchivedCableDetails(project, project.Cables[0].CableInstanceId, "C1", 500, "spec", CableConstructionType.Custom,
            [new("p1.1", "p2.1")], "Existing notes");
        source = project.Schematic!.Symbols[0]; project.Schematic.Symbols.Add(source with { SymbolId = "alias", Position = new(20, 100), Locked = true });
        foreach (var (symbolId, id, y, pinIndex) in new[] { (source.SymbolId, "sensor1", 20d, 0), ("alias", "sensor2", 100d, 1) })
        {
            project.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = "sensor",
                Ports = [new() { PortId = id + "P", Name = "P", Pins = [new() { PinId = id + "1", PinNumber = "1" }] }] });
            project.Schematic!.Symbols.Add(new() { SymbolId = id, ComponentInstanceId = id, PageId = "sheet", Position = new(230, y),
                Anchors = [new() { EndpointId = id + "1", Position = new(0, 5 + pinIndex * 10), Direction = "Left" }] });
            project = author.DrawWire(project, "sheet", SchematicAttachment.Pin(symbolId, source.Anchors[pinIndex].EndpointId),
                SchematicAttachment.Pin(id, id + "1"), [new(80, y + 5 + pinIndex * 10), new(230, y + 5 + pinIndex * 10)]);
        }
        project.Schematic!.Wires[1] = project.Schematic.Wires[1] with { Locked = true };
        return project;
    }
}
