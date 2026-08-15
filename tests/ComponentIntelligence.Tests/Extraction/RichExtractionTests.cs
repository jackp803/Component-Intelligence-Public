using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using Xunit;

namespace ComponentIntelligence.Tests.Extraction;

public sealed class RichExtractionTests
{
    [Fact]
    public void ParseHtml_ExtractsJsonLdAdditionalProperties()
    {
        var html = """
            <html><head>
              <script type="application/ld+json">
              {
                "@context": "https://schema.org",
                "@type": "Product",
                "name": "Pressure sensor",
                "additionalProperty": [
                  {"@type":"PropertyValue","name":"Operating voltage","value":"18...30 V DC"},
                  {"@type":"PropertyValue","name":"Measuring range","value":"-1...16 bar"},
                  {"@type":"PropertyValue","name":"Connector","value":"M12 A-coded, 4 pins"}
                ]
              }
              </script>
            </head><body></body></html>
            """;

        var specs = new SpecificationParser().ParseHtml(html, new Uri("https://manufacturer.example/product/P1"));

        Assert.Contains(specs, spec => spec.ProposedKey == "power.operating_voltage" && spec.RawValue == "18...30 V DC");
        Assert.Contains(specs, spec => spec.ProposedKey == "sensing.measuring_range" && spec.RawValue == "-1...16 bar");
        Assert.Contains(specs, spec => spec.ProposedKey == "connector.raw" && spec.RawValue.Contains("M12", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(specs.SelectMany(spec => spec.Evidence), evidence => evidence.ExtractionMethod == ExtractionMethod.JsonLd);
    }

    [Fact]
    public void ParseHtml_ExtractsNestedApplicationJsonNameValuePairs()
    {
        var html = """
            <script id="__NEXT_DATA__" type="application/json">
            {
              "props": {
                "product": {
                  "technicalData": [
                    {"label":"Pressure rating","value":"100 bar"},
                    {"label":"Ambient temperature","value":"-25...80 °C"}
                  ]
                }
              }
            }
            </script>
            """;

        var specs = new SpecificationParser().ParseHtml(html, new Uri("https://manufacturer.example/p/abc"));

        Assert.Contains(specs, spec => spec.ProposedKey == "sensing.pressure_rating" && spec.RawValue == "100 bar");
        Assert.Contains(specs, spec => spec.ProposedKey == "environment.operating_temperature" && spec.RawValue == "-25...80 °C");
        Assert.Contains(specs.SelectMany(spec => spec.Evidence), evidence => evidence.ExtractionMethod == ExtractionMethod.StructuredJson);
    }

    [Fact]
    public void PdfTableExtractor_ReconstructsEngineeringLabelValueRows()
    {
        var words = new[]
        {
            new PdfPositionedWord("Operating", 10, 52, 90, 100),
            new PdfPositionedWord("voltage", 57, 95, 90, 100),
            new PdfPositionedWord("18...30", 205, 245, 90, 100),
            new PdfPositionedWord("V", 250, 257, 90, 100),
            new PdfPositionedWord("DC", 262, 277, 90, 100),

            new PdfPositionedWord("Measuring", 10, 58, 70, 80),
            new PdfPositionedWord("range", 63, 91, 70, 80),
            new PdfPositionedWord("-1...16", 205, 242, 70, 80),
            new PdfPositionedWord("bar", 247, 265, 70, 80)
        };

        var rows = new PdfTableExtractor().InferRows(3, words);

        Assert.Contains(rows, row => row.PageNumber == 3 && row.Label == "Operating voltage" && row.Value == "18...30 V DC");
        Assert.Contains(rows, row => row.Label == "Measuring range" && row.Value == "-1...16 bar");
    }

    [Fact]
    public void ParseTableRows_PreservesPageEvidenceAndNormalizesKnownLabels()
    {
        var rows = new[] { new PdfTableRow(7, "Response time", "3 ms", ["Response time", "3 ms"]) };
        var specs = new SpecificationParser().ParseTableRows(rows, new Uri("https://example.com/datasheet.pdf"), "abc123");

        var spec = Assert.Single(specs.Where(item => item.ProposedKey == "sensing.response_time"));
        var evidence = Assert.Single(spec.Evidence);
        Assert.Equal(7, evidence.PageNumber);
        Assert.Equal(ExtractionMethod.TableParser, evidence.ExtractionMethod);
        Assert.Equal("abc123", evidence.DocumentHashSha256);
    }

    [Fact]
    public void ParseText_ExtractsDictionaryMappedColonAndSpacingRowsWithoutPromotingArbitraryProse()
    {
        var text = """
            Technical data
            Operating voltage: 18...30 V DC
            Ambient temperature    -25...80 °C
            Response time: 3 ms
            This sentence mentions 24 V but is ordinary descriptive prose and must not become a specification row.
            """;

        var specs = new SpecificationParser().ParseText(
            text,
            new Uri("https://manufacturer.example/P1.pdf"),
            4,
            "hash-1");

        Assert.Contains(specs, spec => spec.ProposedKey == "power.operating_voltage" && spec.RawValue == "18...30 V DC");
        Assert.Contains(specs, spec => spec.ProposedKey == "environment.operating_temperature" && spec.RawValue == "-25...80 °C");
        Assert.Contains(specs, spec => spec.ProposedKey == "sensing.response_time" && spec.RawValue == "3 ms");
        Assert.DoesNotContain(specs, spec => spec.RawName.StartsWith("This sentence", StringComparison.OrdinalIgnoreCase));
        Assert.All(specs.SelectMany(spec => spec.Evidence), evidence => Assert.Equal(4, evidence.PageNumber));
    }

    [Fact]
    public void ParseText_RecognizesExplicitPinRowsButDoesNotGuessNumericRowsWithoutElectricalMeaning()
    {
        var text = """
            Pin assignment
            1: L+ 24V
            2: OUT2 digital output
            3: L- 0V
            4: C/Q IO-Link
            5: 12345
            """;

        var specs = new SpecificationParser().ParseText(
            text,
            new Uri("https://manufacturer.example/P1.pdf"),
            6,
            "hash-2");

        Assert.Contains(specs, spec => spec.RawName == "1" && spec.RawValue == "L+ 24V");
        Assert.Contains(specs, spec => spec.RawName == "4" && spec.RawValue == "C/Q IO-Link");
        Assert.DoesNotContain(specs, spec => spec.RawName == "5" && spec.RawValue == "12345");
    }
}
