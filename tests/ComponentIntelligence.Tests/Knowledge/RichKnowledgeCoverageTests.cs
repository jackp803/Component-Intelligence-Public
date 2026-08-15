using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Knowledge;

public sealed class RichKnowledgeCoverageTests
{
    [Fact]
    public void Html_parser_preserves_unmapped_manufacturer_rows()
    {
        var html = """
            <html><body>
              <h2>Application</h2>
              <table>
                <tr><td>Measuring range</td><td>-1...16 bar</td></tr>
                <tr><td>Process connection</td><td>G 1/2 external thread</td></tr>
                <tr><td>Special feature</td><td>Gold-plated contacts</td></tr>
                <tr><td>Vendor-only unusual field</td><td>Keep this evidence</td></tr>
              </table>
            </body></html>
            """;

        var specs = new SpecificationParser().ParseHtml(html, new Uri("https://www.ifm.com/us/en/product/TEST"));

        Assert.Equal(4, specs.Count);
        Assert.Contains(specs, spec => spec.ProposedKey == "sensing.measuring_range" && spec.RawValue == "-1...16 bar");
        Assert.Contains(specs, spec => spec.ProposedKey == "process.connection");
        var unmapped = Assert.Single(specs, spec => spec.RawName == "Vendor-only unusual field");
        Assert.Null(unmapped.ProposedKey);
        Assert.Equal("Application", unmapped.Section);
        Assert.NotEmpty(unmapped.Evidence);
    }

    [Fact]
    public async Task Rich_official_page_does_not_report_zero_completeness_when_wiring_fields_are_missing()
    {
        var source = new Uri("https://www.ifm.com/us/en/product/TEST");
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage,
            SourceUrl = source,
            ExtractionMethod = ExtractionMethod.TableParser,
            RawValue = "value",
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        var specs = Enumerable.Range(1, 24).Select(index => new RawSpecification
        {
            RawName = $"Field {index}",
            RawValue = $"Value {index}",
            ProposedKey = index <= 10 ? $"general.field_{index}" : null,
            Evidence = [evidence]
        }).ToArray();
        var raw = new RawComponentProfile
        {
            Identity = new ComponentIdentity
            {
                OfficialManufacturer = "IFM",
                OfficialModel = "TEST",
                OfficialProductUrl = source
            },
            Specifications = specs,
            Evidence = [evidence]
        };
        var component = await new ComponentNormalizer().NormalizeAsync(raw);
        var verification = await new VerificationEngine().VerifyAsync(component, raw);

        Assert.True(verification.Completeness >= 0.5m);
        Assert.Equal("Medium", verification.Confidence);
        Assert.Equal(ReadinessStatus.NotReady, verification.Readiness.Wiring);
    }

    [Fact]
    public async Task Manufacturer_datasheet_plus_product_page_can_reach_high_confidence()
    {
        var page = new Uri("https://www.ifm.com/us/en/product/TEST");
        var pdf = new Uri("https://www.ifm.com/download/TEST.pdf");
        var pageEvidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerProductPage,
            SourceUrl = page,
            ExtractionMethod = ExtractionMethod.TableParser,
            RawValue = "20...30 DC",
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        var pdfEvidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerDatasheet,
            SourceUrl = pdf,
            DocumentUrl = pdf,
            ExtractionMethod = ExtractionMethod.PdfText,
            RawValue = "20...30 DC",
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        var raw = new RawComponentProfile
        {
            Identity = new ComponentIdentity
            {
                OfficialManufacturer = "IFM",
                OfficialModel = "TEST",
                OfficialProductUrl = page
            },
            Specifications =
            [
                new RawSpecification
                {
                    RawName = "Operating voltage",
                    RawValue = "20...30 DC",
                    ProposedKey = "power.operating_voltage",
                    Evidence = [pageEvidence, pdfEvidence]
                }
            ],
            Documents =
            [
                new ComponentDocument { Type = "datasheet", Url = pdf, SourceType = ComponentSourceType.ManufacturerDatasheet }
            ],
            Evidence = [pageEvidence, pdfEvidence]
        };
        var component = await new ComponentNormalizer().NormalizeAsync(raw);
        var verification = await new VerificationEngine().VerifyAsync(component, raw);

        Assert.Equal(VerificationStatus.Verified, verification.Status);
        Assert.Equal("High", verification.Confidence);
    }
}
