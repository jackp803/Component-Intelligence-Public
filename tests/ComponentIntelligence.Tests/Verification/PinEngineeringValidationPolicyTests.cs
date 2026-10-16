using ComponentIntelligence.Contracts;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Verification;

public sealed class PinEngineeringValidationPolicyTests
{
    [Fact]
    public void TrustedThirdPartySingleSourceTableParser_IsRejectedEvenWhenTextLooksElectrical()
    {
        var pin = Pin(
            "2",
            "24V POWER",
            ComponentSourceType.TrustedThirdParty,
            VerificationStatus.SingleSource,
            "Pin assignment extracted from: PDF table / page 2");

        Assert.False(PinEngineeringValidationPolicy.IsAccepted(pin));
    }

    [Fact]
    public void LegacyNotionRoundTrip_ThirdPartyPdfTable_IsStillRejected()
    {
        var pin = Pin(
            "13",
            "13 Adhesive tape",
            ComponentSourceType.TrustedThirdParty,
            VerificationStatus.SingleSource,
            "Pin assignment extracted from: PDF table / page 9",
            ExtractionMethod.UserInput);

        Assert.False(PinEngineeringValidationPolicy.IsAccepted(pin));
    }

    [Fact]
    public void ManufacturerTableParser_WithExplicitElectricalMeaning_IsAccepted()
    {
        var pin = Pin(
            "1",
            "L+ 24 V DC",
            ComponentSourceType.ManufacturerDatasheet,
            VerificationStatus.SingleSource,
            "Pin assignment extracted from: Electrical connection / pin assignment");

        Assert.True(PinEngineeringValidationPolicy.IsAccepted(pin));
    }

    [Fact]
    public void VerifiedThirdPartyPin_IsAccepted()
    {
        var pin = Pin(
            "3",
            "0V GND",
            ComponentSourceType.TrustedThirdParty,
            VerificationStatus.Verified,
            "Pin assignment extracted from: PDF table / page 3");

        Assert.True(PinEngineeringValidationPolicy.IsAccepted(pin));
    }

    [Fact]
    public void OrdinaryTableText_IsRejectedAsEngineeringPin()
    {
        var pin = Pin(
            "13",
            "13 Adhesive tape",
            ComponentSourceType.ManufacturerDatasheet,
            VerificationStatus.SingleSource,
            "Pin assignment extracted from: PDF table / page 9");

        Assert.False(PinEngineeringValidationPolicy.IsAccepted(pin));
    }

    private static ComponentPin Pin(
        string number,
        string function,
        ComponentSourceType sourceType,
        VerificationStatus verificationStatus,
        string description,
        ExtractionMethod extractionMethod = ExtractionMethod.TableParser) => new()
    {
        PinNumber = number,
        Function = function,
        Description = description,
        Evidence =
        [
            new Evidence
            {
                SourceType = sourceType,
                SourceUrl = new Uri("https://example.com/source.pdf"),
                DocumentUrl = new Uri("https://example.com/source.pdf"),
                ExtractionMethod = extractionMethod,
                RawValue = function,
                RetrievedAt = DateTimeOffset.UtcNow,
                VerificationStatus = verificationStatus
            }
        ]
    };
}
