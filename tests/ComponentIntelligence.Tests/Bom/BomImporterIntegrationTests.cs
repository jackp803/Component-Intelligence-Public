using ClosedXML.Excel;
using ComponentIntelligence.Bom;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit;

namespace ComponentIntelligence.Tests.Bom;

public sealed class BomImporterIntegrationTests
{
    [Fact]
    public async Task ImportAsync_ImportsCompleteRowCalculatesSparePreservesRawAndIgnoresBlankRows()
    {
        var result = await ImportAsync(["  IFM  ", " O5D100 ", "4", "5", " sensor "], [" ", "", "", "", ""]);
        var row = Assert.Single(result.Rows);
        Assert.Empty(result.Errors);
        Assert.Equal("IFM", row.Manufacturer);
        Assert.Equal("O5D100", row.ModelOrPartNumber);
        Assert.Equal(4, row.UsedQuantity);
        Assert.Equal(5, row.TotalQuantity);
        Assert.Equal(1, row.SpareQuantity);
        Assert.Equal("  IFM  ", row.RawManufacturer);
        Assert.Equal(" O5D100 ", row.RawRow[BomHeaderMapper.ModelOrPartNumber]);
    }

    [Fact]
    public async Task ImportAsync_ReportsMissingManufacturer()
    {
        var row = Assert.Single((await ImportAsync(["", "O5D100", "4", "5", ""])).Rows);
        Assert.Contains(BomRowValidator.MissingManufacturer, row.ValidationFlags);
    }

    [Fact]
    public async Task ImportAsync_ReportsMissingModel()
    {
        var row = Assert.Single((await ImportAsync(["IFM", "", "4", "5", ""])).Rows);
        Assert.Contains(BomRowValidator.MissingModel, row.ValidationFlags);
    }

    [Fact]
    public async Task ImportAsync_HandlesInvalidQuantityWithoutCrashing()
    {
        var row = Assert.Single((await ImportAsync(["IFM", "O5D100", "not-a-number", "5", ""])).Rows);
        Assert.Null(row.UsedQuantity);
    }

    [Fact]
    public async Task ImportAsync_LeavesBlankNotesNullAndPreservesRawNotes()
    {
        var row = Assert.Single((await ImportAsync(["IFM", "O5D100", "4", "5", "   " ])).Rows);
        Assert.Null(row.Notes);
        Assert.Equal("   ", row.RawRow[BomHeaderMapper.Notes]);
    }

    [Fact]
    public async Task ImportAsync_ImportsOverlappingWorksheetAndTableAutoFiltersWithoutChangingSourceWorkbook()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("BOM");
                worksheet.Cell(1, 1).Value = BomHeaderMapper.Manufacturer;
                worksheet.Cell(1, 2).Value = BomHeaderMapper.ModelOrPartNumber;
                worksheet.Cell(1, 3).Value = BomHeaderMapper.UsedQuantity;
                worksheet.Cell(1, 4).Value = BomHeaderMapper.TotalQuantity;
                worksheet.Cell(1, 5).Value = BomHeaderMapper.Notes;
                worksheet.Cell(2, 1).Value = "IFM";
                worksheet.Cell(2, 2).Value = "O5D100";
                worksheet.Cell(2, 3).Value = "4";
                worksheet.Cell(2, 4).Value = "5";
                worksheet.Range("A1:E2").CreateTable("BOM");
                workbook.SaveAs(filePath);
            }

            AddWorksheetAutoFilter(filePath, "A1:E2");
            var originalHash = SHA256.HashData(File.ReadAllBytes(filePath));

            Assert.Throws<InvalidOperationException>(() => new XLWorkbook(filePath));

            var result = await new BomImporter().ImportAsync(filePath);

            var row = Assert.Single(result.Rows);
            Assert.Empty(result.Errors);
            Assert.Equal("IFM", row.Manufacturer);
            Assert.Equal("O5D100", row.ModelOrPartNumber);
            Assert.Equal(1, row.SpareQuantity);
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(filePath)));
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    private static void AddWorksheetAutoFilter(string filePath, string range)
    {
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument document;
        using (var stream = entry.Open())
            document = XDocument.Load(stream);

        entry.Delete();
        var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var output = replacement.Open();
        document.Root!.Add(new XElement(document.Root.Name.Namespace + "autoFilter", new XAttribute("ref", range)));
        document.Save(output);
    }

    private static async Task<BomImportResult> ImportAsync(params string[][] dataRows)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("BOM");
                worksheet.Cell(1, 1).Value = BomHeaderMapper.Manufacturer;
                worksheet.Cell(1, 2).Value = BomHeaderMapper.ModelOrPartNumber;
                worksheet.Cell(1, 3).Value = BomHeaderMapper.UsedQuantity;
                worksheet.Cell(1, 4).Value = BomHeaderMapper.TotalQuantity;
                worksheet.Cell(1, 5).Value = BomHeaderMapper.Notes;
                for (var rowIndex = 0; rowIndex < dataRows.Length; rowIndex++)
                    for (var columnIndex = 0; columnIndex < dataRows[rowIndex].Length; columnIndex++)
                        worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = dataRows[rowIndex][columnIndex];
                workbook.SaveAs(filePath);
            }
            return await new BomImporter().ImportAsync(filePath);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }
}
