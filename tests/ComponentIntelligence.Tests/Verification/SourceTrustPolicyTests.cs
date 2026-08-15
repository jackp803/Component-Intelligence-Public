using ComponentIntelligence.Contracts;
using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Verification;

public sealed class SourceTrustPolicyTests
{
    [Fact]
    public async Task Normalizer_ChoosesManufacturerDatasheetOverEarlierDistributorValue()
    {
        var distributor = Evidence(ComponentSourceType.AuthorizedDistributor, "https://distributor.example/p/1", "20...30 V DC");
        var manufacturer = Evidence(ComponentSourceType.ManufacturerDatasheet, "https://manufacturer.example/ds.pdf", "18...30 V DC");
        var raw = new RawComponentProfile
        {
            Identity = new ComponentIdentity { OfficialManufacturer = "IFM", OfficialModel = "T1" },
            Specifications =
            [
                new RawSpecification { RawName = "Supply voltage", RawValue = "20...30 V DC", ProposedKey = "power.operating_voltage", Evidence = [distributor] },
                new RawSpecification { RawName = "Operating voltage", RawValue = "18...30 V DC", ProposedKey = "power.operating_voltage", Evidence = [manufacturer] }
            ]
        };

        var component = await new ComponentNormalizer().NormalizeAsync(raw);

        Assert.Equal(18m, component.Power.OperatingVoltage?.Min);
        Assert.Equal(30m, component.Power.OperatingVoltage?.Max);
        Assert.Equal("DC", component.Power.OperatingVoltage?.Type);
    }

    [Fact]
    public async Task ManualSupplement_DoesNotOverrideHigherTrustExistingManufacturerFact_AndReportsConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), $"component-intelligence-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "components.db");
        var supplement = Path.Combine(root, "supplement.txt");
        await File.WriteAllTextAsync(supplement, "Operating voltage: 20...30 V DC");

        try
        {
            var manufacturerEvidence = Evidence(ComponentSourceType.ManufacturerDatasheet, "https://manufacturer.example/ds.pdf", "18...30 V DC");
            var existing = new ComponentIR
            {
                Identity = new ComponentIrIdentity { ComponentId = "CMP-T2", Manufacturer = "IFM", Model = "T2", Mpn = "T2" },
                Power = new ComponentPower { OperatingVoltage = new NormalizedVoltage { Min = 18m, Max = 30m, Unit = "V", Type = "DC" } },
                Specifications =
                [
                    new ComponentSpecification
                    {
                        Key = "power.operating_voltage",
                        Name = "Operating voltage",
                        Value = "18...30 V DC",
                        Evidence = [manufacturerEvidence]
                    }
                ],
                Documents =
                [
                    new ComponentDocument
                    {
                        Type = "datasheet",
                        Url = new Uri("https://manufacturer.example/ds.pdf"),
                        SourceType = ComponentSourceType.ManufacturerDatasheet
                    }
                ]
            };
            await new SqliteComponentIrRepository(database).SaveAsync(existing);

            var result = await new ManualKnowledgeImportService(database, Path.Combine(root, "knowledge"))
                .ImportAsync("ROW-1", "IFM", "T2", supplement);

            Assert.NotNull(result.Component);
            Assert.Equal(18m, result.Component!.Power.OperatingVoltage?.Min);
            Assert.Equal(30m, result.Component.Power.OperatingVoltage?.Max);
            Assert.Contains(result.Issues, issue => issue.StartsWith("DATA_CONFLICT:power.operating_voltage:", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Evidence Evidence(ComponentSourceType sourceType, string url, string value) => new()
    {
        SourceType = sourceType,
        SourceUrl = new Uri(url),
        DocumentUrl = url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? new Uri(url) : null,
        ExtractionMethod = url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ExtractionMethod.PdfText : ExtractionMethod.TableParser,
        RawValue = value,
        RetrievedAt = DateTimeOffset.UtcNow,
        VerificationStatus = VerificationStatus.SingleSource
    };
}
