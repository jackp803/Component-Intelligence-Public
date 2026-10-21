using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicProjectSearchTests
{
    [Fact]
    public void DesignationFindsWireOnAnotherPageWithEndpointContextAndFocus()
    {
        var project = Fixture();
        var row = Assert.Single(SchematicProjectSearch.Find(project, "  sEnSoR1_10 \t"));
        Assert.Equal("synthetic-search", row.ProjectId);
        Assert.Equal("field", row.PageId);
        Assert.Equal(2, row.PageNumber);
        Assert.Equal("Field", row.PageTitle);
        Assert.Equal(SchematicProjectSearchKind.Wire, row.Kind);
        Assert.Equal("wire-1", row.ObjectId);
        Assert.Equal("Sensor1_10", row.Name);
        Assert.Contains("Sensor1", row.EndpointDescription);
        Assert.Contains("Supply.Port", row.EndpointDescription);
        Assert.Contains("10", row.EndpointDescription);
        Assert.Contains("24V supply", row.EndpointDescription);
        Assert.Contains("PLC1", row.EndpointDescription);
        Assert.Equal(new[] { "sensor-symbol", "plc-symbol" }, row.EndpointSymbolIds);
        Assert.True(row.FocusBounds.X <= 40 && row.FocusBounds.Y <= 100);
        Assert.True(row.FocusBounds.X + row.FocusBounds.Width >= 280);
        Assert.True(row.FocusBounds.Y + row.FocusBounds.Height >= 130);
    }

    [Theory]
    [InlineData("w01")]
    [InlineData("W001")]
    public void LegacyIndexUsesDocumentWireOrderAcrossAllPages(string query)
    {
        var row = Assert.Single(SchematicProjectSearch.Find(Fixture(), query));
        Assert.Equal("wire-1", row.ObjectId);
        Assert.Equal("Sensor1_10", row.Name);
    }

    [Theory]
    [InlineData("W01", "wire-1")]
    [InlineData("W001", "wire-1")]
    [InlineData("W02", "wire-2")]
    public void LegacyIndexMatchesLegendWithMatingCableAndUnnamedRoutes(string query, string wireId)
    {
        var p = Fixture();
        p.Connections.Add(new() { ConnectionId = "mate", FromEndpointId = "a", ToEndpointId = "b", Kind = ConnectionKind.DirectMating });
        p.Connections.Add(new() { ConnectionId = "cable", FromEndpointId = "c", ToEndpointId = "d", Kind = ConnectionKind.Cable, CableInstanceId = "physical-cable" });
        p.Schematic!.Wires.InsertRange(0, [
            new() { WireId = "mating-route", PageId = "field", ConnectionId = "mate", Designation = "Mating" },
            new() { WireId = "cable-route", PageId = "field", ConnectionId = "cable", Designation = "Cable" },
            new() { WireId = "unnamed-route", PageId = "field" }
        ]);
        var row = Assert.Single(SchematicProjectSearch.Find(p, query));
        Assert.Equal(wireId, row.ObjectId);
        Assert.Equal(wireId, SchematicWireLegendPresentation.Entries(p).Single(e =>
            e.Index == (query == "W001" ? "W01" : query)).WireId);
    }

    [Theory]
    [InlineData("sENSOR1")]
    [InlineData("Proximity")]
    [InlineData("e2e-x10")]
    [InlineData("Supply.Port")]
    [InlineData("Brown")]
    [InlineData("24v SUPPLY")]
    [InlineData("10")]
    [InlineData("Anchor label")]
    public void MatchesReferenceDisplayModelAndRepresentedPortPinLabelsFunctions(string query)
    {
        var rows = SchematicProjectSearch.Find(Fixture(), query);
        Assert.Single(rows, r => r.ObjectId == "sensor-symbol");
        Assert.Equal(rows.Count, rows.Select(r => (r.Kind, r.ObjectId)).Distinct().Count());
    }

    [Theory]
    [InlineData("[a+b]", true)]
    [InlineData(".*", false)]
    [InlineData("(a+)+$", false)]
    [InlineData("[", true)]
    [InlineData("^Sensor1$", false)]
    public void TreatsRegexCharactersAsLiteralText(string query, bool findsSensor)
    {
        Assert.Equal(findsSensor, SchematicProjectSearch.Find(Fixture(), query).Any(r => r.ObjectId == "sensor-symbol"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData("no such part")]
    public void EmptyAndUnmatchedQueriesHaveNoResults(string? query) =>
        Assert.Empty(SchematicProjectSearch.Find(Fixture(), query));

    [Fact]
    public void MissingProjectOrSchematicHasNoResults()
    {
        Assert.Empty(SchematicProjectSearch.Find(null, "anything"));
        Assert.Empty(SchematicProjectSearch.Find(new() { ProjectId = "empty" }, "anything"));
    }

    [Fact]
    public void MultipleMatchingFieldsDoNotDuplicateObjectsAndResultsFollowPageOrder()
    {
        var project = Fixture();
        project.Components[0].DisplayName = "Sensor1 Sensor1";
        project.Schematic!.Symbols.Add(project.Schematic.Symbols[0] with { SymbolId = "sensor-overview", PageId = "overview" });
        var rows = SchematicProjectSearch.Find(project, "Sensor1");
        Assert.Equal(3, rows.Count);
        Assert.Equal("sensor-overview", rows[0].ObjectId);
        Assert.Equal(new[] { 1, 2, 2 }, rows.Select(r => r.PageNumber));
        Assert.Single(rows, r => r.ObjectId == "sensor-symbol");
        Assert.Single(rows, r => r.ObjectId == "wire-1");
    }

    [Fact]
    public void GroupRowsKeepIndependentReferencesAndBounds()
    {
        var project = Fixture();
        foreach (var index in new[] { 1, 2 })
        {
            project.Components.Add(new() { ComponentInstanceId = "row-" + index, ComponentDefinitionId = "wago",
                TypeKey = "Terminal", ReferenceDesignator = "XW" + index, DisplayName = "WAGO 221-2411" });
            project.Schematic!.Symbols.Add(new() { SymbolId = "row-symbol-" + index, ComponentInstanceId = "row-" + index,
                WagoVisualGroupId = "visual-only", PageId = "overview", ManualSize = true,
                Position = new(20, 30 + (index - 1) * 18), Width = 24, Height = 18 });
        }
        var first = Assert.Single(SchematicProjectSearch.Find(project, "XW1"));
        var second = Assert.Single(SchematicProjectSearch.Find(project, "XW2"));
        Assert.Equal("row-symbol-1", first.ObjectId);
        Assert.Equal("row-symbol-2", second.ObjectId);
        Assert.NotEqual(first.FocusBounds, second.FocusBounds);
        Assert.Equal(2, SchematicProjectSearch.Find(project, "221-2411").Count);
    }

    [Fact]
    public void IndividualCableOwnersAndTheirPinsKeepSeparateRuntimeIdentities()
    {
        var project = Fixture();
        foreach (var index in new[] { 1, 2 })
        {
            project.Cables.Add(new() { CableInstanceId = "cable-" + index, CableDefinitionId = "cable-model",
                ReferenceDesignator = "CB" + index, DisplayName = "Test harness", ArchivedCable = new()
                { Template = new() { TemplateId = "template", TemplateRevision = "r1", AssetSha256 = "synthetic" },
                    Ports = [new() { PortId = "cable-port-" + index, Name = "Cable end", Pins = [new()
                    { PinId = "cable-pin-" + index, PinNumber = "1", PinName = "Cable contact", Function = "Cable function " + index }] }] } });
            project.Schematic!.Symbols.Add(new() { SymbolId = "cable-symbol-" + index, CableInstanceId = "cable-" + index,
                PageId = "field", Anchors = [new() { EndpointId = "cable-pin-" + index }] });
        }
        var second = Assert.Single(SchematicProjectSearch.Find(project, "CB2"));
        Assert.Equal(SchematicProjectSearchKind.Cable, second.Kind);
        Assert.Equal("cable-symbol-2", second.ObjectId);
        Assert.Equal("cable-symbol-1", Assert.Single(SchematicProjectSearch.Find(project, "Cable function 1")).ObjectId);
        Assert.Equal(2, SchematicProjectSearch.Find(project, "cable-model").Count);
    }

    [Fact]
    public void PinSearchOnlyFindsRepresentationsThatContainThatPin()
    {
        var project = Fixture();
        project.Components[0].Ports[0].Pins.Add(new() { PinId = "other-pin", PinNumber = "99", Function = "Other section" });
        project.Schematic!.Symbols.Add(project.Schematic.Symbols[0] with { SymbolId = "other-section", PageId = "overview",
            Anchors = [new() { EndpointId = "other-pin" }], SectionIndex = 2, SectionCount = 2 });
        var row = Assert.Single(SchematicProjectSearch.Find(project, "Other section"));
        Assert.Equal("other-section", row.ObjectId);
    }

    [Fact]
    public void SearchDoesNotMutateProjectAndReturnedCollectionsCannotBeChanged()
    {
        var project = Fixture();
        var before = JsonSerializer.Serialize(project);
        var results = SchematicProjectSearch.Find(project, "Sensor1_10");
        var row = Assert.Single(results);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Throws<NotSupportedException>(() => ((IList<SchematicProjectSearchResult>)results).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)row.EndpointSymbolIds).Clear());
        project.Schematic!.Wires[0] = project.Schematic.Wires[0] with { Designation = "Changed" };
        Assert.Equal("Sensor1_10", row.Name);
    }

    [Fact]
    public void ResolveUsesCurrentObjectsAfterMoveReorderAndRenameAndRejectsDeletedOrOtherProject()
    {
        var project = Fixture();
        var original = Assert.Single(SchematicProjectSearch.Find(project, "Sensor1_10"));
        var doc = project.Schematic!;
        doc.Pages.Reverse();
        doc.Wires[0] = doc.Wires[0] with { PageId = "overview", Designation = "Renamed", Points = [new(60, 70), new(80, 70)] };
        var current = SchematicProjectSearch.Resolve(project, original);
        Assert.NotNull(current);
        Assert.Equal("overview", current.PageId);
        Assert.Equal(2, current.PageNumber);
        Assert.Equal("Renamed", current.Name);
        Assert.Null(SchematicProjectSearch.Resolve(new() { ProjectId = "different", Schematic = doc }, original));
        doc.Wires.Clear();
        Assert.Null(SchematicProjectSearch.Resolve(project, original));
        Assert.Null(SchematicProjectSearch.Resolve(null, original));
    }

    [Fact]
    public void RepeatedSameEndpointIsListedOnceAndFreeEndsRemainDescribed()
    {
        var project = Fixture();
        var doc = project.Schematic!;
        doc.Wires[0] = doc.Wires[0] with { End = doc.Wires[0].Start };
        Assert.Equal(new[] { "sensor-symbol" }, Assert.Single(SchematicProjectSearch.Find(project, "Sensor1_10")).EndpointSymbolIds);
        var free = Assert.Single(SchematicProjectSearch.Find(project, "Panel_2"));
        Assert.False(string.IsNullOrWhiteSpace(free.EndpointDescription));
        Assert.Empty(free.EndpointSymbolIds);
        Assert.True(double.IsFinite(free.FocusBounds.Width) && free.FocusBounds.Width > 0);
        Assert.True(double.IsFinite(free.FocusBounds.Height) && free.FocusBounds.Height > 0);
    }

    private static ElectricalProject Fixture() => new()
    {
        ProjectId = "synthetic-search",
        Components = [new() { ComponentInstanceId = "sensor", ComponentDefinitionId = "sensor-definition", TypeKey = "Sensor",
            ReferenceDesignator = "Sensor1", DisplayName = "Proximity [a+b]", Ports = [new() { PortId = "sensor-port", Name = "Supply.Port",
                Pins = [new() { PinId = "sensor-pin", PinNumber = "10", PinName = "Brown", Function = "24V supply" }] }] },
            new() { ComponentInstanceId = "plc", ComponentDefinitionId = "plc-definition", TypeKey = "Controller",
                ReferenceDesignator = "PLC1", DisplayName = "Controller", Ports = [new() { PortId = "plc-port", Name = "Input",
                    Pins = [new() { PinId = "plc-pin", PinNumber = "1", Function = "Digital input" }] }] }],
        BomItems = [new() { ComponentDefinitionId = "sensor-definition", ComponentInstanceIds = ["sensor"],
            Row = new BomRow { RowId = "synthetic-model", Manufacturer = "Synthetic", ModelOrPartNumber = "E2E-X10" } }],
        Schematic = new()
        {
            Pages = [new() { PageId = "overview", Title = "Overview" }, new() { PageId = "field", Title = "Field" }],
            Symbols = [new() { SymbolId = "sensor-symbol", ComponentInstanceId = "sensor", PageId = "field", Position = new(40, 100),
                Anchors = [new() { EndpointId = "sensor-pin", Label = "Anchor label", Position = new(40, 10) }] },
                new() { SymbolId = "plc-symbol", ComponentInstanceId = "plc", PageId = "field", Position = new(240, 100),
                    Anchors = [new() { EndpointId = "plc-pin", Position = new(0, 10), Direction = "Left" }] }],
            Wires = [new() { WireId = "wire-1", PageId = "field", Designation = "Sensor1_10",
                Start = SchematicAttachment.Pin("sensor-symbol", "sensor-pin"), End = SchematicAttachment.Pin("plc-symbol", "plc-pin"),
                Points = [new(80, 110), new(240, 110)] }, new() { WireId = "wire-2", PageId = "overview", Designation = "Panel_2",
                Points = [new(10, 20), new(70, 20)] }]
        }
    };
}
