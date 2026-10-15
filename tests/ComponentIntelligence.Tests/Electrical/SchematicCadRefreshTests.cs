using System.Text.Json;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCadRefreshTests
{
    [Fact]
    public void MovedContactReanchorsWiredComponentWithoutChangingElectricalIdentity()
    {
        var project = ComponentProject();
        var before = JsonSerializer.Serialize(project);
        var electrical = ElectricalState(project);
        var old = project.Schematic!.Symbols[0];
        var wire = project.Schematic.Wires[0];

        var refreshed = Refresh(project, [old.SymbolId], UpdatedGeometry(), "draft-r2");
        var symbol = refreshed.Schematic!.Symbols[0];
        Assert.Equal(old.SymbolId, symbol.SymbolId);
        Assert.Equal(old.ComponentInstanceId, symbol.ComponentInstanceId);
        Assert.Equal(old.Position, symbol.Position);
        Assert.Equal(old.Rotation, symbol.Rotation);
        Assert.Equal(old.Anchors.Select(a => (a.EndpointId, a.SourcePinId, a.SourcePortId, a.CadContactId)),
            symbol.Anchors.Select(a => (a.EndpointId, a.SourcePinId, a.SourcePortId, a.CadContactId)));
        Assert.Equal(new SchematicPoint(5, 16), symbol.Anchors[0].Position);
        Assert.Equal(new SchematicPoint(25, 36), refreshed.Schematic.Wires[0].Points[0]);
        Assert.Equal(wire.Start, refreshed.Schematic.Wires[0].Start);
        Assert.Equal(wire.End, refreshed.Schematic.Wires[0].End);
        Assert.Equal(wire.ConnectionId, refreshed.Schematic.Wires[0].ConnectionId);
        Assert.Equal(electrical, ElectricalState(refreshed));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal(JsonSerializer.Serialize(project.Schematic.Symbols[1]), JsonSerializer.Serialize(refreshed.Schematic.Symbols[1]));
        Assert.Equal(JsonSerializer.Serialize(project.Schematic.Wires[1]), JsonSerializer.Serialize(refreshed.Schematic.Wires[1]));
        Assert.Null(symbol.AssetRevision);
        Assert.Contains(SchematicDraftReview.Inspect(refreshed), i => i.ObjectId == symbol.SymbolId && i.Code == "UNAPPROVED_REPRESENTATION");
        SchematicAuthoringService.Validate(refreshed);
    }

    [Fact]
    public void ReorderedContactsBindByOrdinalTagEvenWithMisleadingValuesAndSourceIds()
    {
        var project = ComponentProject();
        var geometry = UpdatedGeometry() with { ConnectionPoints = [
            new("CAD-B", "CAD-A", new(55, 24), "source-a", "Right"),
            new("CAD-A", "CAD-B", new(5, 16), "source-b", "Left")] };
        var refreshed = Refresh(project, ["component-symbol-1"], geometry);
        var anchors = refreshed.Schematic!.Symbols[0].Anchors;
        Assert.Equal("pin-1-a", anchors[0].EndpointId);
        Assert.Equal("source-a", anchors[0].SourcePinId);
        Assert.Equal("CAD-A", anchors[0].CadContactId);
        Assert.Equal(new SchematicPoint(5, 16), anchors[0].Position);
        Assert.Equal(new SchematicPoint(55, 24), anchors[1].Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDuplicateRequiredTagReportsEveryAffectedBindingAtomically(bool duplicate)
    {
        var project = ComponentProject();
        var before = JsonSerializer.Serialize(project);
        var contacts = UpdatedGeometry().ConnectionPoints.Where(c => c.Tag != "CAD-A").ToList();
        if (duplicate) contacts.AddRange(new[] { new SchematicCadContact("CAD-A", "", new(5, 16)), new("CAD-A", "", new(50, 16)) });
        else contacts.Add(new("cad-a", "CAD-A", new(0, 10), "source-a"));

        var error = Assert.Throws<InvalidOperationException>(() => Refresh(project,
            ["component-symbol-1", "component-symbol-2"], UpdatedGeometry() with { ConnectionPoints = contacts }));
        Assert.Contains("CAD-A", error.Message);
        Assert.Contains("source-a", error.Message);
        Assert.Contains("component-symbol-1", error.Message);
        Assert.Contains("component-symbol-2", error.Message);
        Assert.Contains("pin-1-a", error.Message);
        Assert.Contains("pin-2-a", error.Message);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void MixedMissingPinAndDuplicatePortTagsReportExactBindingsTogether()
    {
        var project = CableProject();
        var symbol = project.Schematic!.Symbols[0];
        var before = JsonSerializer.Serialize(project);
        var geometry = UpdatedGeometry() with { ConnectionPoints = [new("CAD-A", "", new(5, 16)), new("CAD-A", "", new(15, 16))] };
        var error = Assert.Throws<InvalidOperationException>(() => Refresh(project, [symbol.SymbolId], geometry));
        Assert.Contains(symbol.CadPortBindings[0].PortId, error.Message);
        Assert.Contains(symbol.Anchors.Single(a => a.CadContactId == "CAD-B").EndpointId, error.Message);
        Assert.Contains("CAD-A", error.Message);
        Assert.Contains("CAD-B", error.Message);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void RefreshPreservesPerRepresentationDisplayScaleRotationAndManualSize()
    {
        var project = ComponentProject();
        project.Schematic!.Wires.Clear();
        project.Schematic.Symbols[0] = project.Schematic.Symbols[0] with { Width = 20, Height = 15, Rotation = 90, ManualSize = true };
        var refreshed = Refresh(project, ["component-symbol-1"], UpdatedGeometry());
        var symbol = refreshed.Schematic!.Symbols[0];
        Assert.Equal(30, symbol.Width);
        Assert.Equal(20, symbol.Height);
        Assert.Equal(.5, SchematicSymbolPresentation.CadDisplayScale(symbol));
        Assert.True(symbol.ManualSize);
        Assert.Equal(90, symbol.Rotation);
        Assert.Equal(new SchematicPoint(20, 20), symbol.Position);
        Assert.Equal(new SchematicPoint(32, 22.5), SchematicAuthoringService.AnchorPoint(symbol, "pin-1-a"));
    }

    [Fact]
    public void LegacyCableRefreshRequiresAllRepresentationsOfSelectedPhysicalCable()
    {
        var project = CableProject();
        var service = new SchematicAuthoringService();
        project.Schematic!.Symbols.Add(project.Schematic.Symbols[0] with { SymbolId = "legacy-view", Position = new(140, 20) });
        var omitted = project.Schematic!.Symbols[1].SymbolId;
        var before = JsonSerializer.Serialize(project);
        var error = Assert.Throws<InvalidOperationException>(() => Refresh(project, ["cable-symbol-1"], UpdatedGeometry()));
        Assert.Contains(omitted, error.Message);
        Assert.Contains(project.Cables[0].CableInstanceId, error.Message);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void CableRefreshUpdatesOnlyExplicitlySelectedIndependentInstances()
    {
        var project = CableProject();
        var service = new SchematicAuthoringService();
        project = service.PlaceExistingRepresentation(project, "cable-symbol-1", "page", new(140, 20));
        project = service.AddArchivedCable(project, CableTemplate(), CableConstructionType.Custom, Geometry(),
            CableContacts(), "page", new(260, 20), "old.dxf", true);
        var first = project.Schematic!.Symbols[0];
        var second = project.Schematic.Symbols[1];
        project.Schematic.Symbols[1] = second with { Width = 20, Height = 15, Rotation = 180, ManualSize = true };
        var otherCable = JsonSerializer.Serialize(project.Cables[2]);
        var otherSymbol = JsonSerializer.Serialize(project.Schematic.Symbols[2]);
        var cable = project.Cables[0];
        var oldBinding = cable.ArchivedCable!;
        var oldTemplate = JsonSerializer.Serialize(oldBinding.Template);
        var electrical = ElectricalState(project);
        var geometry = Selected(UpdatedGeometry());

        var refreshed = Refresh(project, [first.SymbolId, second.SymbolId], geometry, "draft-r2");
        var binding = refreshed.Cables[0].ArchivedCable!;
        Assert.Equal(geometry.SourceSha256, binding.Template.AssetSha256);
        Assert.Equal(geometry.SelectionSha256, binding.Template.WiringSelectionSha256);
        Assert.Equal("draft-r2", binding.Template.TemplateRevision);
        Assert.Equal(oldTemplate, JsonSerializer.Serialize(binding.Template with {
            AssetSha256 = oldBinding.Template.AssetSha256, WiringSelectionSha256 = oldBinding.Template.WiringSelectionSha256,
            TemplateRevision = oldBinding.Template.TemplateRevision }));
        Assert.Equal(JsonSerializer.Serialize(oldBinding.Ports), JsonSerializer.Serialize(binding.Ports));
        Assert.Equal(oldBinding.Mapping, binding.Mapping);
        Assert.True(binding.MappingConfirmed);
        Assert.Equal(JsonSerializer.Serialize(oldBinding.Manufacturing), JsonSerializer.Serialize(binding.Manufacturing));
        Assert.Equal(JsonSerializer.Serialize(oldBinding.ManufacturingGeometry), JsonSerializer.Serialize(binding.ManufacturingGeometry));
        Assert.Equal(electrical, ElectricalState(refreshed));
        Assert.Equal(otherCable, JsonSerializer.Serialize(refreshed.Cables[2]));
        Assert.Equal(otherSymbol, JsonSerializer.Serialize(refreshed.Schematic!.Symbols[2]));
        Assert.Equal(30, refreshed.Schematic.Symbols[1].Width);
        Assert.Equal(20, refreshed.Schematic.Symbols[1].Height);
        Assert.Equal(180, refreshed.Schematic.Symbols[1].Rotation);
        Assert.All(refreshed.Schematic.Symbols.Take(2), s => {
            Assert.Null(s.AssetRevision);
            Assert.Equal("new.dxf", s.AssetPath);
            Assert.Equal(geometry.SourceSha256, s.AssetSha256);
            Assert.Equal(new SchematicPoint(5, 16), s.CadPortBindings[0].Position);
            Assert.Equal(new SchematicPoint(55, 24), s.Anchors.Single(a => a.CadContactId == "CAD-B").Position);
        });
        SchematicAuthoringService.Validate(refreshed);
        // Subsequent authoring must use the updated pinning without installing an archive asset.
        var placed = service.PlaceCableRepresentation(refreshed, cable.CableInstanceId, geometry, CableContacts(), "page", new(300, 100));
        Assert.Equal(4, placed.Schematic!.Symbols.Count);
    }

    [Fact]
    public void PortAndPinWireEndpointsFollowExactCableContactsWithoutInventingBindings()
    {
        var project = CableProject();
        var symbol = project.Schematic!.Symbols[0];
        var portId = symbol.CadPortBindings[0].PortId;
        var pinId = symbol.Anchors.Single(a => a.CadContactId == "CAD-B").EndpointId;
        var virtualPins = JsonSerializer.Serialize(symbol.Anchors.Where(a => a.CadContactId is null));
        project.Schematic.Wires.Add(new() { WireId = "port-wire", PageId = "page", ManualRoute = true,
            Start = SchematicAttachment.Port(symbol.SymbolId, portId), Points = [new(20, 30), new(10, 30), new(10, 90)] });
        project.Schematic.Wires.Add(new() { WireId = "pin-wire", PageId = "page", ManualRoute = true,
            End = SchematicAttachment.Pin(symbol.SymbolId, pinId), Points = [new(120, 40), new(60, 40)] });
        Normalize(project);

        var refreshed = Refresh(project, [symbol.SymbolId], UpdatedGeometry());
        Assert.Equal(new SchematicPoint(25, 36), refreshed.Schematic!.Wires[0].Points[0]);
        Assert.Equal(new SchematicPoint(75, 44), refreshed.Schematic.Wires[1].Points[^1]);
        Assert.Equal(project.Schematic.Wires[0].Start, refreshed.Schematic.Wires[0].Start);
        Assert.Equal(project.Schematic.Wires[1].End, refreshed.Schematic.Wires[1].End);
        Assert.Equal(virtualPins, JsonSerializer.Serialize(refreshed.Schematic.Symbols[0].Anchors.Where(a => a.CadContactId is null)));
        Assert.Empty(refreshed.Connections);
        SchematicAuthoringService.Validate(refreshed);
    }

    [Fact]
    public void ManualRouteRetainsItsWaypointsDesignationAndSpecifications()
    {
        var project = ComponentProject();
        var wire = project.Schematic!.Wires[0];
        var refreshed = Refresh(project, ["component-symbol-1"], UpdatedGeometry());
        var next = refreshed.Schematic!.Wires[0];
        Assert.True(next.ManualRoute);
        Assert.Equal(wire.Designation, next.Designation);
        Assert.Equal(wire.AreaMm2, next.AreaMm2);
        Assert.Equal(wire.LabelPosition, next.LabelPosition);
        Assert.Equal(wire.Points.Skip(1).ToArray(), next.Points.TakeLast(wire.Points.Count - 1).ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockedSymbolOrMovedLockedEndpointRejectsWithoutMutation(bool symbolLocked)
    {
        var project = ComponentProject();
        if (symbolLocked) project.Schematic!.Symbols[0] = project.Schematic.Symbols[0] with { Locked = true };
        else project.Schematic!.Wires[0] = project.Schematic.Wires[0] with { Locked = true };
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => Refresh(project, ["component-symbol-1"], UpdatedGeometry()));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void LockedRouteAtUnmovedContactRemainsExactlyUnchanged()
    {
        var project = ComponentProject();
        project.Schematic!.Wires[0] = project.Schematic.Wires[0] with { Locked = true };
        var wire = JsonSerializer.Serialize(project.Schematic.Wires[0]);
        var geometry = Geometry() with { SourceSha256 = new('b', 64), Primitives = [new() { Kind = "LINE", Start = new(2, 2), End = new(38, 2) }] };
        var refreshed = Refresh(project, ["component-symbol-1"], geometry);
        Assert.Equal(wire, JsonSerializer.Serialize(refreshed.Schematic!.Wires[0]));
        Assert.Equal(geometry.SourceSha256, refreshed.Schematic.Symbols[0].AssetSha256);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BranchReanchorsToItsExistingParentOrLockedBranchRejects(bool locked)
    {
        var project = ComponentProject();
        project.Schematic!.Wires.Clear();
        project.Connections.Clear();
        project.Schematic.Wires.Add(new() { WireId = "parent", PageId = "page", ManualRoute = true,
            Start = SchematicAttachment.Pin("component-symbol-1", "pin-1-a"), Points = [new(20, 30), new(80, 30)] });
        project.Schematic.Wires.Add(new() { WireId = "branch", PageId = "page", ManualRoute = true, Locked = locked,
            Start = SchematicAttachment.Junction("parent"), Points = [new(40, 30), new(40, 90)] });
        Normalize(project);
        var before = JsonSerializer.Serialize(project);
        if (locked)
        {
            Assert.Throws<InvalidOperationException>(() => Refresh(project, ["component-symbol-1"], UpdatedGeometry()));
            Assert.Equal(before, JsonSerializer.Serialize(project));
        }
        else
        {
            var refreshed = Refresh(project, ["component-symbol-1"], UpdatedGeometry());
            var branch = refreshed.Schematic!.Wires[1];
            Assert.Equal(SchematicAttachment.Junction("parent"), branch.Start);
            Assert.Equal(new SchematicPoint(40, 36), branch.Points[0]);
            Assert.Equal(new SchematicPoint(40, 90), branch.Points[^1]);
            Assert.Empty(refreshed.Connections);
            SchematicAuthoringService.Validate(refreshed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveReopenPreservesRefreshedGraphicsBindingsAndElectricalDeclarations(bool cable)
    {
        var project = cable ? CableProject() : ComponentProject();
        var electrical = ElectricalState(project);
        var refreshed = Refresh(project, [project.Schematic!.Symbols[0].SymbolId], Selected(UpdatedGeometry()), "draft-r2");
        var path = Path.Combine(Path.GetTempPath(), "cad-refresh-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            var saved = await repository.SaveSnapshotAsync(refreshed);
            var loaded = (await repository.GetAsync(saved.ProjectId))!;
            Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(loaded));
            Assert.Equal(electrical, ElectricalState(loaded));
            Assert.Null(loaded.Schematic!.Symbols[0].AssetRevision);
            Assert.Equal(new SchematicPoint(5, 16), cable ? loaded.Schematic.Symbols[0].CadPortBindings[0].Position : loaded.Schematic.Symbols[0].Anchors[0].Position);
            SchematicAuthoringService.Validate(loaded);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("selection")]
    [InlineData("units")]
    [InlineData("dimensions")]
    [InlineData("contact")]
    public void InvalidGraphicalEvidenceRejectsAtomically(string invalid)
    {
        var project = ComponentProject();
        var before = JsonSerializer.Serialize(project);
        var geometry = UpdatedGeometry();
        geometry = invalid switch {
            "hash" => geometry with { SourceSha256 = "invalid" },
            "selection" => geometry with { SelectionSha256 = new('c', 64) },
            "units" => geometry with { MillimetresPerUnit = 0 },
            "dimensions" => geometry with { Width = double.NaN },
            _ => geometry with { ConnectionPoints = [new("CAD-A", "", new(double.NaN, 16)), geometry.ConnectionPoints[1]] }
        };
        Assert.Throws<InvalidOperationException>(() => Refresh(project, ["component-symbol-1"], geometry));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void RefreshSnapshotsCallerOwnedGeometry()
    {
        var project = ComponentProject();
        var contacts = UpdatedGeometry().ConnectionPoints.ToList();
        var refreshed = Refresh(project, ["component-symbol-1"], UpdatedGeometry() with { ConnectionPoints = contacts });
        contacts.Clear();
        Assert.Equal(2, refreshed.Schematic!.Symbols[0].Geometry!.ConnectionPoints.Count);
        SchematicAuthoringService.Validate(refreshed);
    }

    private static ElectricalProject Refresh(ElectricalProject project, IReadOnlyCollection<string> ids,
        SchematicCadAsset geometry, string? revision = null) =>
        new SchematicAuthoringService().RefreshCad(project, ids, geometry, "new.dxf", revision);

    private static SchematicCadAsset Geometry() => new() { SourceSha256 = new('a', 64), MillimetresPerUnit = 1,
        Width = 40, Height = 30, Primitives = [new() { Kind = "LINE", Start = new(0, 10), End = new(40, 20) }],
        ConnectionPoints = [new("CAD-A", "", new(0, 10), Direction: "Left"), new("CAD-B", "", new(40, 20), Direction: "Right")] };

    private static SchematicCadAsset UpdatedGeometry() => Geometry() with { SourceSha256 = new('b', 64), Width = 60, Height = 40,
        ConnectionPoints = [new("CAD-A", "", new(5, 16), Direction: "Left"), new("CAD-B", "", new(55, 24), Direction: "Right")] };

    private static SchematicCadAsset Selected(SchematicCadAsset geometry)
    {
        var selected = geometry with { SelectionBounds = new(0, 0, geometry.Width, geometry.Height) };
        return selected with { SelectionSha256 = SchematicCadSelectionService.GeometryHash(selected) };
    }

    private static ElectricalProject ComponentProject()
    {
        var project = new ElectricalProject { ProjectId = "cad-refresh-components", Schematic = new() { Pages = [new() { PageId = "page", Title = "Synthetic" }] } };
        for (var i = 1; i <= 2; i++)
        {
            var component = new ComponentInstance { ComponentInstanceId = "component-" + i, ComponentDefinitionId = "definition",
                TypeKey = "fixture", Ports = [new() { PortId = "port-" + i, SourcePortId = "source-port", Name = "P", Pins = [
                    new() { PinId = $"pin-{i}-a", SourcePinId = "source-a", PinNumber = "1", Function = "Supply", FunctionManualOverride = true },
                    new() { PinId = $"pin-{i}-b", SourcePinId = "source-b", PinNumber = "2", Function = "Return", FunctionManualOverride = true }] }] };
            project.Components.Add(component);
            project.Schematic.Symbols.Add(new() { SymbolId = "component-symbol-" + i, ComponentInstanceId = component.ComponentInstanceId,
                PageId = "page", Position = new(i == 1 ? 20 : 200, 20), Width = 40, Height = 30,
                Geometry = Geometry(), AssetSha256 = Geometry().SourceSha256, AssetPath = "old.dxf", AssetRevision = "approved-r1",
                Anchors = component.Ports[0].Pins.Select((p, j) => new SchematicAnchor { EndpointId = p.PinId, SourcePinId = p.SourcePinId,
                    SourcePortId = "source-port", CadContactId = j == 0 ? "CAD-A" : "CAD-B", Position = j == 0 ? new(0, 10) : new(40, 20),
                    Direction = j == 0 ? "Left" : "Right", Confirmed = true }).ToList() });
        }
        project.Connections.Add(new() { ConnectionId = "connection", FromEndpointId = "pin-1-a", ToEndpointId = "pin-2-b", ConductorAreaMm2 = 1.5 });
        project.Schematic.Wires.Add(new() { WireId = "wire", PageId = "page", ConnectionId = "connection", ManualRoute = true,
            Designation = "W-SUPPLY", ManualDesignation = true, AreaMm2 = 1.5, ManualSpecification = true, LabelPosition = new(110, 60),
            Start = SchematicAttachment.Pin("component-symbol-1", "pin-1-a"), End = SchematicAttachment.Pin("component-symbol-2", "pin-2-b"),
            Points = [new(20, 30), new(10, 30), new(10, 60), new(240, 60), new(240, 40)] });
        project.Schematic.Wires.Add(new() { WireId = "unrelated", PageId = "page", ManualRoute = true, Points = [new(300, 200), new(340, 200)] });
        Normalize(project);
        return ElectricalProjectMigrator.Migrate(project);
    }

    private static ArchivedCableTemplate CableTemplate() => new() { TemplateId = "custom-cable", TemplateRevision = "approved-r1",
        AssetSha256 = Geometry().SourceSha256, DisplayName = "Synthetic cable", MappingConfirmed = true,
        MappingRevision = "mapping-r1", MappingEvidence = "synthetic exact pin table",
        Mapping = [new("p1.1", "p2.1"), new("p1.2", "p2.2")], TextBindings = [new("REF", CableTextField.Reference)],
        Manufacturing = new() { InventoryOnly = true, PinUsage = [new("p1.2", CablePinUsage.Pending)] },
        Ports = [new() { PortId = "P1", Name = "Input", Pins = [new() { PinId = "p1.1", PinNumber = "1", Function = "Supply" }, new() { PinId = "p1.2", PinNumber = "2", Function = "Return" }] },
            new() { PortId = "P2", Name = "Output", Pins = [new() { PinId = "p2.1", PinNumber = "1", Function = "Supply" }, new() { PinId = "p2.2", PinNumber = "2", Function = "Return" }] }] };

    private static Dictionary<string, string> CableContacts() => new() { ["P1"] = "CAD-A", ["p2.1"] = "CAD-B" };

    private static ElectricalProject CableProject()
    {
        var project = new ElectricalProject { ProjectId = "cad-refresh-cable", Schematic = new() { Pages = [new() { PageId = "page", Title = "Synthetic" }] } };
        project = new SchematicAuthoringService().AddArchivedCable(project, CableTemplate(), CableConstructionType.Custom,
            Geometry(), CableContacts(), "page", new(20, 20), "old.dxf", true, Geometry() with { SourceSha256 = new('c', 64) });
        project.Schematic!.Symbols[0] = project.Schematic.Symbols[0] with { SymbolId = "cable-symbol-1" };
        project.Cables[0].ProvidedLengthMm = 1234;
        project.Cables[0].Specification = "manual spec";
        Normalize(project);
        return ElectricalProjectMigrator.Migrate(project);
    }

    private static string ElectricalState(ElectricalProject project) => JsonSerializer.Serialize(new {
        project.Components, project.Connections, project.Nets, project.TopologyRoutes,
        Cables = project.Cables.Select(c => new { c.CableInstanceId, c.CableDefinitionId, c.ProvidedLengthMm, c.Specification,
            c.CableConstructionType, c.ArchivedCable!.Ports, c.ArchivedCable.Mapping, c.ArchivedCable.MappingConfirmed,
            c.ArchivedCable.HasMappingOverride, c.ArchivedCable.Manufacturing, c.ArchivedCable.ManufacturingGeometry }) });

    private static void Normalize(ElectricalProject project)
    {
        SchematicAuthoringService.Validate(project);
        ProjectPinFunctionService.Synchronize(project);
        SchematicWireIdentification.Reconcile(project);
    }
}
