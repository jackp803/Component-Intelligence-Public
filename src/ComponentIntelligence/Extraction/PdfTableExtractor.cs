using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace ComponentIntelligence.Extraction;

public sealed record PdfPositionedWord(string Text, double Left, double Right, double Bottom, double Top)
{
    public double CenterY => (Bottom + Top) / 2d;
    public double Height => Math.Abs(Top - Bottom);
}

public sealed record PdfTableRow(int PageNumber, string Label, string Value, IReadOnlyList<string> Cells);

/// <summary>
/// Deterministic table-like row reconstruction for digital PDFs.
/// It uses PdfPig word coordinates to group words into visual rows and split rows at meaningful
/// horizontal gaps. It intentionally returns only rows that look like engineering properties or
/// explicit pin/contact assignments, keeping false positives lower than a generic text scraper.
/// </summary>
public sealed class PdfTableExtractor
{
    private static readonly Regex HasNumber = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex NumericPinLabel = new(@"^(?:(?:pin|contact|terminal|pole)\s*(?:no\.?|number|#)?\s*)?\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnitLike = new(@"\b(?:V|mV|A|mA|W|kW|Hz|kHz|MHz|°C|C|bar|mbar|psi|MPa|Pa|mm|cm|m|g|kg|ms|s|Ω|ohm|kΩ|AWG|mm²|mm2|IP\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] ValueTokens =
    [
        "yes", "no", "true", "false", "pnp", "npn", "io-link", "iolink", "rs485", "rs-485", "ethernet",
        "ethercat", "profinet", "modbus", "m12", "m8", "rj45", "dc", "ac", "stainless", "plastic", "aluminium", "aluminum"
    ];
    private static readonly string[] PinFunctionTokens =
    [
        "l+", "l-", "c/q", "+24", "24v", "24 v", "0v", "0 v", "gnd", "sg", "pe", "fe", "shield",
        "rs485", "rs-485", "a+", "b-", "rx", "tx", "di", "do", "ai", "ao", "input", "output", "io-link", "iolink"
    ];

    public IReadOnlyList<PdfTableRow> Extract(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var rows = new List<PdfTableRow>();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new PdfPositionedWord(
                    Clean(word.Text),
                    word.BoundingBox.Left,
                    word.BoundingBox.Right,
                    word.BoundingBox.Bottom,
                    word.BoundingBox.Top))
                .Where(word => word.Text.Length > 0)
                .ToArray();
            rows.AddRange(InferRows(page.Number, words));
        }
        return rows;
    }

    public IReadOnlyList<PdfTableRow> InferRows(int pageNumber, IEnumerable<PdfPositionedWord> input)
    {
        var words = input.Where(word => word.Height > 0 && word.Right >= word.Left).ToArray();
        if (words.Length < 2) return Array.Empty<PdfTableRow>();

        var medianHeight = Median(words.Select(word => word.Height));
        var yTolerance = Math.Clamp(medianHeight * 0.45d, 1.5d, 8d);
        var visualRows = ClusterRows(words, yTolerance);
        var output = new List<PdfTableRow>();

        foreach (var visualRow in visualRows)
        {
            var ordered = visualRow.OrderBy(word => word.Left).ToArray();
            if (ordered.Length < 2 || ordered.Length > 80) continue;

            var gaps = Enumerable.Range(1, ordered.Length - 1)
                .Select(index => new { Index = index, Gap = ordered[index].Left - ordered[index - 1].Right })
                .Where(item => item.Gap > 0)
                .OrderByDescending(item => item.Gap)
                .ToArray();
            if (gaps.Length == 0) continue;

            var rowHeight = Median(ordered.Select(word => word.Height));
            var minimumGap = Math.Max(12d, rowHeight * 1.6d);
            var split = gaps.FirstOrDefault(item => item.Gap >= minimumGap);
            if (split is null) continue;

            var label = Clean(string.Join(' ', ordered.Take(split.Index).Select(word => word.Text)));
            var valueWords = ordered.Skip(split.Index).ToArray();
            var value = Clean(string.Join(' ', valueWords.Select(word => word.Text)));
            if (!LooksLikeEngineeringRow(label, value)) continue;

            var cells = BuildCells(ordered, minimumGap)
                .Select(cell => Clean(string.Join(' ', cell.Select(word => word.Text))))
                .Where(cell => cell.Length > 0)
                .ToArray();
            output.Add(new PdfTableRow(pageNumber, label, value, cells));
        }

        return output
            .GroupBy(row => $"{row.PageNumber}\u001f{row.Label}\u001f{row.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<PdfPositionedWord>> ClusterRows(IReadOnlyList<PdfPositionedWord> words, double tolerance)
    {
        var rows = new List<List<PdfPositionedWord>>();
        foreach (var word in words.OrderByDescending(word => word.CenterY).ThenBy(word => word.Left))
        {
            var target = rows.FirstOrDefault(row => Math.Abs(row.Average(item => item.CenterY) - word.CenterY) <= tolerance);
            if (target is null)
            {
                target = [];
                rows.Add(target);
            }
            target.Add(word);
        }
        return rows.Cast<IReadOnlyList<PdfPositionedWord>>().ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<PdfPositionedWord>> BuildCells(IReadOnlyList<PdfPositionedWord> ordered, double minimumGap)
    {
        var cells = new List<List<PdfPositionedWord>> { new() { ordered[0] } };
        for (var index = 1; index < ordered.Count; index++)
        {
            var gap = ordered[index].Left - ordered[index - 1].Right;
            if (gap >= minimumGap) cells.Add([]);
            cells[^1].Add(ordered[index]);
        }
        return cells.Cast<IReadOnlyList<PdfPositionedWord>>().ToArray();
    }

    private static bool LooksLikeEngineeringRow(string label, string value)
    {
        if (label.Length < 1 || label.Length > 140 || value.Length < 1 || value.Length > 800) return false;

        if (NumericPinLabel.IsMatch(label) && PinFunctionTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!label.Any(char.IsLetter)) return false;
        if (SpecificationDictionary.Map("PDF", label) is not null) return true;

        var labelWords = label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (labelWords > 14) return false;
        if (HasNumber.IsMatch(value) && (UnitLike.IsMatch(value) || value.Length <= 100)) return true;
        return ValueTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Where(value => !double.IsNaN(value) && !double.IsInfinity(value)).OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2d : ordered[middle];
    }

    private static string Clean(string? value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
