using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class PinoutExtractorTests
{
    [Fact]
    public void Extract_ConvertsExplicitPinAssignmentRows()
    {
        var evidence = new Evidence
        {
            SourceType = ComponentSourceType.ManufacturerDatasheet,
            SourceUrl = new Uri("https://example.com/datasheet.pdf"),
            DocumentUrl = new Uri("https://example.com/datasheet.pdf"),
            PageNumber = 3,
            ExtractionMethod = ExtractionMethod.TableParser,
            RawValue = "L+",
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        var specs = new[]
        {
            new RawSpecification { RawName = "1", Section = "Electrical connection / pin assignment", RawValue = "L+ 24 V DC", Evidence = [evidence] },
            new RawSpecification { RawName = "3", Section = "Electrical connection / pin assignment", RawValue = "L- 0 V", Evidence = [evidence with { RawValue = "L-" }] },
            new RawSpecification { RawName = "4", Section = "Electrical connection / pin assignment", RawValue = "C/Q IO-Link", Evidence = [evidence with { RawValue = "C/Q IO-Link" }] }
        };

        var pins = new PinoutExtractor().Extract(specs);

        Assert.Equal(3, pins.Count);
        Assert.Contains(pins, pin => pin.PinNumber == "1" && pin.SignalType == "Power" && pin.VoltageDomain == "24VDC");
        Assert.Contains(pins, pin => pin.PinNumber == "3" && pin.SignalType == "Power");
        Assert.Contains(pins, pin => pin.PinNumber == "4" && pin.SignalType == "Communication" && pin.Direction == "Bidirectional");
    }

    [Fact]
    public void Extract_DoesNotTreatArbitraryNumericTableRowsAsPins()
    {
        var specs = new[]
        {
            new RawSpecification { RawName = "1", Section = "Dimensions", RawValue = "32 mm" },
            new RawSpecification { RawName = "2", Section = "Dimensions", RawValue = "44 mm" }
        };

        Assert.Empty(new PinoutExtractor().Extract(specs));
    }

    [Fact]
    public void PdfTableExtractor_AllowsNumericRowsWhenValueLooksLikePinFunction()
    {
        var words = new[]
        {
            new PdfPositionedWord("1", 10, 15, 90, 100),
            new PdfPositionedWord("L+", 160, 172, 90, 100),
            new PdfPositionedWord("24", 177, 188, 90, 100),
            new PdfPositionedWord("V", 192, 198, 90, 100),
            new PdfPositionedWord("DC", 202, 216, 90, 100),
            new PdfPositionedWord("4", 10, 15, 70, 80),
            new PdfPositionedWord("C/Q", 160, 180, 70, 80),
            new PdfPositionedWord("IO-Link", 185, 220, 70, 80)
        };

        var rows = new PdfTableExtractor().InferRows(5, words);

        Assert.Contains(rows, row => row.Label == "1" && row.Value == "L+ 24 V DC");
        Assert.Contains(rows, row => row.Label == "4" && row.Value == "C/Q IO-Link");
    }
}
