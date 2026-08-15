using ClosedXML.Excel;
using ComponentIntelligence.Bom;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.Bom;

public sealed class RealBomExampleTests
{
    [Fact]
    public async Task SyntheticPublicBom_ImportsAllRows_PreservesModels_AndCalculatesSpares()
    {
        var expected = new[]
        {
            ("IFM", "O5D100", 4, 5, 1, "Public demo photoelectric sensor"),
            ("SICK", "WTB4-3P2161", 2, 3, 1, "Public demo position sensor"),
            ("Siemens", "6EP1333-3BA10", 1, 2, 1, "Public demo 24V DC supply"),
            ("Omron", "E2E-X5ME1", 6, 8, 2, "Public demo proximity sensor"),
            ("IFM", "AL1100", 1, 1, 0, "Public demo IO-Link master"),
            ("Phoenix Contact", "2904600", 2, 2, 0, "Public demo relay module"),
            ("Weidmuller", "1020000000", 10, 12, 2, "Public demo terminal block"),
            ("Murr Elektronik", "7000-12221-6340300", 4, 6, 2, "Public demo M12 cable")
        };
        var filePath = Path.Combine(Path.GetTempPath(), $"component-intelligence-public-bom-{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("BOM");
                var headers = new[]
                {
                    BomHeaderMapper.Manufacturer,
                    BomHeaderMapper.ModelOrPartNumber,
                    BomHeaderMapper.UsedQuantity,
                    BomHeaderMapper.TotalQuantity,
                    BomHeaderMapper.Notes
                };
                for (var column = 0; column < headers.Length; column++)
                    worksheet.Cell(1, column + 1).Value = headers[column];

                for (var index = 0; index < expected.Length; index++)
                {
                    var item = expected[index];
                    var row = index + 2;
                    worksheet.Cell(row, 1).Value = item.Item1;
                    worksheet.Cell(row, 2).Value = item.Item2;
                    worksheet.Cell(row, 3).Value = item.Item3;
                    worksheet.Cell(row, 4).Value = item.Item4;
                    worksheet.Cell(row, 5).Value = item.Item6;
                }
                workbook.SaveAs(filePath);
            }

            var result = await new BomImporter().ImportAsync(filePath);

            Assert.Empty(result.Errors);
            Assert.Equal(expected.Length, result.Rows.Count);
            for (var index = 0; index < expected.Length; index++)
            {
                var row = result.Rows[index];
                var item = expected[index];
                Assert.Equal(BomImportStatus.Imported, row.ImportStatus);
                Assert.Empty(row.ValidationFlags);
                Assert.Equal(item.Item1, row.Manufacturer);
                Assert.Equal(item.Item2, row.ModelOrPartNumber);
                Assert.Equal(item.Item3, row.UsedQuantity);
                Assert.Equal(item.Item4, row.TotalQuantity);
                Assert.Equal(item.Item5, row.SpareQuantity);
                Assert.Equal(item.Item6, row.Notes);
                Assert.Equal(item.Item1, row.RawManufacturer);
                Assert.Equal(item.Item2, row.RawModelOrPartNumber);
            }
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }
}
