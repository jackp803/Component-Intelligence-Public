using ComponentIntelligence.Contracts;
using ComponentIntelligence.Knowledge;
using ComponentIntelligence.Repository;
using Xunit;

namespace ComponentIntelligence.Tests.Knowledge;

public sealed class ManualKnowledgeMergeTests
{
    [Fact]
    public async Task ImportAsync_MergesSupplementalKnowledgeWithoutDroppingExistingSpecifications()
    {
        var root = Path.Combine(Path.GetTempPath(), $"component-intelligence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "components.db");
        var knowledge = Path.Combine(root, "knowledge");
        var file = Path.Combine(root, "supplement.txt");
        await File.WriteAllTextAsync(file, "Operating voltage: 18...30 V DC");

        try
        {
            var evidence = new Evidence
            {
                SourceType = ComponentSourceType.ManufacturerProductPage,
                SourceUrl = new Uri("https://example.com/product/TA2115"),
                ExtractionMethod = ExtractionMethod.TableParser,
                RawValue = "-50...150 °C",
                RetrievedAt = DateTimeOffset.UtcNow,
                VerificationStatus = VerificationStatus.SingleSource
            };
            var existing = new ComponentIR
            {
                Identity = new ComponentIrIdentity
                {
                    ComponentId = "CMP-TA2115",
                    Manufacturer = "IFM",
                    Model = "TA2115",
                    Mpn = "TA2115"
                },
                Specifications =
                [
                    new ComponentSpecification
                    {
                        Key = "sensing.measuring_range",
                        Name = "Measuring range",
                        Value = "-50...150 °C",
                        Evidence = [evidence]
                    }
                ],
                Assets = new ComponentAssets { ProductPageUrl = new Uri("https://example.com/product/TA2115") }
            };
            await new SqliteComponentIrRepository(database).SaveAsync(existing);

            var result = await new ManualKnowledgeImportService(database, knowledge)
                .ImportAsync("ROW-1", "IFM", "TA2115", file);

            Assert.NotNull(result.Component);
            Assert.Contains(result.Component!.Specifications, spec => spec.Key == "sensing.measuring_range" && spec.Value == "-50...150 °C");
            Assert.Contains(result.Component.Specifications, spec => spec.Key == "power.operating_voltage" && spec.Value!.Contains("18...30", StringComparison.Ordinal));
            Assert.Equal("18", result.Component.Power.OperatingVoltage?.Min?.ToString());
            Assert.Equal("30", result.Component.Power.OperatingVoltage?.Max?.ToString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
