using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;
using Port = ComponentIntelligence.Contracts.ComponentPort;
using Pin = ComponentIntelligence.Contracts.ComponentPin;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ComponentSourceIdentityRestorerTests
{
    internal static ComponentIR Catalog() => new()
    {
        Identity = new ComponentIrIdentity { ComponentId = "DEF", Manufacturer = "M", Model = "X" },
        Ports = [new Port { PortId = "P", PortName = "Display" }],
        Pins = [new Pin { PinId = "SOURCE-A", PortId = "P", PinNumber = "1" }, new Pin { PinId = "SOURCE-B", PortId = "P", PinNumber = "2" }]
    };

    [Fact]
    public void NewInstance_IsAlreadyTyped_AndUnchanged()
    {
        var source = Catalog(); var instance = new ComponentProjectBridge().CreateInstance(source, "I");
        var before = JsonSerializer.Serialize(instance);
        var result = new ComponentSourceIdentityRestorer().Restore(instance, source);
        Assert.Equal(SourceIdentityStatus.ALREADY_TYPED, result.Status);
        Assert.Equal(0, result.RestoredCount);
        Assert.Equal(before, JsonSerializer.Serialize(instance));
    }

    [Fact]
    public void Legacy_ExplicitPortAndExactPins_Restored_Idempotent_OnlyLineageChanges()
    {
        var source = Catalog(); var instance = new ComponentProjectBridge().CreateInstance(source, "I");
        var expected = JsonSerializer.Serialize(instance);
        instance.Ports[0].SourcePortId = null;
        foreach (var pin in instance.Ports[0].Pins) pin.SourcePinId = null;
        var restorer = new ComponentSourceIdentityRestorer();
        var audit = restorer.Analyze(instance, source);
        Assert.Null(instance.Ports[0].SourcePortId);
        Assert.Equal(3, audit.RestoredCount);
        var result = restorer.Restore(instance, source);
        Assert.Contains(result.Endpoints, e => e.Status == SourceIdentityStatus.EXPLICIT_LEGACY_RESTORABLE);
        Assert.Contains(result.Endpoints, e => e.Status == SourceIdentityStatus.DETERMINISTIC_EXACT_RESTORABLE);
        Assert.Equal(expected, JsonSerializer.Serialize(instance));
        Assert.Equal(0, restorer.Restore(instance, source).RestoredCount);
    }

    [Fact]
    public void FullEqualityOnly_NoNamesNumbersCaseOrSuffixFallback()
    {
        var source = Catalog(); var expected = new ComponentProjectBridge().CreateInstance(source, "I");
        var instance = new ComponentInstance { ComponentInstanceId = "I", ComponentDefinitionId = "DEF", TypeKey = "X", Ports =
        [new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = "prefix-" + expected.Ports[0].PortId, Name = "Display", Pins =
        [new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = expected.Ports[0].Pins[0].PinId.ToUpperInvariant(), PinNumber = "1", PinName = "SOURCE-A" }] }] };
        var result = new ComponentSourceIdentityRestorer().Restore(instance, source);
        Assert.Equal(SourceIdentityStatus.UNRESOLVED, result.Status);
        Assert.Equal(0, result.RestoredCount);
        Assert.All(result.Endpoints, e => Assert.Equal(SourceIdentityStatus.UNRESOLVED, e.Status));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    [InlineData("contradictory")]
    [InlineData("multiple-capabilities")]
    public void InvalidOrDuplicateAuthority_IsConflict_NoPartialMutation(string mode)
    {
        var source = Catalog(); var instance = new ComponentProjectBridge().CreateInstance(source, "I");
        instance.Ports[0].SourcePortId = null;
        if (mode == "invalid") instance.Ports[0].Pins[0].SourcePinId = "UNKNOWN";
        if (mode == "duplicate") instance.Ports[0].Pins[1].SourcePinId = "SOURCE-A";
        if (mode == "contradictory") instance.Ports[0].Pins[0].SourcePinId = "SOURCE-B";
        if (mode == "multiple-capabilities") instance.Ports[0].Capabilities.Add("SOURCE_PORT_ID:OTHER");
        var before = JsonSerializer.Serialize(instance);
        var result = new ComponentSourceIdentityRestorer().Restore(instance, source);
        Assert.Equal(SourceIdentityStatus.CONFLICT, result.Status);
        Assert.Equal(0, result.RestoredCount);
        Assert.Equal(before, JsonSerializer.Serialize(instance));
    }

    [Fact]
    public void MissingCatalog_IsUnresolved_NotInferred()
    {
        var instance = new ComponentProjectBridge().CreateInstance(Catalog(), "I");
        Assert.Equal(SourceIdentityStatus.UNRESOLVED, new ComponentSourceIdentityRestorer().Restore(instance, null).Status);
    }

    [Fact]
    public void K7lLegacyEndpointIds_RestoreAllEightExplicitCatalogPins()
    {
        const string definition = "OMRON_K7L-AT50DP";
        const string instanceId = "bom:7:omron:k7l-at50dp:1";
        var groups = new[] { (Port: "POWER", Pins: new[] { "1", "8" }), (Port: "SENSING", Pins: new[] { "2", "3", "4" }), (Port: "OUTPUT", Pins: new[] { "5", "6", "7" }) };
        var source = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = definition, Manufacturer = "OMRON", Model = "K7L-AT50DP" },
            Ports = groups.Select(g => new Port { PortId = definition + "_" + g.Port }).ToList(),
            Pins = groups.SelectMany(g => g.Pins.Select(n => new Pin { PinId = definition + "_" + g.Port + "_" + n, PortId = definition + "_" + g.Port, PinNumber = n })).ToList()
        };
        // Captured legacy ID convention; the service must compare full IDs, not parse this fixture.
        var instance = new ComponentInstance { ComponentInstanceId = instanceId, ComponentDefinitionId = definition, TypeKey = "TEST",
            Ports = groups.Select(g => new ComponentIntelligence.Electrical.Domain.ComponentPort
            {
                PortId = instanceId + ":port:omron-k7l-at50dp-" + g.Port.ToLowerInvariant(), Name = "irrelevant",
                Capabilities = ["SOURCE_PORT_ID:" + definition + "_" + g.Port],
                Pins = g.Pins.Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin
                {
                    PinId = instanceId + ":port:omron-k7l-at50dp-" + g.Port.ToLowerInvariant() + ":pin:omron_k7l-at50dp_" + g.Port.ToLowerInvariant() + "_" + n,
                    PinNumber = "DO-NOT-MATCH", PinName = "DO-NOT-MATCH"
                }).ToList()
            }).ToList() };
        var result = new ComponentSourceIdentityRestorer().Restore(instance, source);
        Assert.Equal(11, result.RestoredCount);
        Assert.Equal(source.Pins.Select(p => p.PinId).Order(), instance.Ports.SelectMany(p => p.Pins).Select(p => p.SourcePinId).Order());
    }

    [Fact]
    public async Task DisposableSaveReload_PreservesRestorationAndConnections()
    {
        var source = Catalog(); var instance = new ComponentProjectBridge().CreateInstance(source, "I");
        foreach (var p in instance.Ports) { p.SourcePortId = null; p.Capabilities.Clear(); foreach (var pin in p.Pins) pin.SourcePinId = null; }
        var project = new ElectricalProject { ProjectId = "T", Components = [instance], Connections = [new ElectricalConnection { ConnectionId = "C", FromEndpointId = instance.Ports[0].Pins[0].PinId, ToEndpointId = instance.Ports[0].Pins[1].PinId }] };
        var connections = JsonSerializer.Serialize(project.Connections);
        var result = new ComponentSourceIdentityRestorer().Restore(instance, source);
        Assert.Equal(3, result.RestoredCount);
        Assert.Equal(connections, JsonSerializer.Serialize(project.Connections));
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "test.db"));
            await repository.SaveAsync(project); var loaded = (await repository.GetAsync("T"))!;
            Assert.Equal(connections, JsonSerializer.Serialize(loaded.Connections));
            Assert.Equal(SourceIdentityStatus.ALREADY_TYPED, new ComponentSourceIdentityRestorer().Restore(loaded.Components[0], source).Status);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
