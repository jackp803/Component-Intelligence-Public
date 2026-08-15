using ClosedXML.Excel;

namespace ComponentIntelligence.Bom;

public sealed class BomRowReader
{
    public IReadOnlyList<IReadOnlyDictionary<string, (string RawValue, string NormalizedValue)>> Read(IXLWorksheet worksheet)
    {
        ArgumentNullException.ThrowIfNull(worksheet);

        var headerRow = worksheet.RowsUsed()
            .FirstOrDefault(row => row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString())));

        if (headerRow is null)
            return Array.Empty<IReadOnlyDictionary<string, (string RawValue, string NormalizedValue)>>();

        var headers = headerRow.CellsUsed()
            .Select(cell => (Column: cell.Address.ColumnNumber, Header: cell.GetString().Trim()))
            .Where(header => !string.IsNullOrWhiteSpace(header.Header))
            .ToArray();

        var rows = new List<IReadOnlyDictionary<string, (string RawValue, string NormalizedValue)>>();
        foreach (var row in worksheet.RowsUsed().Where(row => row.RowNumber() > headerRow.RowNumber()))
        {
            if (!row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString())))
                continue;

            var values = new Dictionary<string, (string RawValue, string NormalizedValue)>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                var rawValue = row.Cell(header.Column).GetString();
                values[header.Header] = (rawValue, rawValue.Trim());
            }

            rows.Add(values);
        }

        return rows;
    }
}
