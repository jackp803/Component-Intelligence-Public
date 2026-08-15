using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;
using ContractPin = ComponentIntelligence.Contracts.ComponentPin;
using ContractPort = ComponentIntelligence.Contracts.ComponentPort;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class VendorCustomPartKnowledgeTopologyEndToEndTests
{
    [Fact]
    public async Task CustomPart_CentralKnowledge_ReloadsIntoTopology_WithExplicitPinMapping()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ci-vendor-e2e-{Guid.NewGuid():N}.db");
        try
        {
            var central = new MemoryKnowledgeStore();
            var sync = new ComponentKnowledgeSyncService(databasePath, central);
            var custom = CustomPart();

            var syncResult = await sync.SaveAndSyncAsync(custom);
            Assert.Equal(ComponentSyncStatus.Synced, syncResult.Status);
            Assert.NotNull(central.Component);

            File.Delete(databasePath);
            DeleteSidecars(databasePath);
            var freshSync = new ComponentKnowledgeSyncService(databasePath, central);
            var reload = await freshSync.ReloadCentralAsync("VendorCo", "CAB-ADAPTER-001");
            Assert.NotNull(reload.Component);
            Assert.Equal("https://example.test/vendor/CAB-ADAPTER-001.png", reload.Component!.Assets.ImageUrl?.AbsoluteUri);

            var project = new ElectricalProject { ProjectId = "vendor-e2e" };
            var vendorInstance = new ComponentProjectBridge().CreateInstance(reload.Component, "vendor-1", "X1");
            var target = TargetDevice();
            project.Components.Add(vendorInstance);
            project.Components.Add(target);

            Assert.Single(vendorInstance.Ports);
            Assert.Equal(4, vendorInstance.Ports[0].Pins.Count);
            Assert.Equal("M12", vendorInstance.Ports[0].Connector?.Family);

            var connection = new TopologyConnectionEditor().ConnectPorts(project, vendorInstance.Ports[0].PortId, target.Ports[0].PortId);
            var mappingService = new ConnectionPinMappingService();
            mappingService.SetMappings(project, connection.ConnectionId,
            [
                new PinMappingEntry(vendorInstance.Ports[0].Pins.Single(pin => pin.PinNumber == "1").PinId, target.Ports[0].Pins.Single(pin => pin.PinNumber == "3").PinId, "brown", "+24V", ElectricalLayer.Power),
                new PinMappingEntry(vendorInstance.Ports[0].Pins.Single(pin => pin.PinNumber == "3").PinId, target.Ports[0].Pins.Single(pin => pin.PinNumber == "1").PinId, "blue", "0V", ElectricalLayer.Power),
                new PinMappingEntry(vendorInstance.Ports[0].Pins.Single(pin => pin.PinNumber == "4").PinId, target.Ports[0].Pins.Single(pin => pin.PinNumber == "4").PinId, "black", "C/Q", ElectricalLayer.Communication)
            ]);

            var mappings = mappingService.GetMappings(project, connection.ConnectionId);
            Assert.Equal(3, mappings.Count);
            Assert.DoesNotContain(mappings, item => item.FromPinId.EndsWith("pin:2", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch { }
            DeleteSidecars(databasePath);
        }
    }

    private static ComponentIR CustomPart() => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = "vendorco:cab-adapter-001",
            Manufacturer = "VendorCo",
            Model = "CAB-ADAPTER-001",
            Mpn = "CAB-ADAPTER-001"
        },
        Classification = new ComponentClassification { Category = "Cable Adapter", Subcategory = "Vendor Custom" },
        Connector = new ComponentConnector { Family = "M12", Coding = "A", Pins = 4 },
        Ports =
        [
            new ContractPort
            {
                PortId = "P1",
                PortType = "M12-A",
                ConnectorFamily = "M12",
                SignalType = "Mixed",
                Protocol = "IO-Link",
                VoltageDomain = "24 V DC"
            }
        ],
        Pins =
        [
            Pin("1", "+24V", "Power"),
            Pin("2", "Vendor Auxiliary", "Digital"),
            Pin("3", "0V", "Power"),
            Pin("4", "C/Q", "IO-Link")
        ],
        Assets = new ComponentAssets { ImageUrl = new Uri("https://example.test/vendor/CAB-ADAPTER-001.png") },
        Readiness = new ComponentReadiness { Topology = ReadinessStatus.Ready, Wiring = ReadinessStatus.Ready }
    };

    private static ContractPin Pin(string number, string function, string signal) => new()
    {
        PinNumber = number,
        Function = function,
        SignalType = signal,
        Evidence =
        [
            new Evidence
            {
                SourceType = ComponentSourceType.User,
                ExtractionMethod = ExtractionMethod.UserInput,
                RetrievedAt = DateTimeOffset.UtcNow,
                VerificationStatus = VerificationStatus.UserConfirmed,
                RawValue = $"Pin {number}: {function}"
            }
        ]
    };

    private static ComponentInstance TargetDevice()
    {
        var port = new ComponentIntelligence.Electrical.Domain.ComponentPort
        {
            PortId = "target:p1",
            Name = "P1",
            Protocol = "IO-Link",
            Connector = new ConnectorDefinition { ConnectorId = "target:p1:connector", Family = "M12", Coding = "A", PinCount = 4 }
        };
        foreach (var number in new[] { "1", "2", "3", "4" })
            port.Pins.Add(new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = $"target:p1:pin:{number}", PinNumber = number });
        return new ComponentInstance
        {
            ComponentInstanceId = "target",
            ComponentDefinitionId = "target-definition",
            TypeKey = "IO_LINK_DEVICE",
            ReferenceDesignator = "A1",
            Ports = { port }
        };
    }

    private static void DeleteSidecars(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed class MemoryKnowledgeStore : IComponentKnowledgeStore
    {
        public bool IsEnabled => true;
        public ComponentIR? Component { get; private set; }
        public Task<ComponentKnowledgeLookup> FindByIdentityAsync(string manufacturer, string model, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComponentKnowledgeLookup(
                Component is not null && string.Equals(Component.Identity.Manufacturer, manufacturer, StringComparison.OrdinalIgnoreCase) && string.Equals(Component.Identity.Model, model, StringComparison.OrdinalIgnoreCase) ? Component : null,
                [Component is null ? "MEMORY_MISS" : "MEMORY_HIT"]));
        public Task<ComponentKnowledgeWriteResult> UpsertAsync(ComponentIR component, CancellationToken cancellationToken = default)
        {
            Component = component;
            return Task.FromResult(new ComponentKnowledgeWriteResult(true, ["MEMORY_WRITE"]));
        }
    }
}
