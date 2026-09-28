using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Electrical.Validation;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ArchivedCableInstanceTests
{
    private static ArchivedCableTemplate Template(bool confirmed = false) => new()
    {
        TemplateId = "company-y", TemplateRevision = "rev-001", AssetSha256 = new string('A', 64),
        DisplayName = "Y cable", MappingRevision = confirmed ? "mapping-001" : null,
        MappingConfirmed = confirmed, MappingEvidence = confirmed ? "fixture-approved-pin-table" : null,
        Ports = [new() { PortId = "common", Name = "ETH", Pins = [new() { PinId = "common-5", PinNumber = "5" }] },
                 new() { PortId = "branch-a", Name = "M12 A", Pins = [new() { PinId = "a-1", PinNumber = "1" }] },
                 new() { PortId = "branch-b", Name = "M12 B", Pins = [new() { PinId = "b-1", PinNumber = "1" }] }],
        Mapping = confirmed ? [new("common-5", "a-1")] : []
    };

    [Fact]
    public void ReusingTemplateCreatesIndependentPhysicalInstancesWithoutGuessingMapping()
    {
        var template = Template(); var before = JsonSerializer.Serialize(template);
        var first = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        var second = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        Assert.NotEqual(first.CableInstanceId, second.CableInstanceId);
        Assert.Null(first.ProvidedLengthMm);
        Assert.False(first.ArchivedCable!.MappingConfirmed);
        Assert.Empty(first.ArchivedCable.Mapping);
        Assert.Empty(first.ArchivedCable.Ports.SelectMany(p => p.Pins).Select(p => p.PinId)
            .Intersect(second.ArchivedCable!.Ports.SelectMany(p => p.Pins).Select(p => p.PinId)));
        Assert.Equal("common-5", first.ArchivedCable.Ports[0].Pins[0].SourcePinId);
        first.ArchivedCable.Ports[0].Name = "instance only";
        Assert.Equal("ETH", second.ArchivedCable.Ports[0].Name);
        Assert.Equal(before, JsonSerializer.Serialize(template));
    }

    [Fact]
    public async Task ExternalPinsConnectAndPersistWithoutFakeComponentOrInternalContinuity()
    {
        var cable = ArchivedCableInstanceFactory.Create(Template(), CableConstructionType.Custom);
        var p = new ElectricalProject { ProjectId = "test", Cables = [cable], Components = [new() {
            ComponentInstanceId = "device", ComponentDefinitionId = "device-def", TypeKey = "sensor",
            Ports = [new() { PortId = "device-port", Name = "P", Pins = [new() { PinId = "device-pin", PinNumber = "1" }] }] }] };
        var endpoint = cable.ArchivedCable!.Ports[1].Pins[0].PinId;
        var connection = new TopologyEndpointConnectionService().ConnectEndpoints(p, endpoint, "device-pin");
        Assert.Single(p.Components); Assert.Single(p.Cables); Assert.Single(p.Connections);
        Assert.Equal(ConnectionKind.Wire, connection.Kind);
        Assert.Null(connection.CableInstanceId);
        Assert.DoesNotContain(new ElectricalProjectValidator().Validate(p).Results, r => r.RuleId == "RULE-CONN-001");
        Assert.Contains(new ElectricalProjectValidator().Validate(p).Results, r => r.RuleId == "RULE-CABLE-MAPPING-001" && r.RequiresConfirmation);
        var path = Path.Combine(Path.GetTempPath(), "cable-template-test-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            var repo = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repo.SaveAsync(p); var loaded = (await repo.GetAsync(p.ProjectId))!;
            Assert.Equal("0.7", loaded.SchemaVersion);
            Assert.Equal(JsonSerializer.Serialize(p.Cables), JsonSerializer.Serialize(loaded.Cables));
            Assert.Equal(connection.FromEndpointId, Assert.Single(loaded.Connections).FromEndpointId);
        } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void MappingOverrideIsInstanceOnlyAndAlwaysPendingConfirmation()
    {
        var template = Template(true);
        var first = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        var second = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        var changed = ArchivedCableInstanceFactory.WithMappingOverride(first, [new("common-5", "b-1")]);
        Assert.False(changed.ArchivedCable!.MappingConfirmed);
        Assert.True(changed.ArchivedCable.HasMappingOverride);
        Assert.Equal("mapping-001", changed.ArchivedCable.Template.MappingRevision);
        Assert.True(first.ArchivedCable!.MappingConfirmed);
        Assert.True(second.ArchivedCable!.MappingConfirmed);
        Assert.Equal("a-1", Assert.Single(template.Mapping).ToSourcePinId);
        Assert.Throws<InvalidOperationException>(() => ArchivedCableInstanceFactory.WithMappingOverride(first, [new("common-5", "missing")]));
    }

    [Fact]
    public void UnknownPortCannotActAsConductivePinAndInvalidBindingCannotSave()
    {
        var cable = ArchivedCableInstanceFactory.Create(Template(), CableConstructionType.Custom);
        var p = new ElectricalProject { ProjectId = "draft", Cables = [cable] };
        var service = new TopologyEndpointConnectionService();
        Assert.False(service.IsKnownEndpoint(p, cable.ArchivedCable!.Ports[0].PortId));
        cable.ArchivedCable.Ports[0].Pins[0].SourcePinId = "invented";
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(p));
    }

    [Fact]
    public void Historical06MigratesWithoutInventingCableTemplates()
    {
        var old = new ElectricalProject { SchemaVersion = "0.6", ProjectId = "old", Cables = [new() {
            CableInstanceId = "legacy", CableDefinitionId = "catalog", ReferenceDesignator = "W1" }] };
        var migrated = ElectricalProjectMigrator.Migrate(old);
        Assert.Equal("0.7", migrated.SchemaVersion);
        Assert.Null(Assert.Single(migrated.Cables).ArchivedCable);
        Assert.Equal("W1", migrated.Cables[0].ReferenceDesignator);
        Assert.Empty(migrated.Connections);
        Assert.Null(migrated.Schematic);
    }

    [Fact]
    public void InvalidSourceMappingAndUnprovenConfirmationAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ArchivedCableInstanceFactory.Create(
            Template(true) with { MappingEvidence = null }, CableConstructionType.Custom));
        Assert.Throws<InvalidOperationException>(() => ArchivedCableInstanceFactory.Create(
            Template() with { Mapping = [new("common-5", "common-5")] }, CableConstructionType.Custom));
        Assert.Throws<InvalidOperationException>(() => ArchivedCableInstanceFactory.Create(
            Template() with { Mapping = [new("common-5", "a-1"), new("a-1", "common-5")] }, CableConstructionType.Custom));
    }

    [Fact]
    public async Task CableCadPlacementAndAnotherRepresentationShareOnePhysicalCable()
    {
        var service = new SchematicAuthoringService();
        var p = service.AddPage(new ElectricalProject { ProjectId = "drawing" }, "Cable");
        var page = p.Schematic!.Pages[0].PageId;
        var geometry = new SchematicCadAsset { SourceSha256 = new string('A', 64), Width = 80, Height = 40, MillimetresPerUnit = 1,
            Primitives = [new() { Kind = "LINE", Start = new(0, 20), End = new(80, 20) }],
            ConnectionPoints = [new("common", "", new(0, 20), Direction: "Left"), new("a", "", new(80, 10), Direction: "Right"), new("b", "", new(80, 30), Direction: "Right")] };
        var contacts = new Dictionary<string, string> { ["common-5"] = "common", ["a-1"] = "a", ["b-1"] = "b" };
        p = service.AddArchivedCable(p, Template(), CableConstructionType.Custom, geometry, contacts, page, new(20, 20));
        var cable = Assert.Single(p.Cables); var symbol = Assert.Single(p.Schematic!.Symbols);
        Assert.Empty(p.Components); Assert.Equal(cable.CableInstanceId, symbol.CableInstanceId);
        Assert.Equal("", symbol.ComponentInstanceId); Assert.Equal(3, symbol.Anchors.Count);
        Assert.Equal("common", symbol.Anchors[0].CadContactId);
        Assert.Equal(geometry.SourceSha256, symbol.Geometry!.SourceSha256);
        p = service.PlaceCableRepresentation(p, cable.CableInstanceId, geometry, contacts, page, new(150, 20));
        Assert.Single(p.Cables); Assert.Equal(2, p.Schematic!.Symbols.Count);
        var extra = service.PlaceExistingRepresentation(p, symbol.SymbolId, page, new(220, 80));
        Assert.Single(extra.Cables); Assert.Equal(3, extra.Schematic!.Symbols.Count);
        Assert.Equal(symbol.Anchors, extra.Schematic.Symbols[2].Anchors);
        Assert.NotEqual(symbol.SymbolId, extra.Schematic.Symbols[2].SymbolId);
        p = service.SetSymbolDetails(p, symbol.SymbolId, "W-Y1", symbol.Anchors);
        Assert.All(p.Schematic!.Symbols, s => Assert.Equal("W-Y1", SchematicSymbolOwner.Resolve(p, s).Reference));
        p = service.DrawWire(p, page, SchematicAttachment.Pin(symbol.SymbolId, symbol.Anchors[0].EndpointId), SchematicAttachment.Free(),
            [new(20, 40), new(10, 40), new(10, 70)]);
        p = service.TransformSymbol(p, symbol.SymbolId, new(40, 40), 90);
        Assert.Equal(SchematicAuthoringService.AnchorPoint(p.Schematic!.Symbols[0], symbol.Anchors[0].EndpointId), p.Schematic.Wires[0].Points[0]);
        Assert.Empty(p.Connections);
        Assert.Throws<InvalidOperationException>(() => service.DeleteRepresentation(p, symbol.SymbolId));
        p = service.DeleteRepresentation(p, p.Schematic.Symbols[1].SymbolId);
        Assert.Single(p.Cables); Assert.Single(p.Schematic!.Symbols);
        Assert.Contains(SchematicDraftReview.Inspect(p), i => i.Code == "UNCONFIRMED_CABLE_MAPPING");
        var path = Path.Combine(Path.GetTempPath(), "cable-canvas-test-" + Guid.NewGuid().ToString("N") + ".db");
        try {
            var repo = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repo.SaveAsync(p); var loaded = (await repo.GetAsync(p.ProjectId))!;
            Assert.Equal(JsonSerializer.Serialize(p.Schematic), JsonSerializer.Serialize(loaded.Schematic));
            Assert.Single(loaded.Cables); Assert.Empty(loaded.Components);
        } finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void CablePlacementRejectsWrongAssetWithoutMutatingProjectAndAllowsIncompleteDraftContacts()
    {
        var service = new SchematicAuthoringService();
        var p = service.AddPage(new ElectricalProject { ProjectId = "draft" }, "sheet");
        var before = JsonSerializer.Serialize(p); var page = p.Schematic!.Pages[0].PageId;
        var geometry = new SchematicCadAsset { SourceSha256 = new string('B', 64), Width = 30, Height = 20, MillimetresPerUnit = 1 };
        Assert.Throws<InvalidOperationException>(() => service.AddArchivedCable(p, Template(), CableConstructionType.Custom,
            geometry, new Dictionary<string, string>(), page, new(30, 30)));
        Assert.Equal(before, JsonSerializer.Serialize(p));
        var draft = service.AddArchivedCable(p, Template(), CableConstructionType.Custom,
            geometry with { SourceSha256 = new string('A', 64) }, new Dictionary<string, string>(), page, new(30, 30));
        Assert.Equal(3, SchematicDraftReview.Inspect(draft).Count(i => i.Code == "MISSING_CABLE_CONTACT"));
        Assert.Empty(draft.Schematic!.Symbols[0].Anchors);
        Assert.Empty(draft.Components); Assert.Empty(draft.Connections);
        Assert.Equal(before, JsonSerializer.Serialize(p));
        var symbol = draft.Schematic.Symbols[0];
        var withContacts = symbol with { Geometry = symbol.Geometry! with {
            ConnectionPoints = [new("CONTACT", "", new(0, 5), Direction: "Left")] } };
        draft.Schematic.Symbols[0] = withContacts;
        var bound = service.SetCableContactBindings(draft, symbol.SymbolId, new Dictionary<string, string> { ["common-5"] = "CONTACT" });
        Assert.Equal("common-5", Assert.Single(bound.Schematic!.Symbols[0].Anchors).SourcePinId);
        Assert.Equal("CONTACT", bound.Schematic.Symbols[0].Anchors[0].CadContactId);
        Assert.Equal(2, SchematicDraftReview.Inspect(bound).Count(i => i.Code == "MISSING_CABLE_CONTACT"));
        Assert.Empty(draft.Schematic.Symbols[0].Anchors);
        Assert.Throws<InvalidOperationException>(() => service.SetCableContactBindings(draft, symbol.SymbolId,
            new Dictionary<string, string> { ["unknown-pin"] = "CONTACT" }));
        draft.Schematic.Symbols[0] = symbol with { ComponentInstanceId = "fake" };
        Assert.Throws<InvalidOperationException>(() => SchematicAuthoringService.Validate(draft));
    }

    [Fact]
    public void CablePropertyDraftEditsOnlyOneInstanceAndPreservesUntouchedLengthProvenance()
    {
        var first = ArchivedCableInstanceFactory.Create(Template(true), CableConstructionType.Custom);
        var second = ArchivedCableInstanceFactory.Create(Template(true), CableConstructionType.Custom);
        first.ProvidedLengthMm = 1200; first.LengthSource = CableLengthSource.Imported;
        var p = new ElectricalProject { ProjectId = "edit", Cables = [first, second] };
        var before = JsonSerializer.Serialize(p); var service = new SchematicAuthoringService();
        var unchangedMapping = service.SetArchivedCableDetails(p, first.CableInstanceId, "W1", 1200, "AWG 22, 3 cores",
            CableConstructionType.Custom, first.ArchivedCable!.Mapping);
        Assert.Equal(CableLengthSource.Imported, unchangedMapping.Cables[0].LengthSource);
        Assert.True(unchangedMapping.Cables[0].ArchivedCable!.MappingConfirmed);
        var changed = service.SetArchivedCableDetails(p, first.CableInstanceId, "W2", 1300, "custom spec",
            CableConstructionType.Custom, [new("common-5", "b-1")]);
        Assert.Equal(CableLengthSource.User, changed.Cables[0].LengthSource);
        Assert.Equal("custom spec", changed.Cables[0].Specification);
        Assert.False(changed.Cables[0].ArchivedCable!.MappingConfirmed);
        Assert.Equal(JsonSerializer.Serialize(second), JsonSerializer.Serialize(changed.Cables[1]));
        Assert.Equal(before, JsonSerializer.Serialize(p));
        Assert.Throws<InvalidOperationException>(() => service.SetArchivedCableDetails(p, first.CableInstanceId, null, -1, null,
            CableConstructionType.Custom, first.ArchivedCable.Mapping));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }
}
