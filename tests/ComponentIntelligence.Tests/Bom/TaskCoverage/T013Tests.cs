using ClosedXML.Excel;
using ComponentIntelligence.Bom;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T013Tests
{
    [Fact]
    public void Read_UsesFirstNonEmptyHeaderRow_TrimsValues_PreservesRawText_AndIgnoresBlankRows()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("BOM");
        worksheet.Cell("A3").Value = " Manufacturer ";
        worksheet.Cell("B3").Value = "Model / Part Number";
        worksheet.Cell("A4").Value = "  IFM  ";
        worksheet.Cell("B4").Value = " O5D100 ";
        worksheet.Cell("A5").Value = "   ";
        worksheet.Cell("B5").Value = " ";

        var rows = new BomRowReader().Read(worksheet);

        var row = Assert.Single(rows);
        Assert.Equal("  IFM  ", row["Manufacturer"].RawValue);
        Assert.Equal("IFM", row["Manufacturer"].NormalizedValue);
        Assert.Equal(" O5D100 ", row["Model / Part Number"].RawValue);
        Assert.Equal("O5D100", row["Model / Part Number"].NormalizedValue);
    }

    [Fact]
    public void Read_ReturnsNoRowsWhenWorksheetHasNoNonEmptyHeaderRow()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("BOM");

        var rows = new BomRowReader().Read(worksheet);

        Assert.Empty(rows);
    }
}
