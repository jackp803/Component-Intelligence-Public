using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace ComponentIntelligence.SymbolArchive;

public sealed class SymbolArchiveRepository
{
    public const string SchemaVersion = "ci-symbol-archive.v1";
    public const string MultiRepresentationSchemaVersion = "ci-symbol-archive.v2";
    public const string CableTemplateSchemaVersion = "ci-symbol-archive.v3";
    public const string SchematicLayoutSchemaVersion = "ci-symbol-archive.v4";
    public const string CableManufacturingSchemaVersion = "ci-symbol-archive.v5";
    public const string FileName = "SymbolArchive.json";

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsDriveRootPattern = new("^[A-Za-z]:/", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

    public string? GetContentHash() => File.Exists(_path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_path))) : null;

    public void Save(SymbolArchiveDocument document) => SaveCore(document, null, false);

    public void SaveIfUnchanged(SymbolArchiveDocument document, string? expectedHash) => SaveCore(document, expectedHash, true);

    private void SaveCore(SymbolArchiveDocument document, string? expectedHash, bool compareOriginal)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = ValidateAndNormalize(document);
        Directory.CreateDirectory(_archiveRoot);
        using var guard = new FileStream(Path.Combine(_archiveRoot, ".SymbolArchive.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (compareOriginal && !string.Equals(expectedHash, GetContentHash(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive changed while editing; reload before saving.");
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
        if (document.SchemaVersion != SchemaVersion && document.SchemaVersion != MultiRepresentationSchemaVersion &&
            document.SchemaVersion != CableTemplateSchemaVersion && document.SchemaVersion != SchematicLayoutSchemaVersion &&
            document.SchemaVersion != CableManufacturingSchemaVersion)
            throw new InvalidDataException($"Unsupported Symbol Archive schema '{document.SchemaVersion}'.");

        var bindingKeys = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new List<ComponentSymbolBinding>();
        foreach (var binding in document.Bindings ?? [])
        {
            if (string.IsNullOrWhiteSpace(binding.ComponentId))
                throw new InvalidDataException("Every symbol binding requires ComponentId.");
            var componentId = binding.ComponentId.Trim();
            var representationId = NormalizeRepresentationId(binding.RepresentationId);
            var key = $"{componentId}\u001f{binding.Role}\u001f{representationId}";
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
                RepresentationId = representationId,
                Revisions = revisions.OrderBy(item => item.Revision, StringComparer.Ordinal).ToArray()
            });
        }

        var cableKeys = new HashSet<(string, string)>();
        var cables = (document.CableTemplates ?? []).Select(entry =>
        {
            Electrical.Schematic.ArchivedCableInstanceFactory.ValidateTemplate(entry.Template);
            if (!cableKeys.Add((entry.Template.TemplateId, entry.Template.TemplateRevision)))
                throw new InvalidDataException("Duplicate cable template revision.");
            if (!double.IsFinite(entry.MillimetresPerUnit) || entry.MillimetresPerUnit <= 0 || !Enum.IsDefined(entry.Status) || !Enum.IsDefined(entry.ConstructionType))
                throw new InvalidDataException("Cable template requires valid units, status and construction classification.");
            if (entry.ConstructionType != Electrical.Domain.CableConstructionType.Unknown && string.IsNullOrWhiteSpace(entry.ConstructionEvidence))
                throw new InvalidDataException("Cable construction classification requires explicit source evidence.");
            var pins = entry.Template.Ports.SelectMany(p => p.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
            var bindingsByPin = new HashSet<string>(StringComparer.Ordinal);
            var contacts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in entry.ContactBindings)
                if (!pins.Contains(binding.EngineeringEndpointId) || !bindingsByPin.Add(binding.EngineeringEndpointId) ||
                    string.IsNullOrWhiteSpace(binding.ConnectionPointId) || !contacts.Add(binding.ConnectionPointId))
                    throw new InvalidDataException("Cable CAD binding must identify unique exact source pins and contacts.");
            var manufacturing = entry.ManufacturingAsset;
            if (manufacturing is not null)
            {
                if (!double.IsFinite(manufacturing.MillimetresPerUnit) || manufacturing.MillimetresPerUnit <= 0)
                    throw new InvalidDataException("Manufacturing CAD requires finite positive units.");
                manufacturing = manufacturing with { Selection = ValidateSelection(manufacturing.Selection) };
                manufacturing = manufacturing with { AssetPath = NormalizeArchiveRelativePath(manufacturing.AssetPath),
                    SourceSha256 = NormalizeSha256(manufacturing.SourceSha256) };
                ValidateGeometry(manufacturing.Geometry, manufacturing.SourceSha256, manufacturing.MillimetresPerUnit, manufacturing.Selection);
            }
            var wiringSelection = ValidateSelection(entry.WiringSelection);
            if (!string.Equals(entry.Template.WiringSelectionSha256, wiringSelection?.GeometrySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cable template must pin the wiring selection identity.");
            ValidateGeometry(entry.WiringGeometry, entry.Template.AssetSha256, entry.MillimetresPerUnit, wiringSelection);
            return entry with { AssetPath = NormalizeArchiveRelativePath(entry.AssetPath), ManufacturingAsset = manufacturing, WiringSelection = wiringSelection };
        }).OrderBy(e => e.Template.TemplateId, StringComparer.Ordinal).ThenBy(e => e.Template.TemplateRevision, StringComparer.Ordinal).ToArray();

        var layoutKeys = new HashSet<(string ComponentId, string Revision)>();
        var activeLayouts = new HashSet<string>(StringComparer.Ordinal);
        var layouts = (document.SchematicLayouts ?? []).Select(layout =>
        {
            if (string.IsNullOrWhiteSpace(layout.ComponentId) || string.IsNullOrWhiteSpace(layout.Revision) ||
                !layoutKeys.Add((layout.ComponentId, layout.Revision)) ||
                layout.Active && !activeLayouts.Add(layout.ComponentId))
                throw new InvalidDataException("Schematic layout requires a unique component/revision and one active revision.");
            if (!double.IsFinite(layout.Width) || !double.IsFinite(layout.Height) || layout.Width < 10 || layout.Height < 10 ||
                layout.Rotation is not (0 or 90 or 180 or 270))
                throw new InvalidDataException("Schematic layout requires finite positive frame dimensions.");
            var pins = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pin in layout.Pins)
                if (string.IsNullOrWhiteSpace(pin.SourcePortId) || string.IsNullOrWhiteSpace(pin.SourcePinId) ||
                    !pins.Add(pin.SourcePinId) || !ValidSide(pin.Side) ||
                    !double.IsFinite(pin.Position.X) || !double.IsFinite(pin.Position.Y) ||
                    pin.Position.X < 0 || pin.Position.Y < 0 ||
                    pin.Position.X > layout.Width || pin.Position.Y > layout.Height ||
                    !(pin.Side switch
                    {
                        "Left" => Math.Abs(pin.Position.X) < .01,
                        "Right" => Math.Abs(pin.Position.X - layout.Width) < .01,
                        "Top" => Math.Abs(pin.Position.Y) < .01,
                        "Bottom" => Math.Abs(pin.Position.Y - layout.Height) < .01,
                        _ => false
                    }))
                    throw new InvalidDataException("Schematic layout Pin must have unique exact source identity and a frame-edge position.");
            var ports = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in layout.Ports)
                if (string.IsNullOrWhiteSpace(port.SourcePortId) || !ports.Add(port.SourcePortId) ||
                    !ValidSide(port.Side) || !double.IsFinite(port.Coordinate) || port.Coordinate < 0 ||
                    port.Coordinate > (port.Side is "Top" or "Bottom" ? layout.Width : layout.Height))
                    throw new InvalidDataException("Schematic layout Port position is invalid.");
            if (layout.CollapsedSourcePortIds.Distinct(StringComparer.Ordinal).Count() != layout.CollapsedSourcePortIds.Count ||
                layout.CollapsedSourcePortIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Schematic layout collapsed Port identities must be unique.");
            return layout;
        }).OrderBy(item => item.ComponentId, StringComparer.Ordinal)
          .ThenBy(item => item.Revision, StringComparer.Ordinal).ToArray();

        return document with
        {
            // Older readers must reject variant-bearing archives, never mistake a coil for the default symbol.
            SchemaVersion = document.SchemaVersion == CableManufacturingSchemaVersion || cables.Any(c => c.ManufacturingAsset is not null || c.WiringSelection is not null || c.WiringGeometry is not null)
                ? CableManufacturingSchemaVersion :
                document.SchemaVersion == SchematicLayoutSchemaVersion || layouts.Length > 0 ? SchematicLayoutSchemaVersion :
                document.SchemaVersion == CableTemplateSchemaVersion || cables.Length > 0 ? CableTemplateSchemaVersion :
                document.SchemaVersion == MultiRepresentationSchemaVersion || bindings.Any(b => b.RepresentationId != "default")
                ? MultiRepresentationSchemaVersion : SchemaVersion,
            CableTemplates = cables,
            SchematicLayouts = layouts,
            Bindings = bindings
                .OrderBy(item => item.ComponentId, StringComparer.Ordinal)
                .ThenBy(item => item.Role)
                .ThenBy(item => item.RepresentationId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static bool ValidSide(string side) => side is "Left" or "Right" or "Top" or "Bottom";

    private static CableCadSelection? ValidateSelection(CableCadSelection? selection)
    {
        if (selection is null) return null;
        if (selection.Bounds is { } bounds && (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || bounds.X < 0 || bounds.Y < 0 ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0) ||
            (selection.Bounds is null) == string.IsNullOrWhiteSpace(selection.BlockName))
            throw new InvalidDataException("CAD selection requires one valid region or named block.");
        return selection with { GeometrySha256 = NormalizeSha256(selection.GeometrySha256) };
    }

    private static void ValidateGeometry(Electrical.Schematic.SchematicCadAsset? geometry, string sourceHash, double units, CableCadSelection? selection)
    {
        if (geometry is null) return;
        if (!string.Equals(geometry.SourceSha256, sourceHash, StringComparison.OrdinalIgnoreCase) || geometry.MillimetresPerUnit != units ||
            !double.IsFinite(geometry.Width) || !double.IsFinite(geometry.Height) || geometry.Width <= 0 || geometry.Height <= 0 ||
            selection is not null && (!string.Equals(geometry.SelectionSha256, selection.GeometrySha256, StringComparison.OrdinalIgnoreCase) ||
                geometry.SelectionBounds != selection.Bounds || geometry.SelectionBlockName != selection.BlockName ||
                !string.Equals(Electrical.Schematic.SchematicCadSelectionService.GeometryHash(geometry), selection.GeometrySha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("CAD geometry snapshot does not match its source, selection or units.");
    }

    public string ResolveArchivePath(string assetPath)
    {
        var normalized = NormalizeArchiveRelativePath(assetPath);
        var candidate = Path.GetFullPath(Path.Combine(_archiveRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        AssertContained(candidate);
        return candidate;
    }

    public static string NormalizeRepresentationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, "^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("RepresentationId must be an explicit lowercase stable key (1-64 letters, digits, underscore or hyphen).");
        return value;
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
        if (WindowsDriveRootPattern.IsMatch(trimmed) || Path.IsPathRooted(trimmed) || trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
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
