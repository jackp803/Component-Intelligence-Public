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
}
