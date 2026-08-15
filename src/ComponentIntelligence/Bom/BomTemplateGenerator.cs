using ClosedXML.Excel;

namespace ComponentIntelligence.Bom;

public sealed class BomTemplateGenerator
{
    private static readonly string[] Headers =
    [
        BomHeaderMapper.Manufacturer,
        BomHeaderMapper.ModelOrPartNumber,
        BomHeaderMapper.UsedQuantity,
        BomHeaderMapper.TotalQuantity,
        BomHeaderMapper.Notes
    ];

    public void Generate(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("BOM");

        for (var column = 0; column < Headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = Headers[column];

        var headerRange = worksheet.Range(1, 1, 1, Headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
        headerRange.SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);

        worksheet.Column(1).Width = 22;
        worksheet.Column(2).Width = 28;
        worksheet.Column(3).Width = 16;
        worksheet.Column(4).Width = 16;
        worksheet.Column(5).Width = 40;

        if (File.Exists(filePath))
            File.Delete(filePath);

        workbook.SaveAs(filePath);
    }
}
