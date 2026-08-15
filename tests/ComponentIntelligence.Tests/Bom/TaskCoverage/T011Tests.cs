using ComponentIntelligence.Bom;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T011Tests
{
    [Fact]
    public void Validate_ReturnsEveryDefinedFlagForInvalidRow()
    {
        var row = new BomRow { RowId = "ROW-001", Manufacturer = " ", ModelOrPartNumber = null, UsedQuantity = -1, TotalQuantity = -2 };
        var flags = BomRowValidator.Validate(row);
        Assert.Equal([BomRowValidator.MissingManufacturer, BomRowValidator.MissingModel, BomRowValidator.InvalidUsedQuantity, BomRowValidator.InvalidTotalQuantity, BomRowValidator.TotalLessThanUsed], flags);
    }

    [Fact]
    public void Validate_ReturnsTotalLessThanUsedForOtherwiseValidQuantities()
    {
        var row = new BomRow { RowId = "ROW-002", Manufacturer = "ACME", ModelOrPartNumber = "AX-100", UsedQuantity = 5, TotalQuantity = 4 };
        Assert.Equal([BomRowValidator.TotalLessThanUsed], BomRowValidator.Validate(row));
    }

    [Fact]
    public void Validate_DoesNotRejectOrChangeImportedRow()
    {
        var row = new BomRow { RowId = "ROW-003", Manufacturer = null, ModelOrPartNumber = null, ImportStatus = BomImportStatus.Imported };
        var flags = BomRowValidator.Validate(row);
        Assert.Contains(BomRowValidator.MissingManufacturer, flags);
        Assert.Contains(BomRowValidator.MissingModel, flags);
        Assert.Equal(BomImportStatus.Imported, row.ImportStatus);
        Assert.Equal("ROW-003", row.RowId);
    }
}
