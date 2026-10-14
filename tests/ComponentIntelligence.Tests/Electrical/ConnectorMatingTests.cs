using ComponentIntelligence.Electrical.Bom;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ConnectorMatingTests
{
    [Fact]
    public async Task CrossPageMatingUsesArrowsWithoutAwgAndRoundTripsWithFunctionProvenance()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0].Function = "GND";
        var service = new SchematicAuthoringService();
        var next = service.MatePorts(p, SchematicAttachment.Port(a.SymbolId, from.PortId),
            SchematicAttachment.Port(b.SymbolId, to.PortId), ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked", "J1");
        Assert.Empty(p.Connections); Assert.Empty(p.Schematic!.Continuations);
        Assert.Single(next.Schematic!.Continuations); Assert.Equal(2, next.Schematic.Wires.Count);
        Assert.All(next.Schematic.Wires, wire => { Assert.Null(wire.Awg); Assert.Null(wire.SizingProposal);
            Assert.Null(SchematicCableLabelPresentation.Resolve(next, wire)); });
        Assert.Empty(SchematicWireLegendPresentation.Entries(next));
        Assert.Equal("GND", Port(next, b).Pins[0].DisplayFunction);
        var path = Path.Combine(Path.GetTempPath(), "synthetic-mating-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            var repo = new ComponentIntelligence.Electrical.Persistence.ElectricalProjectRepository(
                new ComponentIntelligence.Repository.SqliteConnectionFactory(), path);
            await repo.SaveAsync(next); var loaded = (await repo.GetAsync(next.ProjectId))!;
            Assert.Equal("0.11", loaded.SchemaVersion);
            Assert.Equal(2, loaded.Connections[0].MatingMap!.Pairs.Count);
            Assert.Equal("GND", Port(loaded, b).Pins[0].DisplayFunction);
            Assert.True(loaded.Cables[0].ArchivedCable!.MappingConfirmed);
            var removed = service.DeleteWire(loaded, loaded.Schematic!.Wires[0].WireId);
            Assert.Empty(removed.Connections); Assert.Equal(2, removed.Cables.Count);
            Assert.Empty(removed.Schematic!.Continuations);
            Assert.Null(Port(removed, b).Pins[0].DisplayFunction);
        } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void SourceChangesAndReadOnlyReviewNeverTreatInferredLabelsAsSources()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        var root = SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0]; root.Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        ProjectPinFunctionService.Synchronize(p);
        root.Function = "COM"; ProjectPinFunctionService.Synchronize(p);
        Assert.Equal("COM", to.Pins[0].DisplayFunction);
        var before = System.Text.Json.JsonSerializer.Serialize(p);
        ProjectPinFunctionService.Inspect(p);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(p));
        root.Function = null; ProjectPinFunctionService.Synchronize(p);
        Assert.Null(to.Pins[0].DisplayFunction);
    }

    [Fact]
    public void ManufacturingRowsDisplayInferredValuesAndPreserveThemWhenOtherMetadataChanges()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0].Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        ProjectPinFunctionService.Synchronize(p);
        var editor = new CableManufacturingEditorService(); var draft = editor.Prepare(p.Cables[1]);
        Assert.Contains(draft.Rows, row => row.FromFunction == "GND" || row.ToFunction == "GND");
        draft.Ends[0].Name = "Renamed";
        var next = editor.ApplyToInstance(p.Cables[1], draft);
        Assert.Null(next.ArchivedCable!.Ports[0].Pins[0].Function);
        p.Cables[1] = next; p.Connections.Clear(); ProjectPinFunctionService.Synchronize(p);
        Assert.Null(p.Cables[1].ArchivedCable!.Ports[0].Pins[0].DisplayFunction);
    }
    [Fact]
    public void CableMateUsesActualPinNumbersWithoutMergingCircuitsOrAddingMaterials()
    {
        var (p, a, b) = Drawing();
        var from = Port(p, a); var to = Port(p, b); to.Pins.Reverse();
        var before = new DerivedBomEngine().Build(p).Sum(l => l.Quantity);
        var pairs = ConnectorMatingService.Prepare(p, from.PortId, to.PortId);
        Assert.Equal(["1", "7"], pairs.Select(x => from.Pins.Single(pin => pin.PinId == x.FromPinId).PinNumber));
        Assert.All(pairs, pair => Assert.Equal(from.Pins.Single(pin => pin.PinId == pair.FromPinId).PinNumber,
            to.Pins.Single(pin => pin.PinId == pair.ToPinId).PinNumber));
        var mate = ConnectorMatingService.Connect(p, from.PortId, to.PortId, pairs, "user verified synthetic contacts");
        Assert.Equal(ConnectionKind.DirectMating, mate.Kind); Assert.Null(mate.CableInstanceId); Assert.Null(mate.ConductorAreaMm2);
        Assert.Equal(2, p.Cables.Count); Assert.Equal(before, new DerivedBomEngine().Build(p).Sum(l => l.Quantity));
        var graph = new TerminalConnectivityEngine();
        Assert.True(graph.AreConnected(p, from.Pins[0].PinId, pairs[0].ToPinId));
        Assert.False(graph.AreConnected(p, from.Pins[0].PinId, from.Pins[1].PinId));
        Assert.False(graph.AreConnected(p, from.PortId, to.PortId));
    }

    [Theory]
    [InlineData("gender")] [InlineData("coding")] [InlineData("count")] [InlineData("unknown")] [InlineData("duplicate-number")]
    public void MatingRejectsUnknownOrIncompatibleInventory(string kind)
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        switch (kind) {
            case "gender": to.Connector!.Gender = ConnectorGender.Male; break;
            case "coding": to.Connector!.Coding = "D"; break;
            case "count": to.Connector!.PinCount = 3; break;
            case "unknown": to.Connector!.Coding = null; break;
            case "duplicate-number": to.Pins[1].PinNumber = "1"; break;
        }
        Assert.Throws<InvalidOperationException>(() => ConnectorMatingService.Prepare(p, from.PortId, to.PortId));
        Assert.Empty(p.Connections); Assert.Equal(2, p.Cables.Count);
    }

    [Fact]
    public void MappingMustBeCompleteUniqueAndConfirmed()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        var pairs = ConnectorMatingService.Prepare(p, from.PortId, to.PortId);
        Assert.Throws<InvalidOperationException>(() => ConnectorMatingService.Connect(p, from.PortId, to.PortId, pairs.Take(1).ToArray(), "checked"));
        Assert.Throws<InvalidOperationException>(() => ConnectorMatingService.Connect(p, from.PortId, to.PortId, [pairs[0], pairs[0]], "checked"));
        Assert.Throws<InvalidOperationException>(() => ConnectorMatingService.Connect(p, from.PortId, to.PortId, pairs, ""));
        Assert.Empty(p.Connections);
        var mate = ConnectorMatingService.Connect(p, from.PortId, to.PortId, pairs, "checked");
        Assert.Throws<InvalidOperationException>(() => ConnectorMatingService.Connect(p, from.PortId, to.PortId, pairs, "checked"));
        p.Components.Add(new() { ComponentInstanceId = "d", ComponentDefinitionId = "d", TypeKey = "sensor",
            Ports = [new() { PortId = "d.p", Name = "P", Pins = [new() { PinId = "d.1", PinNumber = "1" }] }] });
        Assert.Throws<InvalidOperationException>(() => new TopologyEndpointConnectionService().ConnectEndpoints(p, pairs[0].FromPinId, "d.1"));
        p.Connections.Remove(mate);
        Assert.Equal(ConnectionKind.Wire, new TopologyEndpointConnectionService().ConnectEndpoints(p, pairs[0].FromPinId, "d.1").Kind);
    }

    [Fact]
    public void PropagationTraversesConfirmedCableInteriorsAndKeepsIndependentInstances()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        var root = SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0]; root.Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        ProjectPinFunctionService.Synchronize(p);
        var destination = SchematicSymbolOwner.Resolve(p, b).Ports[1].Pins[0];
        Assert.Equal("GND", destination.DisplayFunction); Assert.Null(destination.Function);
        Assert.Equal(root.PinId, destination.InferredFunction!.SourcePinId);
        Assert.Null(SchematicSymbolOwner.Resolve(p, b).Ports[1].Pins[1].DisplayFunction);
        p.Cables.Add(ArchivedCableInstanceFactory.Create(Template("third", ConnectorGender.Female), CableConstructionType.Custom));
        ProjectPinFunctionService.Synchronize(p);
        Assert.All(p.Cables[^1].ArchivedCable!.Ports.SelectMany(x => x.Pins), pin => Assert.Null(pin.DisplayFunction));
        p.Connections.Clear(); ProjectPinFunctionService.Synchronize(p);
        Assert.Null(destination.DisplayFunction); Assert.Equal("GND", root.Function);
    }

    [Fact]
    public void UnknownInternalMapStopsInferenceAndShowsReviewItem()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        p.Cables[0].ArchivedCable = p.Cables[0].ArchivedCable! with { MappingConfirmed = false };
        SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0].Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        var issues = ProjectPinFunctionService.Synchronize(p);
        Assert.Null(to.Pins[0].DisplayFunction);
        Assert.Contains(issues, issue => issue.Code == "UNCONFIRMED_INTERNAL_MAPPING");
    }

    [Fact]
    public void ManualAndBlankOverridesSurviveChangesAndConflictsClearGeneratedLabels()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        var root = SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0]; root.Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        ProjectPinFunctionService.Synchronize(p);
        Assert.Equal("GND", to.Pins[0].DisplayFunction);
        ProjectPinFunctionService.SetOverride(p, to.Pins[0].PinId, "", true);
        root.Function = "COM"; ProjectPinFunctionService.Synchronize(p);
        Assert.True(to.Pins[0].FunctionManualOverride); Assert.True(string.IsNullOrEmpty(to.Pins[0].DisplayFunction));
        ProjectPinFunctionService.SetOverride(p, to.Pins[0].PinId, null, false);
        Assert.Equal("COM", to.Pins[0].DisplayFunction);
        ProjectPinFunctionService.SetOverride(p, to.Pins[0].PinId, "24V", true);
        var issues = ProjectPinFunctionService.Synchronize(p);
        Assert.Equal("24V", to.Pins[0].DisplayFunction); Assert.Equal("COM", root.Function);
        Assert.Contains(issues, issue => issue.Code == "FUNCTION_CONFLICT");
        Assert.Null(from.Pins[0].InferredFunction);
    }

    [Fact]
    public void ManufacturingNoOpShowsInferenceWithoutFreezingItIntoDeclaredFunctions()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0].Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        ProjectPinFunctionService.Synchronize(p);
        var editor = new CableManufacturingEditorService(); var draft = editor.Prepare(p.Cables[1]);
        Assert.Equal("GND", draft.Ends[0].Pins[0].DisplayFunction);
        var next = editor.ApplyToInstance(p.Cables[1], draft);
        Assert.Null(next.ArchivedCable!.Ports[0].Pins[0].Function);
        Assert.True(next.ArchivedCable.MappingConfirmed);
        Assert.Null(next.ArchivedCable.Template.Ports[0].Pins[0].Function);
    }

    [Fact]
    public void CablePortContinuationExplainsRequiredMatingMapInsteadOfUnknownEndpoint()
    {
        var (project, first, second) = Drawing();
        var service = new SchematicAuthoringService();
        project = service.DrawWire(project, first.PageId, SchematicAttachment.Port(first.SymbolId, Port(project, first).PortId),
            SchematicAttachment.Free(), [new(80, 30), new(100, 30)]);
        project = service.DrawWire(project, second.PageId, SchematicAttachment.Free(),
            SchematicAttachment.Port(second.SymbolId, Port(project, second).PortId), [new(10, 30), new(20, 30)]);
        var error = Assert.Throws<InvalidOperationException>(() => service.ConnectAcrossPages(project,
            project.Schematic!.Wires[0].WireId, false, project.Schematic.Wires[1].WireId, true, "J1"));
        Assert.Contains("接頭對接", error.Message);
    }

    internal static ComponentPort Port(ElectricalProject project, SchematicSymbol symbol) =>
        SchematicSymbolOwner.Resolve(project, symbol).Ports[0];

    [Fact]
    public void ValidatorChecksActualMatedPinsAndReportsFunctionConflicts()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        from.Pins[0].Power = new() { Role = PowerRole.Source };
        to.Pins[0].Power = new() { Role = PowerRole.Source };
        from.Pins[0].Function = "GND"; to.Pins[0].Function = "24V";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        var report = new ComponentIntelligence.Electrical.Validation.ElectricalProjectValidator().Validate(p);
        Assert.Contains(report.Results, x => x.RuleId == "RULE-PWR-004");
        Assert.Contains(report.Results, x => x.RuleId == "RULE-FUNCTION_CONFLICT");
        Assert.DoesNotContain(report.Results, x => x.RuleId == "RULE-CONN-001");
    }

    [Fact]
    public void SamePageMatingUsesExplicitPairsAndCannotAcquireWireGauge()
    {
        var (p, a, b) = Drawing(false); var from = Port(p, a); var to = Port(p, b);
        var service = new SchematicAuthoringService();
        var next = service.MatePorts(p, SchematicAttachment.Port(a.SymbolId, from.PortId),
            SchematicAttachment.Port(b.SymbolId, to.PortId), ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked", "J1");
        Assert.Single(next.Schematic!.Wires); Assert.Empty(next.Schematic.Continuations);
        Assert.Throws<InvalidOperationException>(() => service.SetWireAwg(next, next.Schematic.Wires[0].WireId, 20));
        Assert.Null(next.Schematic.Wires[0].SizingProposal);
    }

    [Fact]
    public void MatingCannotBeConvertedToAnExtraCableByLegacyMappingEditor()
    {
        var p = new ElectricalProject { ProjectId = "component-mating" };
        foreach (var id in new[] { "a", "b" }) p.Components.Add(new() { ComponentInstanceId = id, ComponentDefinitionId = id, TypeKey = id,
            Ports = [new() { PortId = id, Name = id, Connector = Connector(id == "a" ? ConnectorGender.Male : ConnectorGender.Female),
                Pins = [new() { PinId = id + "1", PinNumber = "1" }, new() { PinId = id + "7", PinNumber = "7" }] }] });
        var mate = ConnectorMatingService.Connect(p, "a", "b", ConnectorMatingService.Prepare(p, "a", "b"), "checked");
        var editor = new ConnectionPinMappingService();
        Assert.Equal(2, editor.GetMappings(p, mate.ConnectionId).Count);
        Assert.Throws<InvalidOperationException>(() => editor.SetMappings(p, mate.ConnectionId, [new("a1", "b1")]));
        Assert.Empty(p.Cables); Assert.Equal(ConnectionKind.DirectMating, mate.Kind);
        p.Components.Add(new() { ComponentInstanceId = "c", ComponentDefinitionId = "c", TypeKey = "c",
            Ports = [new() { PortId = "c", Name = "c", Connector = Connector(ConnectorGender.Female) }] });
        Assert.Throws<InvalidOperationException>(() => new TopologyEndpointConnectionService().ConnectEndpoints(p, "a", "c"));
    }

    [Fact]
    public void ManualFunctionCaptionDoesNotUseStalePinName()
    {
        var (p, a, _) = Drawing(); var pin = Port(p, a).Pins[0]; pin.PinName = "GND";
        ProjectPinFunctionService.SetOverride(p, pin.PinId, "COM", true);
        var contact = SchematicWireContacts.Resolve(p, SchematicAttachment.Pin(a.SymbolId, pin.PinId));
        Assert.Equal("COM", contact!.ShortFunction);
        ProjectPinFunctionService.SetOverride(p, pin.PinId, "", true);
        Assert.Empty(SchematicWireContacts.Resolve(p, SchematicAttachment.Pin(a.SymbolId, pin.PinId))!.ShortFunction);
    }

    [Fact]
    public void DuplicatePersistedMateIdsCannotBypassOccupancyWithoutRoutes()
    {
        var (p, _, _) = Drawing(); p.Schematic = null;
        var from = p.Cables[0].ArchivedCable!.Ports[0]; var to = p.Cables[1].ArchivedCable!.Ports[0];
        var mate = ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        var third = ArchivedCableInstanceFactory.Create(Template("third", ConnectorGender.Female), CableConstructionType.Custom);
        p.Cables.Add(third); var other = third.ArchivedCable!.Ports[0];
        p.Connections.Add(new() { ConnectionId = mate.ConnectionId, Kind = ConnectionKind.DirectMating,
            FromEndpointId = from.PortId, ToEndpointId = other.PortId, MatingMap = new() { Confirmed = true, Evidence = "checked",
                Pairs = ConnectorMatingService.Prepare(p, from.PortId, other.PortId).ToList() } });
        Assert.Throws<InvalidOperationException>(() => ConnectorMatingService.Validate(p, mate));
        Assert.Throws<InvalidOperationException>(() => new TerminalConnectivityEngine().BuildGroups(p));
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(p));
    }

    [Fact]
    public void ClearingInferredManufacturingFunctionCreatesPersistentBlankOverride()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a); var to = Port(p, b);
        SchematicSymbolOwner.Resolve(p, a).Ports[1].Pins[0].Function = "GND";
        ConnectorMatingService.Connect(p, from.PortId, to.PortId, ConnectorMatingService.Prepare(p, from.PortId, to.PortId), "checked");
        ProjectPinFunctionService.Synchronize(p);
        var editor = new CableManufacturingEditorService(); var draft = editor.Prepare(p.Cables[1]);
        foreach (var row in draft.Rows)
        foreach (var port in draft.Ends)
        {
            var cell = CableManufacturingCells.Get(draft, row, port.PortId);
            if (cell.SourcePinId == to.Pins[0].SourcePinId)
            { cell.Function = ""; CableManufacturingCells.Set(draft, row, port.PortId, cell); }
        }
        p.Cables[1] = editor.ApplyToInstance(p.Cables[1], draft); ProjectPinFunctionService.Synchronize(p);
        var pin = p.Cables[1].ArchivedCable!.Ports[0].Pins[0];
        Assert.True(pin.FunctionManualOverride); Assert.True(string.IsNullOrEmpty(pin.DisplayFunction));
        var reopened = editor.ApplyToInstance(p.Cables[1], editor.Prepare(p.Cables[1]));
        Assert.True(reopened.ArchivedCable!.Ports[0].Pins[0].FunctionManualOverride);
    }

    [Fact]
    public void KnownQuattroTerminalContinuitySharesFunctionInferenceButUnknownBodyDoesNot()
    {
        var (p, a, b) = Drawing(); var from = Port(p, a).Pins[0]; var to = Port(p, b).Pins[0]; from.Function = "COM";
        p.Components.Add(new() { ComponentInstanceId = "tb", ComponentDefinitionId = "PHOENIX_CONTACT_3209578", TypeKey = "TerminalBlock",
            Ports = [new() { PortId = "tb-port", Name = "TB", Pins = [new() { PinId = "tb.1", PinNumber = "1" }, new() { PinId = "tb.2", PinNumber = "2" }] }] });
        p.Connections.Add(new() { ConnectionId = "left", FromEndpointId = from.PinId, ToEndpointId = "tb.1", Kind = ConnectionKind.Wire });
        p.Connections.Add(new() { ConnectionId = "right", FromEndpointId = "tb.2", ToEndpointId = to.PinId, Kind = ConnectionKind.Wire });
        ProjectPinFunctionService.Synchronize(p); Assert.Equal("COM", to.DisplayFunction);
        p.Components[0] = new() { ComponentInstanceId = "tb", ComponentDefinitionId = "UNKNOWN_TERMINAL", TypeKey = "TerminalBlock", Ports = p.Components[0].Ports };
        ProjectPinFunctionService.Synchronize(p); Assert.Null(to.DisplayFunction);
    }

    internal static ArchivedCableTemplate Template(string id, ConnectorGender gender, bool confirmed = true) => new()
    {
        TemplateId = id, TemplateRevision = "r1", AssetSha256 = new('a', 64),
        MappingConfirmed = confirmed, MappingRevision = confirmed ? "r1" : null,
        MappingEvidence = confirmed ? "synthetic verified mapping" : null,
        Ports = [
            new() { PortId = "face", Name = "P1", Connector = Connector(gender),
                Pins = [new() { PinId = "face.1", PinNumber = "1" }, new() { PinId = "face.7", PinNumber = "7" }] },
            new() { PortId = "back", Name = "P2",
                Pins = [new() { PinId = "back.1", PinNumber = "1" }, new() { PinId = "back.2", PinNumber = "2" }] }],
        Mapping = [new("face.1", "back.1"), new("face.7", "back.2")]
    };

    internal static ConnectorDefinition Connector(ConnectorGender gender) => new()
    { ConnectorId = "synthetic", Family = "M12", Coding = "A", PinCount = 2, Gender = gender };

    internal static (ElectricalProject Project, SchematicSymbol First, SchematicSymbol Second) Drawing(bool acrossPages = true)
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new() { ProjectId = "synthetic-mating" }, "First");
        if (acrossPages) project = service.AddPage(project, "Second");
        var geometry = new SchematicCadAsset { SourceSha256 = new('a', 64), Width = 60, Height = 20,
            MillimetresPerUnit = 1, Primitives = [new() { Kind = "LINE", Start = new(0, 10), End = new(60, 10) }],
            ConnectionPoints = [new("contact", "", new(60, 10), Direction: "Right")] };
        project = service.AddArchivedCable(project, Template("male", ConnectorGender.Male),
            CableConstructionType.Custom, geometry, new Dictionary<string, string> { ["face"] = "contact" },
            project.Schematic!.Pages[0].PageId, new(20, 20));
        project = service.AddArchivedCable(project, Template("female", ConnectorGender.Female),
            CableConstructionType.Custom, geometry with { ConnectionPoints = [new("contact", "", new(0, 10), Direction: "Left")] },
            new Dictionary<string, string> { ["face"] = "contact" },
            project.Schematic!.Pages[^1].PageId, new(acrossPages ? 20 : 180, 20));
        return (project, project.Schematic!.Symbols[0], project.Schematic.Symbols[1]);
    }
}
