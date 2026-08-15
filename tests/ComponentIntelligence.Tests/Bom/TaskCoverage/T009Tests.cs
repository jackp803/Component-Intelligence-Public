using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T009Tests
{
    [Fact]
    public void BomRow_StoresAllUserImportAndValidationFields()
    {
        var rawRow = new Dictionary<string, string?>
        {
            ["Manufacturer"] = " ACME ", ["Part Number"] = " AX-100 ", ["Used Qty"] = "4",
            ["Total Qty"] = "6", ["Spare Qty"] = "2"
        };
        var row = new BomRow
        {
            RowId = "ROW-001", RawManufacturer = " ACME ", RawModelOrPartNumber = " AX-100 ",
            Manufacturer = "ACME", ModelOrPartNumber = "AX-100", UsedQuantity = 4, TotalQuantity = 6, SpareQuantity = 2,
            Notes = "Install in panel A", ImportStatus = default,
            ValidationFlags = ["ManufacturerNormalized", "QuantityValidated"], RawRow = rawRow
        };

        Assert.Equal("ROW-001", row.RowId);
        Assert.Equal(" ACME ", row.RawManufacturer);
        Assert.Equal(" AX-100 ", row.RawModelOrPartNumber);
        Assert.Equal("ACME", row.Manufacturer);
        Assert.Equal("AX-100", row.ModelOrPartNumber);
        Assert.Equal(4, row.UsedQuantity);
        Assert.Equal(6, row.TotalQuantity);
        Assert.Equal(2, row.SpareQuantity);
        Assert.Equal("Install in panel A", row.Notes);
        Assert.Equal(default(BomImportStatus), row.ImportStatus);
        Assert.Equal(["ManufacturerNormalized", "QuantityValidated"], row.ValidationFlags);
        Assert.Same(rawRow, row.RawRow);
        Assert.Equal(" ACME ", row.RawRow["Manufacturer"]);
        Assert.Equal(" AX-100 ", row.RawRow["Part Number"]);
        Assert.Equal("4", row.RawRow["Used Qty"]);
        Assert.Equal("6", row.RawRow["Total Qty"]);
        Assert.Equal("2", row.RawRow["Spare Qty"]);
    }
}
