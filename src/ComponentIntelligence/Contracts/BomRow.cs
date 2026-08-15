namespace ComponentIntelligence.Contracts;

public sealed record BomRow
{
    public required string RowId { get; init; }
    public string? RawManufacturer { get; init; }
    public string? RawModelOrPartNumber { get; init; }
    public string? Manufacturer { get; init; }
    public string? ModelOrPartNumber { get; init; }
    public int? UsedQuantity { get; init; }
    public int? TotalQuantity { get; init; }
    public int? SpareQuantity { get; init; }
    public string? Notes { get; init; }
    public BomImportStatus ImportStatus { get; init; } = BomImportStatus.Imported;
    public IReadOnlyList<string> ValidationFlags { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string?> RawRow { get; init; } = new Dictionary<string, string?>();
}
