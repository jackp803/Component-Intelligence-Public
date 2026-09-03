using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.SymbolArchive;

public sealed class SymbolArchiveRepository
{
    public const string SchemaVersion = "ci-symbol-archive.v1";
    public const string FileName = "SymbolArchive.json";

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string _archiveRoot;
    private readonly string _path;
    private readonly JsonSerializerOptions _json;

    public SymbolArchiveRepository(string centralArchiveRootOrWorkbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(centralArchiveRootOrWorkbookPath);
        var full = Path.GetFullPath(centralArchiveRootOrWorkbookPath.Trim());
        _archiveRoot = string.Equals(Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(full) ?? throw new InvalidOperationException("Workbook has no containing directory.")
            : full;
        _path = Path.Combine(_archiveRoot, FileName);
        _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        };
        _json.Converters.Add(new JsonStringEnumConverter());
    }

    public string ArchiveRoot => _archiveRoot;
    public string ArchivePath => _path;

    public SymbolArchiveDocument Load()
    {
        if (!File.Exists(_path)) return new SymbolArchiveDocument();
        SymbolArchiveDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SymbolArchiveDocument>(File.ReadAllText(_path), _json)
                ?? throw new InvalidDataException("SymbolArchive.json is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("SymbolArchive.json is malformed and cannot be used as authority.", exception);
        }
        return ValidateAndNormalize(document);
    }

    public void Save(SymbolArchiveDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = ValidateAndNormalize(document);
        Directory.CreateDirectory(_archiveRoot);
        var temp = Path.Combine(_archiveRoot, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, _json);
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public SymbolArchiveDocument ValidateAndNormalize(SymbolArchiveDocument document)
    {
        if (!string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported Symbol Archive schema '{document.SchemaVersion}'.");

        var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new List<ComponentSymbolBinding>();
        foreach (var binding in document.Bindings ?? [])
        {
            if (string.IsNullOrWhiteSpace(binding.ComponentId))
                throw new InvalidDataException("Every symbol binding requires ComponentId.");
            var componentId = binding.ComponentId.Trim();
            var key = $"{componentId}\u001f{binding.Role}";
            if (!bindingKeys.Add(key))
                throw new InvalidDataException($"Duplicate ComponentId + SymbolRole binding: {componentId} / {binding.Role}.");

            var revisionNames = new HashSet<string>(StringComparer.Ordinal);
            var approved = 0;
            var revisions = new List<SymbolRevisionRecord>();
            foreach (var revision in binding.Revisions ?? [])
            {
                if (string.IsNullOrWhiteSpace(revision.Revision) || !revisionNames.Add(revision.Revision.Trim()))
                    throw new InvalidDataException($"Duplicate or empty revision in {componentId} / {binding.Role}.");
                if (revision.Status == SymbolRevisionStatus.Approved) approved++;
                var relativePath = NormalizeArchiveRelativePath(revision.AssetPath);
                var sha = NormalizeSha256(revision.AssetHashSha256);
                var endpoints = new HashSet<string>(StringComparer.Ordinal);
                var portBindings = (revision.PortBindings ?? []).Select(portBinding =>
                {
                    if (string.IsNullOrWhiteSpace(portBinding.EngineeringEndpointId) ||
                        string.IsNullOrWhiteSpace(portBinding.ConnectionPointId))
                        throw new InvalidDataException("Port binding identities must be nonblank.");
                    var endpointId = portBinding.EngineeringEndpointId.Trim();
                    if (!endpoints.Add(endpointId))
                        throw new InvalidDataException($"Duplicate EngineeringEndpointId '{endpointId}' in {componentId} / {binding.Role} / {revision.Revision}.");
                    return portBinding with
                    {
                        EngineeringEndpointId = endpointId,
                        ConnectionPointId = portBinding.ConnectionPointId.Trim()
                    };
                }).OrderBy(item => item.EngineeringEndpointId, StringComparer.Ordinal).ToArray();

                revisions.Add(revision with
                {
                    Revision = revision.Revision.Trim(),
                    AssetPath = relativePath,
                    AssetHashSha256 = sha,
                    PortBindings = portBindings
                });
            }
            if (approved > 1)
                throw new InvalidDataException($"More than one Approved revision exists for {componentId} / {binding.Role}.");

            bindings.Add(binding with
            {
                ComponentId = componentId,
                Revisions = revisions.OrderBy(item => item.Revision, StringComparer.Ordinal).ToArray()
            });
        }

        return document with
        {
            SchemaVersion = SchemaVersion,
            Bindings = bindings
                .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
                .ThenBy(item => item.Role)
                .ToArray()
        };
    }

    public string ResolveArchivePath(string assetPath)
    {
        var normalized = NormalizeArchiveRelativePath(assetPath);
        var candidate = Path.GetFullPath(Path.Combine(_archiveRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        AssertContained(candidate);
        return candidate;
    }

    public static string NormalizeSha256(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Sha256Pattern.IsMatch(value.Trim()))
            throw new InvalidDataException("SHA-256 must contain exactly 64 hexadecimal characters.");
        return value.Trim().ToLowerInvariant();
    }

    private string NormalizeArchiveRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("AssetPath is required.");
        var trimmed = path.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
            throw new InvalidDataException("AssetPath must be archive-relative and may not escape the archive root.");
        var full = Path.GetFullPath(Path.Combine(_archiveRoot, trimmed.Replace('/', Path.DirectorySeparatorChar)));
        AssertContained(full);
        return Path.GetRelativePath(_archiveRoot, full).Replace('\\', '/');
    }

    private void AssertContained(string candidate)
    {
        var root = Path.GetFullPath(_archiveRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AssetPath escapes the archive root.");
    }
}
