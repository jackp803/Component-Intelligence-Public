namespace ComponentIntelligence.Bom;

public static class BomHeaderMapper
{
    public const string Manufacturer = "Manufacturer";
    public const string ModelOrPartNumber = "Model / Part Number";
    public const string UsedQuantity = "Used Quantity";
    public const string TotalQuantity = "Total Quantity";
    public const string Notes = "Notes";

    private static readonly IReadOnlyDictionary<string, string> Mappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Manufacturer] = Manufacturer,
            ["製造商"] = Manufacturer,
            [ModelOrPartNumber] = ModelOrPartNumber,
            ["型號 / 料號"] = ModelOrPartNumber,
            [UsedQuantity] = UsedQuantity,
            ["使用數量"] = UsedQuantity,
            [TotalQuantity] = TotalQuantity,
            ["總數"] = TotalQuantity,
            [Notes] = Notes,
            ["備註"] = Notes
        };

    public static bool TryMap(string? header, out string canonicalHeader)
    {
        canonicalHeader = string.Empty;
        if (string.IsNullOrWhiteSpace(header))
            return false;

        return Mappings.TryGetValue(header.Trim(), out canonicalHeader!);
    }
}
