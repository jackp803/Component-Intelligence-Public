using System.Text.Json;

namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingProjectMetadata
{
    public static DrawingProjectMetadata Empty { get; } = new();
    public string? ProjectName { get; init; }
    public string? EquipmentPartNumber { get; init; }
    public string? Checker { get; init; }
    public string? Drafter { get; init; }
    public string? Revision { get; init; }
    public IReadOnlyList<DrawingPageMetadata> Pages { get; init; } = [];
}

public sealed record DrawingPageMetadata
{
    public required string PageId { get; init; }
    public string? DrawingTitle { get; init; }
    public string? DrawingDocumentNumber { get; init; }
}

public sealed class DrawingProjectMetadataStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };
    private readonly string _path;

    public DrawingProjectMetadataStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComponentIntelligence", "drawing-project-metadata.json")
            : Path.GetFullPath(path);
    }

    public DrawingProjectMetadata Load(string projectId)
    {
        ValidateProjectId(projectId);
        if (!File.Exists(_path)) return DrawingProjectMetadata.Empty;
        var values = JsonSerializer.Deserialize<Dictionary<string, DrawingProjectMetadata>>(File.ReadAllText(_path), Json)
            ?? throw new InvalidDataException("Drawing project metadata store is empty.");
        return values.TryGetValue(projectId, out var value) ? Validate(value) : DrawingProjectMetadata.Empty;
    }

    public void Save(string projectId, DrawingProjectMetadata metadata)
    {
        ValidateProjectId(projectId);
        var normalized = Validate(metadata);
        var values = File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, DrawingProjectMetadata>>(File.ReadAllText(_path), Json) ?? []
            : [];
        values[projectId] = normalized;
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(values.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), Json));
    }

    private static DrawingProjectMetadata Validate(DrawingProjectMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var pages = metadata.Pages.OrderBy(x => x.PageId, StringComparer.Ordinal).ToArray();
        if (pages.Any(x => string.IsNullOrWhiteSpace(x.PageId)) || pages.Select(x => x.PageId).Distinct(StringComparer.Ordinal).Count() != pages.Length)
            throw new InvalidDataException("Drawing page metadata requires unique non-empty pageId values.");
        return metadata with { Pages = pages };
    }

    private static void ValidateProjectId(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("ProjectId is required.", nameof(projectId));
    }
}
