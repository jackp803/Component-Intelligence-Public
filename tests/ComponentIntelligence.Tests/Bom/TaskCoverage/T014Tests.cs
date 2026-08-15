using ClosedXML.Excel;
using ComponentIntelligence.Bom;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T014Tests
{
    [Fact]
    public async Task ImportAsync_ImportsBomRows_PreservesRawValues_AndIgnoresBlankRows()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("bom");
                worksheet.Cell("A2").Value = "Manufacturer";
                worksheet.Cell("B2").Value = "Model / Part Number";
                worksheet.Cell("C2").Value = "Used Quantity";
                worksheet.Cell("D2").Value = "Total Quantity";
                worksheet.Cell("E2").Value = "Notes";
                worksheet.Cell("A3").Value = "  IFM  ";
                worksheet.Cell("B3").Value = " O5D100-A/1 ";
                worksheet.Cell("C3").Value = "4";
                worksheet.Cell("D3").Value = "5";
                worksheet.Cell("E3").Value = " sensor ";
                worksheet.Cell("A4").Value = " ";
                workbook.SaveAs(filePath);
            }

            var result = await new BomImporter().ImportAsync(filePath);

            var row = Assert.Single(result.Rows);
            Assert.Empty(result.Errors);
            Assert.Equal("IFM", row.Manufacturer);
            Assert.Equal("O5D100-A/1", row.ModelOrPartNumber);
            Assert.Equal(" O5D100-A/1 ", row.RawModelOrPartNumber);
            Assert.Equal(" O5D100-A/1 ", row.RawRow[BomHeaderMapper.ModelOrPartNumber]);
            Assert.Equal(4, row.UsedQuantity);
            Assert.Equal(5, row.TotalQuantity);
            Assert.Equal(1, row.SpareQuantity);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_ReturnsAnErrorWhenBomWorksheetIsMissing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                workbook.AddWorksheet("Parts");
                workbook.SaveAs(filePath);
            }

            var result = await new BomImporter().ImportAsync(filePath);

            Assert.Empty(result.Rows);
            Assert.Single(result.Errors);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}
