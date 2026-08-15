using ComponentIntelligence.Contracts;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Verification;

public sealed class ConflictVerificationTests
{
    [Fact]
    public async Task VerifyAsync_DetectsIndependentSourceConflict_OnCriticalField()
    {
        var identity = new ComponentIdentity
        {
            OfficialManufacturer = "IFM",
            OfficialModel = "TEST-1",
            Mpn = "TEST-1",
            OfficialProductUrl = new Uri("https://manufacturer.example/product/TEST-1")
        };
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = "CMP-1",
                Manufacturer = "IFM",
                Model = "TEST-1",
                Mpn = "TEST-1"
            }
        };
        var manufacturerEvidence = Evidence(ComponentSourceType.ManufacturerDatasheet, "https://manufacturer.example/datasheet.pdf", "18...30 V DC");
        var distributorEvidence = Evidence(ComponentSourceType.AuthorizedDistributor, "https://distributor.example/product/TEST-1", "20...30 V DC");
        var raw = new RawComponentProfile
        {
            Identity = identity,
            Specifications =
            [
                new RawSpecification
                {
                    RawName = "Operating voltage",
                    RawValue = "18...30 V DC",
                    ProposedKey = "power.operating_voltage",
                    Evidence = [manufacturerEvidence]
                },
                new RawSpecification
                {
                    RawName = "Supply voltage",
                    RawValue = "20...30 V DC",
                    ProposedKey = "power.operating_voltage",
                    Evidence = [distributorEvidence]
                }
            ],
            Evidence = [manufacturerEvidence, distributorEvidence]
        };

        var result = await new VerificationEngine().VerifyAsync(component, raw);

        Assert.Equal(VerificationStatus.Conflict, result.Status);
        Assert.Equal("Low", result.Confidence);
        Assert.Contains(result.Issues, issue => issue.StartsWith("DATA_CONFLICT:power.operating_voltage:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_DoesNotConflict_WhenIndependentSourcesAgree()
    {
        var identity = new ComponentIdentity { OfficialManufacturer = "IFM", OfficialModel = "TEST-2" };
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "CMP-2", Manufacturer = "IFM", Model = "TEST-2" }
        };
        var first = Evidence(ComponentSourceType.ManufacturerProductPage, "https://manufacturer.example/p/TEST-2", "M12");
        var second = Evidence(ComponentSourceType.AuthorizedDistributor, "https://distributor.example/p/TEST-2", "M12");
        var raw = new RawComponentProfile
        {
            Identity = identity,
            Specifications =
            [
                new RawSpecification { RawName = "Connector", RawValue = "M12", ProposedKey = "connector.family", Evidence = [first] },
                new RawSpecification { RawName = "Connector type", RawValue = "M12", ProposedKey = "connector.family", Evidence = [second] }
            ],
            Evidence = [first, second]
        };

        var result = await new VerificationEngine().VerifyAsync(component, raw);

        Assert.NotEqual(VerificationStatus.Conflict, result.Status);
        Assert.DoesNotContain(result.Issues, issue => issue.StartsWith("DATA_CONFLICT:", StringComparison.Ordinal));
    }

    private static Evidence Evidence(ComponentSourceType sourceType, string url, string value) => new()
    {
        SourceType = sourceType,
        SourceUrl = new Uri(url),
        ExtractionMethod = ExtractionMethod.TableParser,
        RawValue = value,
        RetrievedAt = DateTimeOffset.UtcNow,
        VerificationStatus = VerificationStatus.SingleSource
    };
}
