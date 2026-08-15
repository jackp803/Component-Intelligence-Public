using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class DocumentEvidenceQualityTests
{
    [Fact]
    public void IdentityCheck_ConfirmsExactTargetModelFromDocumentContent()
    {
        var result = new DocumentIdentityChecker().Check(
            Identity("IFM", "TA2115"),
            Document("https://media.ifm.com/generic-datasheet.pdf", ComponentSourceType.ManufacturerDatasheet),
            ["IFM electronic GmbH\nTemperature transmitter TA2115\nOperating voltage 18...32 V DC"]);

        Assert.Equal(DocumentIdentityStatus.Confirmed, result.Status);
        Assert.True(result.IsAccepted);
        Assert.Contains(result.Diagnostics, item => item.StartsWith("DOCUMENT_IDENTITY_CONFIRMED_CONTENT", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityCheck_RejectsDifferentSameFamilyModel()
    {
        var result = new DocumentIdentityChecker().Check(
            Identity("IFM", "TA2115"),
            Document("https://media.ifm.com/temperature-datasheet.pdf", ComponentSourceType.ManufacturerDatasheet),
            ["IFM temperature transmitter TA2114\nM12 connector"]);

        Assert.Equal(DocumentIdentityStatus.Mismatch, result.Status);
        Assert.False(result.IsAccepted);
        Assert.Contains(result.Diagnostics, item => item.Contains("EXPECTED=TA2115", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityCheck_AcceptsExactModelInReliableManufacturerPdfFilename()
    {
        var result = new DocumentIdentityChecker().Check(
            Identity("IFM", "TA2115"),
            Document("https://media.ifm.com/TA2115.pdf", ComponentSourceType.ManufacturerDatasheet),
            ["Technical data"]);

        Assert.Equal(DocumentIdentityStatus.Confirmed, result.Status);
        Assert.True(result.IsAccepted);
        Assert.Contains(result.Diagnostics, item => item.StartsWith("DOCUMENT_IDENTITY_CONFIRMED_METADATA", StringComparison.Ordinal));
    }

    [Fact]
    public void IdentityCheck_DoesNotTrustGenericWebFilenameWithoutContentConfirmation()
    {
        var result = new DocumentIdentityChecker().Check(
            Identity("IFM", "TA2115"),
            Document("https://example.invalid/TA2115.pdf", ComponentSourceType.GenericWeb),
            ["Technical data only"]);

        Assert.Equal(DocumentIdentityStatus.Unconfirmed, result.Status);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void Reconciliation_AgreementAcrossNativeAndTablePromotesVerifiedEvidence()
    {
        var reconciler = new CrossChannelSpecificationReconciler();
        var result = reconciler.Reconcile(
        [
            Spec("power.operating_voltage", "Operating voltage", "18...32 V DC", ExtractionMethod.PdfText),
            Spec("power.operating_voltage", "Operating voltage", "18...32 V DC", ExtractionMethod.TableParser)
        ]);

        var merged = Assert.Single(result.Specifications);
        Assert.Equal(VerificationStatus.Verified, merged.Status);
        Assert.Equal(2, merged.Evidence.Count);
        Assert.All(merged.Evidence, evidence => Assert.Equal(VerificationStatus.Verified, evidence.VerificationStatus));
        Assert.Contains(result.Diagnostics, item => item.StartsWith("CROSS_CHANNEL_AGREEMENT", StringComparison.Ordinal));
    }

    [Fact]
    public void Reconciliation_OcrConflictCannotOverrideNativePdfText()
    {
        var reconciler = new CrossChannelSpecificationReconciler();
        var result = reconciler.Reconcile(
        [
            Spec("power.operating_voltage", "Operating voltage", "24 V DC", ExtractionMethod.PdfText),
            Spec("power.operating_voltage", "Operating voltage", "240 V DC", ExtractionMethod.OcrText, VerificationStatus.Inferred)
        ]);

        Assert.Equal(2, result.Specifications.Count);
        Assert.All(result.Specifications, specification => Assert.Equal(VerificationStatus.Conflict, specification.Status));
        var best = SourceTrustPolicy.BestSpecification(result.Specifications, "power.operating_voltage");
        Assert.NotNull(best);
        Assert.Equal("24 V DC", best!.RawValue);
        Assert.Contains(result.Diagnostics, item => item.StartsWith("CROSS_CHANNEL_CONFLICT", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentIdentityContext_PushRestoresPreviousTarget()
    {
        Assert.Null(DocumentIdentityContext.Current);
        var outer = Identity("IFM", "TA2115");
        var inner = Identity("IFM", "O5D100");

        using (DocumentIdentityContext.Push(outer))
        {
            Assert.Equal("TA2115", DocumentIdentityContext.Current?.OfficialModel);
            using (DocumentIdentityContext.Push(inner))
                Assert.Equal("O5D100", DocumentIdentityContext.Current?.OfficialModel);
            Assert.Equal("TA2115", DocumentIdentityContext.Current?.OfficialModel);
        }

        Assert.Null(DocumentIdentityContext.Current);
    }

    private static ComponentIdentity Identity(string manufacturer, string model) => new()
    {
        OfficialManufacturer = manufacturer,
        OfficialModel = model,
        Mpn = model
    };

    private static ComponentDocument Document(string url, ComponentSourceType sourceType) => new()
    {
        Type = "datasheet",
        Url = new Uri(url),
        SourceType = sourceType
    };

    private static RawSpecification Spec(
        string key,
        string name,
        string value,
        ExtractionMethod method,
        VerificationStatus status = VerificationStatus.SingleSource) => new()
    {
        ProposedKey = key,
        RawName = name,
        RawValue = value,
        Status = status,
        Evidence =
        [
            new Evidence
            {
                SourceType = ComponentSourceType.ManufacturerDatasheet,
                DocumentUrl = new Uri("https://media.ifm.com/TA2115.pdf"),
                DocumentHashSha256 = "same-pdf-hash",
                PageNumber = 2,
                ExtractionMethod = method,
                RawValue = value,
                RetrievedAt = DateTimeOffset.UnixEpoch,
                VerificationStatus = status
            }
        ]
    };
}
