using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicPortFanoutTests
{
    private readonly SchematicAuthoringService _service = new();

    [Fact]
    public void FanoutChangesOnlyDisplayPositionsAndCreatesNoConductiveEdges()
    {
        var project = Fixture(cad: true);
        var before = Snapshot(project);
        var original = project.Schematic!.Symbols[0];
        var next = Fanout(project, true);
        var symbol = next.Schematic!.Symbols[0];

        Assert.Equal("0.12", next.SchemaVersion);
        Assert.Equal(Snapshot(original.Anchors), Snapshot(symbol.Anchors));
        Assert.Equal(Snapshot(original.Geometry), Snapshot(symbol.Geometry));
        Assert.Equal(new SchematicPoint(140, 112), NativePoint(symbol, "A1"));
        Assert.Equal(new SchematicPoint(165, 110), SchematicAuthoringService.AnchorPoint(symbol, "A1"));
        Assert.Equal(new SchematicPoint(165, 120), SchematicAuthoringService.AnchorPoint(symbol, "A2"));
        Assert.Equal(new SchematicPoint(165, 130), SchematicAuthoringService.AnchorPoint(symbol, "A3"));
        Assert.Equal(new[] { "A1", "A2", "A3" }, Placements(symbol).Select(p => p.GetProperty("EndpointId").GetString()));
        Assert.Equal(Snapshot(project.Components), Snapshot(next.Components));
        Assert.Equal(Snapshot(project.BomItems), Snapshot(next.BomItems));
        Assert.Empty(next.Schematic.Wires);
        Assert.Empty(next.Connections);
        Assert.Empty(next.Nets);
        Assert.Empty(new TerminalConnectivityEngine().BuildGroups(next));
        Assert.Equal(before, Snapshot(project));
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void EnablingCollapsedUnconnectedPortMakesEveryFanoutPinVisibleWithoutInventingConnections()
    {
        var project = Fixture();
        var before = Snapshot(project);
        var next = Fanout(project, true);
        var symbol = next.Schematic!.Symbols[0];
        var owner = SchematicSymbolOwner.Resolve(next, symbol);

        Assert.DoesNotContain("PA", symbol.CollapsedPortIds);
        Assert.Contains("OTHER", symbol.CollapsedPortIds);
        Assert.All(symbol.Anchors.Where(a => a.EndpointId != "A4"),
            anchor => Assert.False(SchematicPortPresentation.IsCollapsedPin(next.Schematic, symbol, owner, anchor)));
        var body = SchematicPortPresentation.GenericBodyBounds(next.Schematic, symbol, owner);
        Assert.True(body.X + body.Width < SchematicAuthoringService.AnchorPoint(symbol, "A1").X);
        Assert.Empty(next.Schematic.Wires);
        Assert.Empty(next.Connections);
        Assert.Empty(next.Nets);
        Assert.Equal(before, Snapshot(project));
        var reset = Fanout(next, false);
        Assert.DoesNotContain("PA", reset.Schematic!.Symbols[0].CollapsedPortIds);
        Assert.Equal(new SchematicPoint(140, 112), SchematicAuthoringService.AnchorPoint(reset.Schematic.Symbols[0], "A1"));
    }

    [Fact]
    public void ExpandingFanoutKeepsWholePortRouteAuthoritativeAndVisible()
    {
        var project = Fixture();
        project.Connections.Add(new() { ConnectionId = "PORT-ROUTE", FromEndpointId = "PA", ToEndpointId = "PB" });
        project.Schematic!.Wires.Add(new() { WireId = "WHOLE-PORT", PageId = "P", ConnectionId = "PORT-ROUTE",
            Start = SchematicAttachment.Port("SA", "PA"), End = SchematicAttachment.Port("SB", "PB"),
            Points = [new(130, 115), new(200, 115), new(200, 120), new(260, 120)] });
        SchematicAuthoringService.Validate(project);
        var next = Fanout(project, true);
        var wire = Assert.Single(next.Schematic!.Wires);
        Assert.Equal(SchematicAttachment.Port("SA", "PA"), wire.Start);
        Assert.Equal(SchematicAttachment.Port("SB", "PB"), wire.End);
        Assert.Equal(SchematicPortPresentation.ConnectionPoint(next, next.Schematic.Symbols[0], "PA"), wire.Points[0]);
        Assert.True(SchematicPortPresentation.HasPortRoute(next.Schematic, next.Schematic.Symbols[0], "PA"));
        Assert.DoesNotContain("PA", next.Schematic.Symbols[0].CollapsedPortIds);
        Assert.Equal(Snapshot(project.Connections), Snapshot(next.Connections));
        Assert.Equal(Groups(project), Groups(next));
        SchematicAuthoringService.Validate(next);
    }

    [Theory]
    [InlineData("Right", 0, 165, 110)]
    [InlineData("Right", 90, 130, 165)]
    [InlineData("Right", 180, 75, 130)]
    [InlineData("Right", 270, 110, 75)]
    [InlineData("Left", 0, 75, 110)]
    [InlineData("Top", 0, 110, 75)]
    [InlineData("Bottom", 0, 110, 165)]
    [InlineData("Top", 90, 165, 110)]
    [InlineData("Bottom", 270, 165, 130)]
    public void FanoutFollowsNativeSideAndRotation(string side, int rotation, double x, double y)
    {
        var project = Fixture(side: side, rotation: rotation);
        var next = Fanout(project, true);
        Assert.Equal(new SchematicPoint(x, y), SchematicAuthoringService.AnchorPoint(next.Schematic!.Symbols[0], "A1"));
        Assert.Equal(Snapshot(project.Schematic!.Symbols[0].Anchors), Snapshot(next.Schematic.Symbols[0].Anchors));
    }

    [Fact]
    public void CompactGenericBodyDoesNotEncloseTheExpandedDisplayPins()
    {
        var project = Fixture(wired: true);
        var symbol = project.Schematic!.Symbols[0];
        var bounds = SchematicPortPresentation.GenericBodyBounds(project.Schematic, symbol, SchematicSymbolOwner.Resolve(project, symbol));
        var next = Fanout(project, true);
        symbol = next.Schematic!.Symbols[0];
        Assert.Equal(bounds, SchematicPortPresentation.GenericBodyBounds(next.Schematic, symbol, SchematicSymbolOwner.Resolve(next, symbol)));
        Assert.Equal(40, symbol.Width);
        Assert.Equal(40, symbol.Height);
        Assert.False(symbol.ManualSize);
    }

    [Fact]
    public void RenderedFanoutLeadsHaveIndependentLanesWithoutASharedBus()
    {
        var project = Fixture();
        var port = project.Components[0].Ports[0];
        port.Pins.Clear();
        var anchors = new List<SchematicAnchor>();
        for (var i = 1; i <= 6; i++)
        {
            port.Pins.Add(new() { PinId = "AF" + i, PinNumber = i.ToString() });
            anchors.Add(new() { EndpointId = "AF" + i, Position = new(40, 13 + 2 * i), Direction = "Right" });
        }
        anchors.Add(project.Schematic!.Symbols[0].Anchors.Single(a => a.EndpointId == "A4"));
        project.Schematic.Symbols[0] = project.Schematic.Symbols[0] with { Anchors = anchors };
        var next = Fanout(project, true, pitch: 12);
        var symbol = next.Schematic!.Symbols[0];
        var paths = port.Pins.Select(pin => new SchematicWire { WireId = pin.PinId, PageId = "P",
            Points = SchematicAuthoringService.PortFanoutLeadPoints(symbol, pin.PinId).ToList() }).ToArray();

        Assert.DoesNotContain(SchematicCrossingService.Analyze(paths).Conflicts, c => c.Code == "COLLINEAR_OVERLAP");
        Assert.Equal(6, paths.Select(p => p.Points[1].X).Distinct().Count());
        foreach (var path in paths)
        {
            Assert.Equal(NativePoint(symbol, path.WireId), path.Points[0]);
            Assert.Equal(SchematicAuthoringService.AnchorPoint(symbol, path.WireId), path.Points[^1]);
        }
        Assert.Empty(next.Schematic.Wires);
        Assert.Empty(next.Connections);
        Assert.Empty(new TerminalConnectivityEngine().BuildGroups(next));
    }

    [Fact]
    public void AutomaticPinRoutesUseSeparateReservedChannelsAndPreserveExactConnections()
    {
        var project = Fixture(wired: true, cad: true);
        var next = Fanout(project, true);
        Assert.Equal(Snapshot(project.Connections), Snapshot(next.Connections));
        Assert.Equal(Snapshot(project.Nets), Snapshot(next.Nets));
        Assert.Equal(Groups(project), Groups(next));
        Assert.Equal(3, next.Schematic!.Wires.Count);
        for (var i = 0; i < 3; i++)
        {
            var wire = next.Schematic.Wires[i];
            Assert.Equal(project.Schematic!.Wires[i].Start, wire.Start);
            Assert.Equal(project.Schematic.Wires[i].End, wire.End);
            Assert.Equal(SchematicAuthoringService.AnchorPoint(next.Schematic.Symbols[0], "A" + (i + 1)), wire.Points[0]);
            Assert.Equal(new SchematicPoint(260, 112 + i * 8), wire.Points[^1]);
        }
        Assert.DoesNotContain(SchematicCrossingService.Analyze(next.Schematic.Wires).Conflicts, c => c.Code == "COLLINEAR_OVERLAP");
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void AutomaticDraftRoutesAlsoReserveSeparateChannelsWithoutInventingConnections()
    {
        var project = Fixture(cad: true);
        for (var i = 1; i <= 3; i++)
            project.Schematic!.Wires.Add(new() { WireId = "DRAFT" + i, PageId = "P",
                Start = SchematicAttachment.Pin("SA", "A" + i),
                Points = [new(140, 104 + 8 * i), new(200, 104 + 8 * i), new(200, 150 + 10 * i), new(250, 150 + 10 * i)] });

        var next = Fanout(project, true);

        Assert.DoesNotContain(SchematicCrossingService.Analyze(next.Schematic!.Wires).Conflicts, c => c.Code == "COLLINEAR_OVERLAP");
        Assert.Empty(next.Connections);
        Assert.Empty(next.Nets);
        Assert.All(next.Schematic.Wires, w => Assert.Equal(SchematicAttachmentKind.Free, w.End.Kind));
        SchematicAuthoringService.Validate(next);
    }

    [Fact]
    public void ManualWireRetainsInteriorWaypointsAndResetReturnsNativeContact()
    {
        var project = Fixture(wired: true, cad: true);
        project.Schematic!.Wires.RemoveRange(1, 2);
        project.Connections.RemoveRange(1, 2);
        project.Schematic.Wires[0] = project.Schematic.Wires[0] with { ManualRoute = true,
            ManualSpecification = true, AreaMm2 = 2.5, ManualDesignation = true, Designation = "USER-WIRE",
            Points = [new(140, 112), new(190, 112), new(190, 180), new(230, 180), new(230, 112), new(260, 112)] };
        project.Connections[0].ConductorAreaMm2 = 2.5;
        var wire = project.Schematic.Wires[0];
        var next = Fanout(project, true);
        var moved = Assert.Single(next.Schematic!.Wires);
        foreach (var point in wire.Points.Skip(1)) Assert.Contains(point, moved.Points);
        Assert.True(moved.ManualRoute);
        Assert.Equal("USER-WIRE", moved.Designation);
        Assert.Equal(2.5, moved.AreaMm2);
        Assert.Equal(wire.Start, moved.Start);
        Assert.Equal(wire.End, moved.End);

        var reset = Fanout(next, false);
        Assert.Empty(Placements(reset.Schematic!.Symbols[0]));
        Assert.Equal(new SchematicPoint(140, 112), reset.Schematic.Wires[0].Points[0]);
        Assert.Equal(Snapshot(project.Connections), Snapshot(reset.Connections));
        Assert.Equal(Groups(project), Groups(reset));
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("wire")]
    public void LockedIncidentObjectRejectsEnableAndResetWithoutMutation(string locked)
    {
        var project = Fixture(wired: true, cad: true);
        var expanded = Fanout(project, true);
        foreach (var value in new[] { project, expanded })
        {
            if (locked == "symbol") value.Schematic!.Symbols[0] = value.Schematic.Symbols[0] with { Locked = true };
            else value.Schematic!.Wires[0] = value.Schematic.Wires[0] with { Locked = true };
            Reject(value, () => Fanout(value, value == project));
        }
    }

    [Theory]
    [InlineData("unknown-symbol")]
    [InlineData("unknown-port")]
    [InlineData("partial")]
    [InlineData("empty")]
    public void OnlyExistingCompleteRepresentedPortCanFanOut(string invalid)
    {
        var project = Fixture();
        if (invalid == "partial") project.Schematic!.Symbols[0].Anchors.RemoveAt(0);
        if (invalid == "empty") project.Components[0].Ports[0].Pins.Clear();
        Reject(project, () => Fanout(project, true, symbolId: invalid == "unknown-symbol" ? "missing" : "SA",
            portId: invalid == "unknown-port" ? "missing" : "PA"));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-10, 25)]
    [InlineData(double.NaN, 25)]
    [InlineData(double.PositiveInfinity, 25)]
    [InlineData(10, 0)]
    [InlineData(10, double.NaN)]
    public void InvalidPitchOrLeadRejectsWithoutMutation(double pitch, double lead)
    {
        var project = Fixture();
        Reject(project, () => Fanout(project, true, pitch, lead));
    }

    [Fact]
    public void DisplayContactOrLeadOutsideSheetOrInsideAnotherBodyRejectsAtomically()
    {
        var project = Fixture();
        Reject(project, () => Fanout(project, true, lead: 400));
        Reject(project, () => Fanout(project, true, pitch: 250));
        project.Schematic!.Symbols[1] = project.Schematic.Symbols[1] with { Position = new(155, 100), ManualSize = true };
        Reject(project, () => Fanout(project, true));
        project.Schematic.Symbols[1] = project.Schematic.Symbols[1] with { Position = new(145, 100), Width = 10, ManualSize = true };
        Reject(project, () => Fanout(project, true));
    }

    [Fact]
    public void JsonRoundTripAndMigrationPreserveDisplayPlacementWithUnchangedConnectivity()
    {
        var project = Fixture(cad: true, wired: true);
        var expanded = Fanout(project, true, pitch: 12, lead: 30);
        var restored = ElectricalProjectMigrator.Migrate(JsonSerializer.Deserialize<ElectricalProject>(Snapshot(expanded))!);
        Assert.Equal("0.12", restored.SchemaVersion);
        Assert.Equal(Snapshot(expanded.Schematic), Snapshot(restored.Schematic));
        Assert.Equal(Snapshot(project.Connections), Snapshot(restored.Connections));
        Assert.Equal(Snapshot(project.BomItems), Snapshot(restored.BomItems));
        Assert.Equal(Groups(project), Groups(restored));
        SchematicAuthoringService.Validate(restored);
        Assert.Equal(new SchematicPoint(170, 108), SchematicAuthoringService.AnchorPoint(restored.Schematic!.Symbols[0], "A1"));
        var reset = Fanout(restored, false);
        Assert.Equal(new SchematicPoint(140, 112), SchematicAuthoringService.AnchorPoint(reset.Schematic!.Symbols[0], "A1"));
        Assert.Equal("0.12", ElectricalProjectMigrator.Migrate(reset).SchemaVersion);
    }

    [Theory]
    [InlineData("legacy-version")]
    [InlineData("unknown-pin")]
    [InlineData("duplicate")]
    [InlineData("partial-port")]
    [InlineData("inside-body")]
    public void PersistedInvalidOrUnprotectedPlacementsFailClosed(string invalid)
    {
        var project = Fanout(Fixture(cad: true), true);
        var json = JsonNode.Parse(Snapshot(project))!;
        var placements = json["Schematic"]!["Symbols"]![0]!["EndpointDisplayPlacements"]!.AsArray();
        if (invalid == "legacy-version") json["SchemaVersion"] = "0.11";
        if (invalid == "unknown-pin") placements[0]!["EndpointId"] = "missing";
        if (invalid == "duplicate") placements.Add(placements[0]!.DeepClone());
        if (invalid == "partial-port") placements.RemoveAt(0);
        if (invalid == "inside-body") placements[0]!["Position"] = JsonSerializer.SerializeToNode(new SchematicPoint(20, 20));
        var restored = json.Deserialize<ElectricalProject>()!;
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(restored));
    }

    private ElectricalProject Fanout(ElectricalProject project, bool enabled, double pitch = 10, double lead = 25,
        string symbolId = "SA", string portId = "PA") => _service.SetPortFanout(project, symbolId, portId, enabled, pitch, lead);

    private static SchematicPoint NativePoint(SchematicSymbol symbol, string endpointId) =>
        SchematicAuthoringService.NativeAnchorPoint(symbol, endpointId);

    private static JsonElement[] Placements(SchematicSymbol symbol) =>
        JsonSerializer.SerializeToElement(symbol).GetProperty("EndpointDisplayPlacements").EnumerateArray().ToArray();
    private static string Snapshot<T>(T value) => JsonSerializer.Serialize(value);
    private static string[] Groups(ElectricalProject project) => new TerminalConnectivityEngine().BuildGroups(project)
        .Select(g => string.Join(",", g.EndpointIds.Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal).ToArray();
    private static void Reject(ElectricalProject project, Action action)
    {
        var before = Snapshot(project);
        Assert.Throws<InvalidOperationException>(action);
        Assert.Equal(before, Snapshot(project));
    }

    private static ElectricalProject Fixture(bool cad = false, bool wired = false, string side = "Right", int rotation = 0)
    {
        var project = new ElectricalProject { ProjectId = "fanout", Components =
        [new() { ComponentInstanceId = "A", ComponentDefinitionId = "synthetic", TypeKey = "TEST",
            Ports = [Port("PA", "A"), new() { PortId = "OTHER", Name = "Other", Pins = [new() { PinId = "A4", PinNumber = "4" }] }] },
            new() { ComponentInstanceId = "B", ComponentDefinitionId = "synthetic", TypeKey = "TEST", Ports = [Port("PB", "B")] }],
            BomItems = [new() { Row = new() { RowId = "synthetic-row", UsedQuantity = 1 }, ComponentInstanceIds = ["A"] }] };
        var sourceAnchors = Enumerable.Range(1, 3).Select(i => new SchematicAnchor { EndpointId = "A" + i,
            CadContactId = cad ? "CP" + i : null, Direction = side, Confirmed = true,
            Position = side switch { "Left" => new(0, 4 + 8 * i), "Top" => new(4 + 8 * i, 0),
                "Bottom" => new(4 + 8 * i, 40), _ => new(40, 4 + 8 * i) } }).ToList();
        sourceAnchors.Add(new() { EndpointId = "A4", Position = new(0, 20), Direction = "Left", Confirmed = true });
        project.Schematic = new() { Pages = [new() { PageId = "P", Title = "Synthetic" }], Symbols =
        [new() { SymbolId = "SA", ComponentInstanceId = "A", PageId = "P", Position = new(100, 100),
            Width = 40, Height = 40, Rotation = rotation, CollapsedPortIds = ["PA", "OTHER"], Anchors = sourceAnchors,
            Geometry = cad ? new() { Width = 40, Height = 40, SourceSha256 = new string('a', 64),
                ConnectionPoints = sourceAnchors.Take(3).Select(a => new SchematicCadContact(a.CadContactId!, "", a.Position)).ToList() } : null },
            new() { SymbolId = "SB", ComponentInstanceId = "B", PageId = "P", Position = new(260, 100), Width = 40, Height = 40,
                Anchors = Enumerable.Range(1, 3).Select(i => new SchematicAnchor { EndpointId = "B" + i,
                    Position = new(0, 4 + 8 * i), Direction = "Left", Confirmed = true }).ToList() }] };
        if (wired)
            for (var i = 1; i <= 3; i++)
            {
                project.Connections.Add(new() { ConnectionId = "CONN" + i, FromEndpointId = "A" + i, ToEndpointId = "B" + i });
                project.Schematic.Wires.Add(new() { WireId = "WIRE" + i, PageId = "P", ConnectionId = "CONN" + i,
                    Start = SchematicAttachment.Pin("SA", "A" + i), End = SchematicAttachment.Pin("SB", "B" + i),
                    Points = [new(140, 104 + 8 * i), new(260, 104 + 8 * i)] });
            }
        ProjectPinFunctionService.Synchronize(project);
        SchematicWireIdentification.Reconcile(project);
        SchematicAuthoringService.Validate(project);
        return project;
    }

    private static ComponentPort Port(string id, string prefix) => new() { PortId = id, Name = "IO",
        Pins = Enumerable.Range(1, 3).Select(i => new ComponentPin { PinId = prefix + i, PinNumber = i.ToString() }).ToList() };
}
