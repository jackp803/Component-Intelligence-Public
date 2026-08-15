using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// Extracts engineering specifications from structured data embedded in product pages.
/// Handles schema.org JSON-LD, generic application/json blobs and HTML microdata.
/// The extractor is intentionally conservative: it keeps explicit name/value property objects
/// and scalar fields whose labels look engineering-relevant, while ignoring analytics/navigation noise.
/// </summary>
public sealed class StructuredSpecificationExtractor
{
    private static readonly Regex CamelCaseBoundary = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly string[] EngineeringTokens =
    [
        "voltage", "current", "power", "input", "output", "connector", "connection", "interface", "protocol",
        "communication", "temperature", "pressure", "range", "accuracy", "repeatability", "response", "dimension",
        "weight", "protection", "rating", "material", "mount", "housing", "media", "medium", "frequency", "speed",
        "baud", "ip", "nema", "io-link", "iolink", "ethernet", "ethercat", "modbus", "rs485", "rs-485", "pin", "contact"
    ];

    public IReadOnlyList<RawSpecification> ParseHtml(
        string html,
        Uri sourceUrl,
        ComponentSourceType sourceType = ComponentSourceType.ManufacturerProductPage)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(sourceUrl);

        var output = new List<RawSpecification>();
        var document = new HtmlParser().ParseDocument(html);

        foreach (var script in document.QuerySelectorAll("script"))
        {
            var type = script.GetAttribute("type")?.Trim();
            var id = script.GetAttribute("id")?.Trim();
            var text = script.TextContent?.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length > 8_000_000) continue;

            var method = string.Equals(type, "application/ld+json", StringComparison.OrdinalIgnoreCase)
                ? ExtractionMethod.JsonLd
                : ExtractionMethod.StructuredJson;
            var looksStructured = method == ExtractionMethod.JsonLd ||
                                  string.Equals(type, "application/json", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(id, "__NEXT_DATA__", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(id, "__NUXT_DATA__", StringComparison.OrdinalIgnoreCase);
            if (!looksStructured) continue;

            TryParseJson(text, sourceUrl, sourceType, method, output);
        }

        foreach (var element in document.QuerySelectorAll("[itemprop]"))
        {
            var rawName = element.GetAttribute("itemprop")?.Trim();
            var value = element.GetAttribute("content") ?? element.GetAttribute("value") ?? element.TextContent;
            AddScalar(output, "Microdata", rawName, value, sourceUrl, sourceType, ExtractionMethod.StructuredJson, allowRawEngineeringField: false);
        }

        return output
            .GroupBy(spec => $"{spec.Section}\u001f{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
            .ToArray();
    }

    private static void TryParseJson(
        string json,
        Uri sourceUrl,
        ComponentSourceType sourceType,
        ExtractionMethod method,
        List<RawSpecification> output)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 128
            });
            Walk(document.RootElement, "$", sourceUrl, sourceType, method, output, 0);
        }
        catch (JsonException)
        {
            // Some sites place JavaScript expressions inside script tags labelled as JSON.
            // Do not turn a malformed blob into a search failure; the HTML/table path can still succeed.
        }
    }

    private static void Walk(
        JsonElement element,
        string path,
        Uri sourceUrl,
        ComponentSourceType sourceType,
        ExtractionMethod method,
        List<RawSpecification> output,
        int depth)
    {
        if (depth > 32) return;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryReadNameValueObject(element, out var pairName, out var pairValue, out var pairUnit))
            {
                var value = string.IsNullOrWhiteSpace(pairUnit) ? pairValue : $"{pairValue} {pairUnit}";
                AddScalar(output, FriendlySection(path), pairName, value, sourceUrl, sourceType, method, allowRawEngineeringField: true);
            }

            foreach (var property in element.EnumerateObject())
            {
                var nextPath = path == "$" ? property.Name : $"{path}.{property.Name}";
                if (IsScalar(property.Value))
                {
                    var label = Humanize(property.Name);
                    AddScalar(output, FriendlySection(path), label, ScalarText(property.Value), sourceUrl, sourceType, method, allowRawEngineeringField: true);
                }
                else
                {
                    Walk(property.Value, nextPath, sourceUrl, sourceType, method, output, depth + 1);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, $"{path}[{index}]", sourceUrl, sourceType, method, output, depth + 1);
                index++;
                if (index >= 5000) break;
            }
        }
    }

    private static bool TryReadNameValueObject(JsonElement element, out string? name, out string? value, out string? unit)
    {
        name = ReadScalarProperty(element, "name") ?? ReadScalarProperty(element, "label") ?? ReadScalarProperty(element, "propertyID");
        value = ReadScalarProperty(element, "value") ?? ReadScalarProperty(element, "valueText");
        unit = ReadScalarProperty(element, "unitText") ?? ReadScalarProperty(element, "unitCode");
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value);
    }

    private static string? ReadScalarProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) || !IsScalar(property.Value)) continue;
            return ScalarText(property.Value);
        }
        return null;
    }

    private static void AddScalar(
        List<RawSpecification> output,
        string? section,
        string? rawName,
        string? rawValue,
        Uri sourceUrl,
        ComponentSourceType sourceType,
        ExtractionMethod method,
        bool allowRawEngineeringField)
    {
        var name = Clean(rawName);
        var value = Clean(rawValue);
        if (name.Length == 0 || value.Length == 0 || value.Length > 2000) return;

        var key = SpecificationDictionary.Map(section, name);
        if (key is null && (!allowRawEngineeringField || !LooksEngineeringRelevant(name, value))) return;

        var evidence = new Evidence
        {
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            ExtractionMethod = method,
            RawValue = value,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        output.Add(new RawSpecification
        {
            RawName = name,
            Section = string.IsNullOrWhiteSpace(section) ? "Structured data" : section,
            RawValue = value,
            ProposedKey = key,
            Status = VerificationStatus.SingleSource,
            Evidence = [evidence]
        });
    }

    private static bool LooksEngineeringRelevant(string name, string value)
    {
        var combined = $"{name} {value}".ToLowerInvariant();
        return EngineeringTokens.Any(token => combined.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string Humanize(string value)
    {
        var replaced = value.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        replaced = CamelCaseBoundary.Replace(replaced, " ");
        return Clean(replaced);
    }

    private static string FriendlySection(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$") return "Structured data";
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Regex.Replace(part, @"\[\d+\]", string.Empty))
            .Where(part => part != "$" && part.Length > 0)
            .TakeLast(3)
            .Select(Humanize)
            .ToArray();
        return parts.Length == 0 ? "Structured data" : $"Structured data / {string.Join(" / ", parts)}";
    }

    private static bool IsScalar(JsonElement element) => element.ValueKind is
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static string ScalarText(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? element.GetString() ?? string.Empty
        : element.GetRawText();

    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
