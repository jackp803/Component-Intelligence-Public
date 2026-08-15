using System.Globalization;

namespace ComponentIntelligence.Extraction;

/// <summary>
/// OCR word box normalized to [0,1] using the same visual top-left coordinate convention as
/// DiagramGeometry. Confidence is retained on Tesseract's 0-100 scale.
/// </summary>
public sealed record OcrTextBox(
    string Text,
    double Left,
    double Top,
    double Right,
    double Bottom,
    double Confidence)
{
    public DiagramPoint Center => new((Left + Right) / 2d, (Top + Bottom) / 2d);
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);
}

public sealed record TesseractTsvParseResult(
    IReadOnlyList<OcrTextBox> Boxes,
    int ImageWidth,
    int ImageHeight,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Parses Tesseract TSV level-5 word rows. TSV is deterministic local OCR metadata; no language
/// model or semantic inference is performed here.
/// </summary>
public static class TesseractTsvParser
{
    public static TesseractTsvParseResult Parse(string? tsv)
    {
        if (string.IsNullOrWhiteSpace(tsv))
            return new TesseractTsvParseResult([], 0, 0, ["OCR_TSV_EMPTY"]);

        var rows = new List<TsvRow>();
        foreach (var line in tsv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("level\t", StringComparison.OrdinalIgnoreCase)) continue;
            var cells = line.Split('\t');
            if (cells.Length < 12) continue;
            if (!TryInt(cells[0], out var level) ||
                !TryInt(cells[6], out var left) ||
                !TryInt(cells[7], out var top) ||
                !TryInt(cells[8], out var width) ||
                !TryInt(cells[9], out var height))
                continue;
            _ = double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence);
            var text = string.Join("\t", cells.Skip(11)).Trim();
            rows.Add(new TsvRow(level, left, top, width, height, confidence, text));
        }

        var pageRow = rows.FirstOrDefault(row => row.Level == 1 && row.Width > 0 && row.Height > 0);
        var imageWidth = pageRow?.Width ?? rows.Select(row => row.Left + row.Width).DefaultIfEmpty(0).Max();
        var imageHeight = pageRow?.Height ?? rows.Select(row => row.Top + row.Height).DefaultIfEmpty(0).Max();
        if (imageWidth <= 0 || imageHeight <= 0)
            return new TesseractTsvParseResult([], imageWidth, imageHeight, ["OCR_TSV_INVALID_PAGE_SIZE"]);

        var boxes = rows
            .Where(row => row.Level == 5 && row.Width > 0 && row.Height > 0 && !string.IsNullOrWhiteSpace(row.Text))
            .Select(row => new OcrTextBox(
                row.Text,
                Clamp01((double)row.Left / imageWidth),
                Clamp01((double)row.Top / imageHeight),
                Clamp01((double)(row.Left + row.Width) / imageWidth),
                Clamp01((double)(row.Top + row.Height) / imageHeight),
                row.Confidence))
            .Where(box => box.Width > 0 && box.Height > 0)
            .ToArray();

        return new TesseractTsvParseResult(
            boxes,
            imageWidth,
            imageHeight,
            [$"OCR_TSV_WORD_BOXES:{boxes.Length}", $"OCR_TSV_PAGE_SIZE:{imageWidth}x{imageHeight}"]);
    }

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private sealed record TsvRow(
        int Level,
        int Left,
        int Top,
        int Width,
        int Height,
        double Confidence,
        string Text);
}
