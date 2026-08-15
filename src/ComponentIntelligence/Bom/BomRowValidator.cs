using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Bom;

public static class BomRowValidator
{
    public const string MissingManufacturer = "MISSING_MANUFACTURER";
    public const string MissingModel = "MISSING_MODEL";
    public const string InvalidUsedQuantity = "INVALID_USED_QUANTITY";
    public const string InvalidTotalQuantity = "INVALID_TOTAL_QUANTITY";
    public const string TotalLessThanUsed = "TOTAL_LESS_THAN_USED";

    public static IReadOnlyList<string> Validate(BomRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var flags = new List<string>();
        if (string.IsNullOrWhiteSpace(row.Manufacturer)) flags.Add(MissingManufacturer);
        if (string.IsNullOrWhiteSpace(row.ModelOrPartNumber)) flags.Add(MissingModel);
        if (row.UsedQuantity is < 0) flags.Add(InvalidUsedQuantity);
        if (row.TotalQuantity is < 0) flags.Add(InvalidTotalQuantity);
        if (row.UsedQuantity is not null && row.TotalQuantity is not null && row.TotalQuantity < row.UsedQuantity) flags.Add(TotalLessThanUsed);
        return flags;
    }
}
