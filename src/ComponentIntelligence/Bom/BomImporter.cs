using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Bom;

public sealed record BomImportResult(
    IReadOnlyList<BomRow> Rows,
    IReadOnlyList<string> Errors);

public sealed class BomImporter
{
    private readonly BomRowReader _rowReader = new();

    public Task<BomImportResult> ImportAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? normalizedFilePath = null;
        try
        {
            using var workbook = OpenWorkbook(filePath, out normalizedFilePath);
            var worksheet = workbook.Worksheets
                .FirstOrDefault(sheet => string.Equals(sheet.Name, "BOM", StringComparison.OrdinalIgnoreCase));

            if (worksheet is null)
            {
                return Task.FromResult(new BomImportResult(
                    Array.Empty<BomRow>(),
                    ["A worksheet named 'BOM' was not found."]));
            }

            var rows = new List<BomRow>();
            var errors = new List<string>();
            var rowNumber = 1;

            foreach (var values in _rowReader.Read(worksheet))
            {
                var rawRow = values.ToDictionary(
                    pair => pair.Key,
                    pair => (string?)pair.Value.RawValue,
                    StringComparer.OrdinalIgnoreCase);

                var row = new BomRow
                {
                    RowId = rowNumber.ToString(CultureInfo.InvariantCulture),
                    RawManufacturer = GetRaw(values, BomHeaderMapper.Manufacturer),
                    RawModelOrPartNumber = GetRaw(values, BomHeaderMapper.ModelOrPartNumber),
                    Manufacturer = GetNormalized(values, BomHeaderMapper.Manufacturer),
                    ModelOrPartNumber = GetNormalized(values, BomHeaderMapper.ModelOrPartNumber),
                    UsedQuantity = GetInteger(values, BomHeaderMapper.UsedQuantity),
                    TotalQuantity = GetInteger(values, BomHeaderMapper.TotalQuantity),
                    Notes = GetNormalized(values, BomHeaderMapper.Notes),
                    ImportStatus = BomImportStatus.Imported,
                    ValidationFlags = Array.Empty<string>(),
                    RawRow = rawRow
                };

                var validationFlags = BomRowValidator.Validate(row);
                row = row with
                {
                    SpareQuantity = row.UsedQuantity is not null && row.TotalQuantity is not null && row.TotalQuantity >= row.UsedQuantity
                        ? row.TotalQuantity - row.UsedQuantity
                        : null,
                    ValidationFlags = validationFlags,
                    ImportStatus = validationFlags.Count == 0
                        ? BomImportStatus.Imported
                        : BomImportStatus.ImportedWithWarnings
                };

                if (validationFlags.Count > 0)
                    errors.Add($"Row {row.RowId}: {string.Join(", ", validationFlags)}");

                rows.Add(row);
                rowNumber++;
            }

            return Task.FromResult(new BomImportResult(rows, errors));
        }
        finally
        {
            if (normalizedFilePath is not null && File.Exists(normalizedFilePath))
                File.Delete(normalizedFilePath);
        }
    }

    private static XLWorkbook OpenWorkbook(string filePath, out string? normalizedFilePath)
    {
        normalizedFilePath = null;
        try
        {
            return new XLWorkbook(filePath);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("overlaps with the worksheet's autofilter", StringComparison.OrdinalIgnoreCase))
        {
            normalizedFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
            RemoveOverlappingWorksheetAutoFilters(filePath, normalizedFilePath);
            return new XLWorkbook(normalizedFilePath);
        }
    }

    private static void RemoveOverlappingWorksheetAutoFilters(string sourcePath, string destinationPath)
    {
        using var source = ZipFile.OpenRead(sourcePath);
        var worksheetPaths = source.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var destination = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        foreach (var entry in source.Entries)
        {
            var destinationEntry = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            using var input = entry.Open();
            using var output = destinationEntry.Open();

            if (worksheetPaths.Contains(entry.FullName))
            {
                var document = XDocument.Load(input);
                document.Root?.Descendants(MainNamespace + "autoFilter").Remove();
                document.Save(output);
            }
            else
            {
                input.CopyTo(output);
            }
        }
    }

    private static readonly XNamespace MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static string? GetRaw(
        IReadOnlyDictionary<string, (string RawValue, string NormalizedValue)> values,
        string header) => values.TryGetValue(header, out var value) ? value.RawValue : null;

    private static string? GetNormalized(
        IReadOnlyDictionary<string, (string RawValue, string NormalizedValue)> values,
        string header) => values.TryGetValue(header, out var value) && !string.IsNullOrEmpty(value.NormalizedValue)
            ? value.NormalizedValue
            : null;

    private static int? GetInteger(
        IReadOnlyDictionary<string, (string RawValue, string NormalizedValue)> values,
        string header) => values.TryGetValue(header, out var value) && int.TryParse(value.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
