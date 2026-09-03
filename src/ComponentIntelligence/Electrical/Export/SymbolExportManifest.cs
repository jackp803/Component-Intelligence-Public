using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Electrical.Export;

public sealed record SymbolExportManifestEntry
{
    public required string ComponentId { get; init; }
    public SymbolRole SymbolRole { get; init; }
    public SymbolSourceType SourceType { get; init; }
    public required string Revision { get; init; }
    public required string AssetPath { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record SymbolExportManifest
{
    public const string SchemaVersion = "ci-symbol-export-manifest.v1";
    public string Schema { get; init; } = SchemaVersion;
    public IReadOnlyList<SymbolExportManifestEntry> Symbols { get; init; } = [];

    public static SymbolExportManifest FromResolutions(IEnumerable<SymbolResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(resolutions);
        return new SymbolExportManifest
        {
            Symbols = resolutions
                .Select(resolution => new SymbolExportManifestEntry
                {
                    ComponentId = resolution.ComponentId,
                    SymbolRole = resolution.Role,
                    SourceType = resolution.SourceType,
                    Revision = resolution.Revision,
                    AssetPath = resolution.AssetPath,
                    Sha256 = SymbolArchiveRepository.NormalizeSha256(resolution.Sha256)
                })
                .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
                .ThenBy(item => item.SymbolRole)
                .ToArray()
        };
    }

    public string ToDeterministicJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(this, options);
    }
}
