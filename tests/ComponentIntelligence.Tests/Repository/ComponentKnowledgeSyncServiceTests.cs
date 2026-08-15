using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Repository;

public sealed class ComponentKnowledgeSyncServiceTests
{
    [Fact]
    public async Task SaveAndSync_NoCentralToken_SavesLocalAndLeavesPending()
    {
        var path = TempDatabasePath();
        try
        {
            var central = new FakeKnowledgeStore { Enabled = false };
            var service = new ComponentKnowledgeSyncService(path, central);
            var component = BuildComponent("L+");

            var result = await service.SaveAndSyncAsync(component);

            Assert.True(result.LocalSaved);
            Assert.False(result.CentralAttempted);
            Assert.Equal(ComponentSyncStatus.Pending, result.Status);
            var local = await new SqliteComponentIrRepository(path).FindByIdentityAsync("IFM", "CUSTOM-001");
            Assert.NotNull(local);
            var state = await service.GetStateAsync("IFM", "CUSTOM-001");
            Assert.Equal(ComponentSyncStatus.Pending, state?.Status);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task SaveAndSync_VerifiedCentralPinConflict_DoesNotOverwriteCentral()
    {
        var path = TempDatabasePath();
        try
        {
            var central = new FakeKnowledgeStore
            {
                Enabled = true,
                Existing = BuildComponent("OUT1", VerificationStatus.Verified)
            };
            var service = new ComponentKnowledgeSyncService(path, central);
            var edited = BuildComponent("C/Q", VerificationStatus.UserConfirmed);

            var result = await service.SaveAndSyncAsync(edited);

            Assert.True(result.LocalSaved);
            Assert.True(result.CentralAttempted);
            Assert.False(result.CentralSucceeded);
            Assert.Equal(ComponentSyncStatus.Conflict, result.Status);
            Assert.Contains(result.Conflicts, value => value.StartsWith("PIN_VERIFIED_CONFLICT:4:", StringComparison.Ordinal));
            Assert.Equal(0, central.UpsertCount);
            var local = await new SqliteComponentIrRepository(path).FindByIdentityAsync("IFM", "CUSTOM-001");
            Assert.Equal("C/Q", local?.Pins.Single().Function);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task SaveAndSync_NoConflict_MarksSynced()
    {
        var path = TempDatabasePath();
        try
        {
            var central = new FakeKnowledgeStore
            {
                Enabled = true,
                Existing = BuildComponent("C/Q", VerificationStatus.Verified)
            };
            var service = new ComponentKnowledgeSyncService(path, central);
            var edited = BuildComponent("C/Q", VerificationStatus.UserConfirmed) with
            {
                Assets = new ComponentAssets { ImageUrl = new Uri("https://example.test/custom-001.png") }
            };

            var result = await service.SaveAndSyncAsync(edited);

            Assert.True(result.CentralSucceeded);
            Assert.Equal(ComponentSyncStatus.Synced, result.Status);
            Assert.Equal(1, central.UpsertCount);
            Assert.Equal("https://example.test/custom-001.png", central.LastWritten?.Assets.ImageUrl?.AbsoluteUri);
            var state = await service.GetStateAsync("IFM", "CUSTOM-001");
            Assert.Equal(ComponentSyncStatus.Synced, state?.Status);
            Assert.NotNull(state?.LastSuccessfulSyncAt);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static ComponentIR BuildComponent(string pinFunction, VerificationStatus status = VerificationStatus.UserConfirmed) =>
        new()
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "test:ifm:custom-001",
                Manufacturer = "IFM",
                Model = "CUSTOM-001",
                Mpn = "CUSTOM-001"
            },
            Connector = new ComponentConnector { Family = "M12", Coding = "A", Pins = 4 },
            Pins =
            [
                new ComponentPin
                {
                    PinNumber = "4",
                    Function = pinFunction,
                    Evidence =
                    [
                        new Evidence
                        {
                            SourceType = status == VerificationStatus.UserConfirmed ? ComponentSourceType.User : ComponentSourceType.ManufacturerDatasheet,
                            ExtractionMethod = status == VerificationStatus.UserConfirmed ? ExtractionMethod.UserInput : ExtractionMethod.PdfText,
                            RetrievedAt = DateTimeOffset.UtcNow,
                            VerificationStatus = status,
                            RawValue = pinFunction
                        }
                    ]
                }
            ],
            Specifications =
            [
                new ComponentSpecification
                {
                    Key = "operating_voltage",
                    Name = "Operating voltage",
                    Value = "24 V DC",
                    Status = status,
                    Evidence =
                    [
                        new Evidence
                        {
                            SourceType = status == VerificationStatus.UserConfirmed ? ComponentSourceType.User : ComponentSourceType.ManufacturerDatasheet,
                            ExtractionMethod = status == VerificationStatus.UserConfirmed ? ExtractionMethod.UserInput : ExtractionMethod.PdfText,
                            RetrievedAt = DateTimeOffset.UtcNow,
                            VerificationStatus = status,
                            RawValue = "24 V DC"
                        }
                    ]
                }
            ],
            Readiness = new ComponentReadiness { Topology = ReadinessStatus.Partial, Wiring = ReadinessStatus.Partial }
        };

    private static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"component-intelligence-sync-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed class FakeKnowledgeStore : IComponentKnowledgeStore
    {
        public bool Enabled { get; set; }
        public ComponentIR? Existing { get; set; }
        public int UpsertCount { get; private set; }
        public ComponentIR? LastWritten { get; private set; }
        public bool IsEnabled => Enabled;

        public Task<ComponentKnowledgeLookup> FindByIdentityAsync(string manufacturer, string model, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComponentKnowledgeLookup(Existing, Existing is null ? ["FAKE_MISS"] : ["FAKE_HIT"]));

        public Task<ComponentKnowledgeWriteResult> UpsertAsync(ComponentIR component, CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            LastWritten = component;
            Existing = component;
            return Task.FromResult(new ComponentKnowledgeWriteResult(true, ["FAKE_WRITE_OK"]));
        }
    }
}
