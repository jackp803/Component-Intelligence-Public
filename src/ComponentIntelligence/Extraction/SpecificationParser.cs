using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

public sealed class SpecificationParser
{
    private static readonly Regex MConnector = new(@"\bM(?<size>8|12)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Coding = new(@"cod(?:ing|ed)\s*[:=]?\s*(?<code>[A-Z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Contacts = new(@"(?:contacts?|pins?|poles?|接點|接点|腳位|脚位|針腳|针脚)\s*[:=：]?\s*(?<pins>\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumericPinLabel = new(@"^(?:(?:pin|contact|terminal|pole)\s*(?:no\.?|number|#)?\s*)?\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] PinFunctionTokens =
    [
        "l+", "l-", "c/q", "+24", "24v", "24 v", "0v", "0 v", "gnd", "sg", "pe", "fe", "shield",
        "rs485", "rs-485", "a+", "b-", "rx", "tx", "di", "do", "ai", "ao", "input", "output", "io-link", "iolink"
    ];
    private readonly StructuredSpecificationExtractor _structured = new();

    public IReadOnlyList<RawSpecification> ParseHtml(string html, Uri sourceUrl) =>
        ParseHtml(html, sourceUrl, ComponentSourceType.ManufacturerProductPage);

    public IReadOnlyList<RawSpecification> ParseHtml(
        string html,
        Uri sourceUrl,
        ComponentSourceType sourceType)
    {
        ArgumentNullException.ThrowIfNull(html);
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var specs = new List<RawSpecification>();
        var section = string.Empty;
        foreach (var element in document.All)
        {
            if (element.TagName is "H2" or "H3" or "H4")
            {
                section = Clean(element.TextContent);
                continue;
            }

            if (element.TagName == "TR")
            {
                var cells = element.QuerySelectorAll("th,td").Select(cell => Clean(cell.TextContent)).Where(value => value.Length > 0).ToArray();
                if (cells.Length >= 2)
                    AddPair(specs, section, cells[0], string.Join("; ", cells.Skip(1)), sourceUrl, null, null, sourceType, ExtractionMethod.TableParser);
            }
            else if (element.TagName == "DT")
            {
                var value = element.NextElementSibling?.TagName == "DD" ? Clean(element.NextElementSibling.TextContent) : string.Empty;
                if (value.Length > 0)
                    AddPair(specs, section, Clean(element.TextContent), value, sourceUrl, null, null, sourceType, ExtractionMethod.Html);
            }
        }

        specs.AddRange(_structured.ParseHtml(html, sourceUrl, sourceType));
        return Dedupe(specs);
    }

    public IReadOnlyList<RawSpecification> ParseText(
        string text,
        Uri documentUrl,
        int pageNumber,
        string documentHash,
        ComponentSourceType sourceType = ComponentSourceType.ManufacturerDatasheet,
        ExtractionMethod extractionMethod = ExtractionMethod.PdfText)
    {
        var specs = new List<RawSpecification>();
        var patterns = new (string Label, string Pattern)[]
        {
            ("Operating voltage", @"(?:Operating\s+voltage|Supply\s+voltage|工作電壓|工作电压|供電電壓|供电电压|電源電壓|电源电压)\s*(?:\[[^\]]+\])?\s*[:：|]?\s*(?<v>\d+(?:[.,]\d+)?\s*(?:\.\.\.|…|~|～|-)\s*\d+(?:[.,]\d+)?\s*(?:V\s*)?(?:AC|DC)?)"),
            ("Current consumption", @"(?:Current\s+consumption|Current\s+draw|電流消耗|电流消耗|消耗電流|消耗电流)\s*(?:\[[^\]]+\])?\s*[:：|]?\s*(?<v><?\s*\d+(?:[.,]\d+)?\s*mA[^\r\n]*)"),
            ("Electrical design", @"(?:Electrical\s+design|電氣設計|电气设计|輸出類型|输出类型)\s*[:：|]?\s*(?<v>PNP(?:/NPN)?|NPN(?:/PNP)?)"),
            ("Communication interface", @"(?:Communication\s+interface|通訊介面|通讯接口|通信介面|通信接口)\s*[:：|]?\s*(?<v>IO-?Link|RS-?485|Ethernet|PROFINET|EtherNet/IP|EtherCAT|Modbus(?:\s+TCP|\s+RTU)?|CAN(?:open)?)"),
            ("Connector", @"(?:Connector|Connection|接頭|接头|連接器|连接器|電氣連接|电气连接)\s*[:：|]?\s*(?<v>[^\r\n]{0,180}\bM(?:8|12)\b[^\r\n]{0,180})")
        };

        foreach (var (label, pattern) in patterns)
        {
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var value = Clean(match.Groups["v"].Value);
                if (value.Length > 0)
                    AddPair(specs, string.Empty, label, value, documentUrl, documentUrl, documentHash, sourceType, extractionMethod, pageNumber);
            }
        }

        // Precision-first generic line parsing: leverage the existing specification dictionary instead of
        // relying on a short list of regexes. Only mapped labels (or explicit electrical pin rows) are promoted,
        // which captures many datasheet fields without turning arbitrary prose into engineering facts.
        foreach (var (label, value) in ExtractMappedLinePairs(text))
            AddPair(specs, $"PDF text / page {pageNumber}", label, value, documentUrl, documentUrl, documentHash, sourceType, extractionMethod, pageNumber);

        return Dedupe(specs);
    }

    public IReadOnlyList<RawSpecification> ParseTableRows(
        IEnumerable<PdfTableRow> rows,
        Uri documentUrl,
        string documentHash,
        ComponentSourceType sourceType = ComponentSourceType.ManufacturerDatasheet)
    {
        var specs = new List<RawSpecification>();
        foreach (var row in rows)
        {
            AddPair(
                specs,
                $"PDF table / page {row.PageNumber}",
                row.Label,
                row.Value,
                documentUrl,
                documentUrl,
                documentHash,
                sourceType,
                ExtractionMethod.TableParser,
                row.PageNumber);
        }
        return Dedupe(specs);
    }

    internal static IReadOnlyList<(string Label, string Value)> ExtractMappedLinePairs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<(string, string)>();
        var output = new List<(string Label, string Value)>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length < 3 || line.Length > 1000) continue;

            foreach (var pair in CandidateSplits(line))
            {
                var label = Clean(pair.Label).Trim('-', '–', '—', '|', ';');
                // Do not trim dash characters from values. A leading '-' is engineering data for
                // negative limits/ranges (for example -25...80 °C), not presentation punctuation.
                var value = Clean(pair.Value).Trim('|', ';');
                if (label.Length is < 1 or > 160 || value.Length is < 1 or > 800) continue;

                var mapped = SpecificationDictionary.Map("PDF", label);
                var explicitPin = NumericPinLabel.IsMatch(label) &&
                                  PinFunctionTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (mapped is null && !explicitPin) continue;

                output.Add((label, value));
                break;
            }
        }

        return output
            .DistinctBy(pair => $"{pair.Label}\u001f{pair.Value}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Label, string Value)> CandidateSplits(string line)
    {
        var punctuation = line.IndexOfAny([':', '：']);
        if (punctuation > 0 && punctuation < line.Length - 1)
            yield return (line[..punctuation], line[(punctuation + 1)..]);

        var tab = line.IndexOf('\t');
        if (tab > 0 && tab < line.Length - 1)
            yield return (line[..tab], line[(tab + 1)..]);

        var whitespace = Regex.Match(line, @"\s{2,}");
        if (whitespace.Success && whitespace.Index > 0 && whitespace.Index + whitespace.Length < line.Length)
            yield return (line[..whitespace.Index], line[(whitespace.Index + whitespace.Length)..]);
    }

    private static void AddPair(
        List<RawSpecification> specs,
        string section,
        string label,
        string value,
        Uri sourceUrl,
        Uri? documentUrl,
        string? documentHash,
        ComponentSourceType sourceType,
        ExtractionMethod method,
        int? pageNumber = null)
    {
        var key = SpecificationDictionary.Map(section, label);
        var evidence = new Evidence
        {
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            DocumentUrl = documentUrl,
            DocumentHashSha256 = documentHash,
            PageNumber = pageNumber,
            ExtractionMethod = method,
            RawValue = value,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };

        specs.Add(new RawSpecification
        {
            RawName = label,
            Section = string.IsNullOrWhiteSpace(section) ? null : section,
            RawValue = value,
            ProposedKey = key,
            Status = VerificationStatus.SingleSource,
            Evidence = [evidence]
        });

        if (key == "connector.raw") ExpandConnector(specs, value, evidence, section);
    }

    private static void ExpandConnector(List<RawSpecification> specs, string value, Evidence evidence, string? section)
    {
        var family = MConnector.Match(value);
        if (family.Success) AddExpanded(specs, "Connector family", $"M{family.Groups["size"].Value}", "connector.family", evidence, section);
        var coding = Coding.Match(value);
        if (coding.Success) AddExpanded(specs, "Connector coding", coding.Groups["code"].Value.ToUpperInvariant(), "connector.coding", evidence, section);
        var contacts = Contacts.Match(value);
        if (contacts.Success) AddExpanded(specs, "Connector pin count", contacts.Groups["pins"].Value, "connector.pin_count", evidence, section);
    }

    private static void AddExpanded(List<RawSpecification> specs, string label, string value, string key, Evidence parent, string? section) => specs.Add(new RawSpecification
    {
        RawName = label,
        Section = string.IsNullOrWhiteSpace(section) ? null : section,
        RawValue = value,
        ProposedKey = key,
        Status = parent.VerificationStatus,
        Evidence = [parent with { RawValue = value }]
    });

    private static IReadOnlyList<RawSpecification> Dedupe(IEnumerable<RawSpecification> specs) => specs
        .GroupBy(spec => $"{spec.Section}\u001f{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}", StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
        .ToArray();

    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}