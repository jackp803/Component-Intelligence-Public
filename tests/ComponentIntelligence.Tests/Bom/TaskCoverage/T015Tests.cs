using ClosedXML.Excel;
using ComponentIntelligence.Bom;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T015Tests
{
    [Fact]
    public void Generate_CreatesStyledBomTemplate()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            new BomTemplateGenerator().Generate(filePath);

            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheet("BOM");

            Assert.Equal(BomHeaderMapper.Manufacturer, worksheet.Cell("A1").GetString());
            Assert.Equal(BomHeaderMapper.ModelOrPartNumber, worksheet.Cell("B1").GetString());
            Assert.Equal(BomHeaderMapper.UsedQuantity, worksheet.Cell("C1").GetString());
            Assert.Equal(BomHeaderMapper.TotalQuantity, worksheet.Cell("D1").GetString());
            Assert.Equal(BomHeaderMapper.Notes, worksheet.Cell("E1").GetString());
            Assert.True(worksheet.Cell("A1").Style.Font.Bold);
            Assert.Equal(XLColor.LightBlue.Color, worksheet.Cell("A1").Style.Fill.BackgroundColor.Color);
            Assert.True(worksheet.AutoFilter.IsEnabled);
            Assert.Equal(1, worksheet.SheetView.SplitRow);
            Assert.Equal(22d, worksheet.Column(1).Width);
            Assert.Equal(28d, worksheet.Column(2).Width);
            Assert.Equal(16d, worksheet.Column(3).Width);
            Assert.Equal(16d, worksheet.Column(4).Width);
            Assert.Equal(40d, worksheet.Column(5).Width);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}
