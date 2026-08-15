using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Knowledge;

public sealed class ManualKnowledgeImportTests
{
    [Fact]
    public async Task TextKnowledgeImport_UpdatesLocalComponentIrWithoutOnlineSearch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ci-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "components.db");
        var knowledge = Path.Combine(root, "knowledge");
        var textFile = Path.Combine(root, "AL1342-spec.txt");
        await File.WriteAllTextAsync(textFile, "Operating voltage: 20 - 30 V DC\nCommunication interface: IO-Link\nConnector: M12 pins: 8 coding A");

        try
        {
            var service = new ManualKnowledgeImportService(database, knowledge);
            var result = await service.ImportAsync("ROW-1", "IFM", "AL1342", textFile);

            Assert.Equal(ManualKnowledgeImportStatus.ImportedToComponentIr, result.Status);
            Assert.True(result.ExtractedSpecificationCount >= 2);
            Assert.NotNull(result.Component);
            Assert.Equal("IFM", result.Component!.Identity.Manufacturer);
            Assert.Equal("AL1342", result.Component.Identity.Model);
            Assert.NotNull(result.Component.Power.OperatingVoltage);
            Assert.Equal("M12", result.Component.Connector.Family);

            var stored = await new SqliteComponentIrRepository(database).FindByIdentityAsync("IFM", "AL1342");
            Assert.NotNull(stored);
            Assert.Equal("M12", stored!.Connector.Family);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownIdentity_PreservesDocumentForLaterReviewInsteadOfGuessing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ci-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "components.db");
        var knowledge = Path.Combine(root, "knowledge");
        var textFile = Path.Combine(root, "unknown.txt");
        await File.WriteAllTextAsync(textFile, "Operating voltage: 24 - 24 V DC");

        try
        {
            var service = new ManualKnowledgeImportService(database, knowledge);
            var result = await service.ImportAsync("ROW-TBD", "TBD", "TBD (DEMO DEVICE)", textFile);

            Assert.Equal(ManualKnowledgeImportStatus.StoredForIdentityReview, result.Status);
            Assert.Null(result.Component);
            Assert.Contains("IDENTITY_REQUIRED_BEFORE_COMPONENT_IR", result.Issues);
            Assert.True(File.Exists(result.StoredPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
