using ComponentIntelligence.Contracts;
using ComponentIntelligence.Enrichment;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Pipeline;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Sources;
using ComponentIntelligence.Sources.Ifm;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Mvp;

public sealed class MvpPipelineTests
{
    [Fact]
    public async Task O5D100_RunsEndToEnd_ThenKeepsEnrichingWhileTopologyKnowledgeIsIncomplete()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var database = Path.Combine(directory, "mvp.db");
        try
        {
            var repository = new SqliteComponentIrRepository(database);
            IComponentSource[] sources = [new IfmO5D100SeedSource()];
            var pipeline = new ComponentIntelligencePipeline(
                repository,
                new ComponentResolver(repository, sources),
                new ComponentEnricher(sources),
                new ComponentNormalizer(),
                new VerificationEngine());
            var row = new BomRow
            {
                RowId = "1",
                Manufacturer = "IFM",
                ModelOrPartNumber = "O5D100",
                RawManufacturer = "IFM",
                RawModelOrPartNumber = "O5D100",
                UsedQuantity = 4,
                TotalQuantity = 5,
                SpareQuantity = 1,
                ImportStatus = BomImportStatus.Imported
            };

            var first = await pipeline.ProcessAsync(row);
            Assert.False(first.LocalRepositoryHit);
            Assert.Equal(ResolutionStatus.Resolved, first.ResolutionStatus);
            Assert.NotNull(first.Component);
            Assert.Equal(10m, first.Component!.Power.OperatingVoltage!.Min);
            Assert.Equal(30m, first.Component.Power.OperatingVoltage.Max);
            Assert.Equal("DC", first.Component.Power.OperatingVoltage.Type);
            Assert.Equal("PNP", first.Component.Io.OutputType);
            Assert.Equal("M12", first.Component.Connector.Family);
            Assert.Equal("A", first.Component.Connector.Coding);
            Assert.Equal(4, first.Component.Connector.Pins);
            Assert.Equal(4, first.Component.Pins.Count);
            Assert.All(first.Component.Pins, pin => Assert.Null(pin.Function));
            Assert.Equal(VerificationStatus.SingleSource, first.Verification!.Status);
            Assert.Equal(ReadinessStatus.Partial, first.Component.Readiness.Topology);

            // A cached record is no longer enough by itself. Since every pin function is still Unknown,
            // the second run must continue enrichment rather than silently returning the cached copy.
            var second = await pipeline.ProcessAsync(row);
            Assert.False(second.LocalRepositoryHit);
            Assert.Equal(first.Component.Identity.ComponentId, second.Component!.Identity.ComponentId);
            Assert.Equal(ReadinessStatus.Partial, second.Component.Readiness.Topology);
            Assert.Contains("EXISTING_KNOWLEDGE_ENRICHMENT_ATTEMPTED", second.Issues);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MissingModel_ReturnsWaitingForInput_WithoutGuessing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new SqliteComponentIrRepository(Path.Combine(directory, "mvp.db"));
            IComponentSource[] sources = [new IfmO5D100SeedSource()];
            var pipeline = new ComponentIntelligencePipeline(
                repository,
                new ComponentResolver(repository, sources),
                new ComponentEnricher(sources),
                new ComponentNormalizer(),
                new VerificationEngine());
            var result = await pipeline.ProcessAsync(new BomRow
            {
                RowId = "1",
                Manufacturer = "IFM",
                RawManufacturer = "IFM",
                ImportStatus = BomImportStatus.ImportedWithWarnings,
                ValidationFlags = ["MISSING_MODEL"]
            });
            Assert.Equal(ResolutionStatus.WaitingForInput, result.ResolutionStatus);
            Assert.Null(result.Component);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
