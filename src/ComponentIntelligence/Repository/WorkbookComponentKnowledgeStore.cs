using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Repository;

/// <summary>
/// Read-only adapter for the zero-cost Component Intelligence central archive.
/// Components, Ports, and Pins are required engineering tables. PowerConversions is an optional,
/// backward-compatible explicit engineering-evidence table. PDF/image/drawing assets live beside
/// the workbook under Documents/&lt;Manufacturer&gt;/&lt;Model&gt;/ and are referenced by relative path.
/// Local SQLite remains the runtime/query cache and is not structurally modified by this adapter.
/// </summary>
public sealed class WorkbookComponentKnowledgeStore : IComponentKnowledgeStore
{
    private static readonly Regex VoltageRange = new(
        @"(?<min>\d+(?:[.,]\d+)?)\s*(?:\.\.\.|…|-|to)\s*(?<max>\d+(?:[.,]\d+)?)\s*V\s*(?<type>DC|AC)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VoltageSingle = new(
        @"(?<value>\d+(?:[.,]\d+)?)\s*V\s*(?<type>DC|AC)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _workbookPath;
    private readonly string _workbookRoot;

    public WorkbookComponentKnowledgeStore(string workbookPath)
    {
        _workbookPath = string.IsNullOrWhiteSpace(workbookPath) ? string.Empty : Path.GetFullPath(workbookPath.Trim());
        _workbookRoot = string.IsNullOrWhiteSpace(_workbookPath)
            ? string.Empty
            : Path.GetDirectoryName(_workbookPath) ?? string.Empty;
    }

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_workbookPath) &&
        File.Exists(_workbookPath) &&
        string.Equals(Path.GetExtension(_workbookPath), ".xlsx", StringComparison.OrdinalIgnoreCase);

    public Task<ComponentKnowledgeLookup> FindByIdentityAsync(
        string manufacturer,
        string model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled)
            return Task.FromResult(new ComponentKnowledgeLookup(null,
                ["CENTRAL_WORKBOOK_DISABLED_OR_MISSING", _workbookPath]));

        try
        {
            using var workbook = new XLWorkbook(_workbookPath);
            if (!workbook.Worksheets.TryGetWorksheet("Components", out var componentsSheet) ||
                !workbook.Worksheets.TryGetWorksheet("Ports", out var portsSheet) ||
                !workbook.Worksheets.TryGetWorksheet("Pins", out var pinsSheet))
            {
                return Task.FromResult(new ComponentKnowledgeLookup(null,
                    ["CENTRAL_WORKBOOK_SCHEMA_INVALID", "REQUIRED_SHEETS:Components,Ports,Pins"]));
            }

            var components = ReadRows(componentsSheet);
            var ports = ReadRows(portsSheet);
            var pins = ReadRows(pinsSheet);
            var powerConversions = workbook.Worksheets.TryGetWorksheet("PowerConversions", out var powerConversionsSheet)
                ? ReadRows(powerConversionsSheet)
                : Array.Empty<IReadOnlyDictionary<string, string>>();

            var targetManufacturer = NormalizeManufacturer(manufacturer);
            var targetModel = NormalizeModel(model);
            var componentRow = components.FirstOrDefault(row =>
                string.Equals(NormalizeManufacturer(Get(row, "Manufacturer")), targetManufacturer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeModel(Get(row, "Model")), targetModel, StringComparison.OrdinalIgnoreCase));

            if (componentRow is null)
                return Task.FromResult(new ComponentKnowledgeLookup(null,
                    ["CENTRAL_WORKBOOK_COMPONENT_NOT_FOUND"]));

            var component = BuildComponent(componentRow, ports, pins, powerConversions);
            var diagnostics = new List<string>
            {
                "CENTRAL_WORKBOOK_COMPONENT_FOUND",
                "CENTRAL_WORKBOOK_READ_ONLY"
            };
            diagnostics.AddRange(BuildUnresolvedConversionDiagnostics(components, powerConversions));
            return Task.FromResult(new ComponentKnowledgeLookup(component, diagnostics));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(new ComponentKnowledgeLookup(null,
                [$"CENTRAL_WORKBOOK_READ_FAILED:{exception.GetType().Name}:{exception.Message}"]));
        }
    }

    public Task<ComponentKnowledgeWriteResult> UpsertAsync(
        ComponentIR component,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ComponentKnowledgeWriteResult(false,
            ["CENTRAL_WORKBOOK_READ_ONLY", "GPT_ARCHIVE_WORKFLOW_OWNS_WRITES"]));
    }

    /// <summary>
    /// Reads every component from the central archive for bounded library pickers. The workbook
    /// remains read-only; callers decide which structured categories are relevant.
    /// </summary>
    public Task<IReadOnlyList<ComponentIR>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled) return Task.FromResult<IReadOnlyList<ComponentIR>>(Array.Empty<ComponentIR>());

        using var workbook = new XLWorkbook(_workbookPath);
        if (!workbook.Worksheets.TryGetWorksheet("Components", out var componentsSheet) ||
            !workbook.Worksheets.TryGetWorksheet("Ports", out var portsSheet) ||
            !workbook.Worksheets.TryGetWorksheet("Pins", out var pinsSheet))
            return Task.FromResult<IReadOnlyList<ComponentIR>>(Array.Empty<ComponentIR>());

        var componentRows = ReadRows(componentsSheet);
        var portRows = ReadRows(portsSheet);
        var pinRows = ReadRows(pinsSheet);
        var powerConversionRows = workbook.Worksheets.TryGetWorksheet("PowerConversions", out var powerConversionsSheet)
            ? ReadRows(powerConversionsSheet)
            : Array.Empty<IReadOnlyDictionary<string, string>>();
        IReadOnlyList<ComponentIR> result = componentRows
            .Select(row => BuildComponent(row, portRows, pinRows, powerConversionRows))
            .OrderBy(component => component.Identity.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Identity.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    private ComponentIR BuildComponent(
        IReadOnlyDictionary<string, string> componentRow,
        IReadOnlyList<IReadOnlyDictionary<string, string>> portRows,
        IReadOnlyList<IReadOnlyDictionary<string, string>> pinRows,
        IReadOnlyList<IReadOnlyDictionary<string, string>> powerConversionRows)
    {
        var componentId = Required(componentRow, "ComponentID");
        var manufacturer = Required(componentRow, "Manufacturer");
        var model = Required(componentRow, "Model");

        var ports = portRows
            .Where(row => string.Equals(Get(row, "ComponentID"), componentId, StringComparison.OrdinalIgnoreCase))
            .Select(BuildPort)
            .Where(port => port is not null)
            .Cast<ComponentPort>()
            .ToArray();
        var portIds = ports.Select(port => port.PortId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pins = pinRows
            .Where(row => portIds.Contains(Get(row, "PortID")))
            .Select(BuildPin)
            .Where(pin => pin is not null)
            .Cast<ComponentPin>()
            .ToArray();
        var powerConversions = powerConversionRows
            .Where(row => string.Equals(Meaningful(Get(row, "ComponentID")), componentId, StringComparison.OrdinalIgnoreCase))
            .Where(HasConversionPayload)
            .Select(BuildPowerConversion)
            .OrderBy(conversion => conversion.ConversionId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(conversion => conversion.InputPowerDomainId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(conversion => conversion.OutputPowerDomainId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(conversion => CanonicalIdList(conversion.InputPortIds), StringComparer.Ordinal)
            .ThenBy(conversion => CanonicalIdList(conversion.InputPinIds), StringComparer.Ordinal)
            .ThenBy(conversion => CanonicalIdList(conversion.OutputPortIds), StringComparer.Ordinal)
            .ThenBy(conversion => CanonicalIdList(conversion.OutputPinIds), StringComparer.Ordinal)
            .ToArray();

        var specifications = BuildSpecifications(componentRow);
        var rootConnector = BuildRootConnector(ports);
        var topologyFromArchive = ParseReadiness(Get(componentRow, "TopologyStatus"));
        var wiring = DetermineWiringReadiness(pins);
        var assets = new ComponentAssets
        {
            DatasheetUrl = FirstUri(
                TryAbsoluteUri(Get(componentRow, "DatasheetURL")),
                ResolveExistingRelativeFile(Get(componentRow, "DatasheetPath"))),
            ImageUrl = ResolveExistingRelativeFile(Get(componentRow, "ImagePath"))
        };

        return new ComponentIR
        {
            Identity = new ComponentIrIdentity
            {
                ComponentId = componentId,
                Manufacturer = manufacturer,
                Model = model,
                Mpn = model
            },
            Classification = new ComponentClassification
            {
                Category = Meaningful(Get(componentRow, "Category"))
            },
            Power = new ComponentPower
            {
                OperatingVoltage = ParseVoltage(Get(componentRow, "Voltage"))
            },
            Io = new ComponentIo
            {
                OutputType = Meaningful(Get(componentRow, "OutputType"))
            },
            Connector = rootConnector,
            Ports = ports,
            Pins = pins,
            PowerConversions = powerConversions,
            Specifications = specifications,
            Assets = assets,
            Readiness = new ComponentReadiness
            {
                Topology = topologyFromArchive,
                Wiring = wiring,
                Validation = ReadinessStatus.Partial,
                Drawing = wiring == ReadinessStatus.Ready ? ReadinessStatus.Partial : ReadinessStatus.NotReady
            }
        };
    }

    private static ComponentPort? BuildPort(IReadOnlyDictionary<string, string> row)
    {
        var portId = Meaningful(Get(row, "PortID"));
        if (portId is null) return null;

        var role = Meaningful(Get(row, "PortRole"));
        return new ComponentPort
        {
            PortId = portId,
            PortName = Meaningful(Get(row, "PortName")),
            PortRole = role,
            PortType = role,
            Direction = Meaningful(Get(row, "Direction")),
            SignalType = Meaningful(Get(row, "SignalType")),
            VoltageDomain = Meaningful(Get(row, "Voltage")),
            PowerDomainId = Meaningful(Get(row, "PowerDomainId")),
            Protocol = Meaningful(Get(row, "Protocol")),
            ConnectorFamily = Meaningful(Get(row, "Connector")),
            ConnectorCoding = Meaningful(Get(row, "ConnectorCoding")),
            ConnectorGender = Meaningful(Get(row, "Gender")),
            PinCount = ParsePositiveInt(Get(row, "PinCount")),
            PhysicalSide = Meaningful(Get(row, "PhysicalSide")),
            TopologyEndpointMode = Meaningful(Get(row, "TopologyEndpointMode"))
        };
    }

    private static ComponentPin? BuildPin(IReadOnlyDictionary<string, string> row)
    {
        var portId = Meaningful(Get(row, "PortID"));
        var pinNumber = Meaningful(Get(row, "PinNumber"));
        if (portId is null || pinNumber is null) return null;

        return new ComponentPin
        {
            PinId = Meaningful(Get(row, "PinID")),
            PortId = portId,
            PinNumber = pinNumber,
            PinName = Meaningful(Get(row, "PinName")),
            PinRole = Meaningful(Get(row, "PinRole")),
            Direction = Meaningful(Get(row, "Direction")),
            SignalType = Meaningful(Get(row, "SignalType")),
            VoltageDomain = Meaningful(Get(row, "Voltage")),
            PowerDomainId = Meaningful(Get(row, "PowerDomainId")),
            Function = Meaningful(Get(row, "Function")),
            PinStatus = RawOrNull(Get(row, "PinStatus")),
            Description = Meaningful(Get(row, "Notes"))
        };
    }

    private static ComponentPowerConversion BuildPowerConversion(IReadOnlyDictionary<string, string> row) => new()
    {
        ConversionId = Meaningful(Get(row, "ConversionID")),
        InputPowerDomainId = Meaningful(Get(row, "InputPowerDomainID")),
        OutputPowerDomainId = Meaningful(Get(row, "OutputPowerDomainID")),
        InputPortIds = ParseStableIdList(Get(row, "InputPortIDs")),
        InputPinIds = ParseStableIdList(Get(row, "InputPinIDs")),
        OutputPortIds = ParseStableIdList(Get(row, "OutputPortIDs")),
        OutputPinIds = ParseStableIdList(Get(row, "OutputPinIDs"))
    };

    private static bool HasConversionPayload(IReadOnlyDictionary<string, string> row) =>
        Meaningful(Get(row, "ConversionID")) is not null ||
        Meaningful(Get(row, "InputPowerDomainID")) is not null ||
        Meaningful(Get(row, "OutputPowerDomainID")) is not null ||
        Meaningful(Get(row, "InputPortIDs")) is not null ||
        Meaningful(Get(row, "InputPinIDs")) is not null ||
        Meaningful(Get(row, "OutputPortIDs")) is not null ||
        Meaningful(Get(row, "OutputPinIDs")) is not null;

    private static IReadOnlyList<string> ParseStableIdList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(';', StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CanonicalIdList(IReadOnlyList<string> values) => string.Join(";", values);

    private static IReadOnlyList<string> BuildUnresolvedConversionDiagnostics(
        IReadOnlyList<IReadOnlyDictionary<string, string>> componentRows,
        IReadOnlyList<IReadOnlyDictionary<string, string>> conversionRows)
    {
        if (conversionRows.Count == 0) return Array.Empty<string>();

        var knownComponentIds = componentRows
            .Select(row => Meaningful(Get(row, "ComponentID")))
            .Where(value => value is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return conversionRows
            .Where(HasConversionPayload)
            .Select(row => new
            {
                ComponentId = Meaningful(Get(row, "ComponentID")),
                ConversionId = Meaningful(Get(row, "ConversionID"))
            })
            .Where(item => item.ComponentId is null || !knownComponentIds.Contains(item.ComponentId))
            .Select(item =>
                $"CENTRAL_WORKBOOK_POWER_CONVERSION_COMPONENT_UNRESOLVED:ComponentID={item.ComponentId ?? "<blank>"};ConversionID={item.ConversionId ?? "<blank>"}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ComponentSpecification> BuildSpecifications(IReadOnlyDictionary<string, string> row)
    {
        var result = new List<ComponentSpecification>();
        var width = ParsePositiveDouble(Get(row, "WidthMm"));
        var height = ParsePositiveDouble(Get(row, "HeightMm"));
        var depth = ParsePositiveDouble(Get(row, "DepthMm"));
        if (width is double w && height is double h && depth is double d)
        {
            result.Add(new ComponentSpecification
            {
                Key = "dimensions",
                Name = "Dimensions",
                Section = "Mechanical",
                Value = $"{Format(w)} x {Format(h)} x {Format(d)} mm",
                Status = VerificationStatus.SingleSource
            });
        }

        AddSpecification(result, "geometry_type", "Geometry Type", "Mechanical", Get(row, "GeometryType"));
        AddSpecification(result, "installation", "Installation / Mounting", "Mechanical", Get(row, "MountingType"));
        AddSpecification(result, "io_type", "I/O Type", "Electrical", Get(row, "IOType"));
        AddSpecification(result, "protocol", "Protocol", "Communication", Get(row, "Protocol"));
        AddSpecification(result, "description", "Description", "Identity", Get(row, "Description"));
        AddSpecification(result, "drawing_path", "Drawing Path", "Mechanical", Get(row, "DrawingPath"));
        AddSpecification(result, "layout_status", "Layout Status", "Mechanical", Get(row, "LayoutStatus"));
        return result;
    }

    private static void AddSpecification(
        ICollection<ComponentSpecification> target,
        string key,
        string name,
        string section,
        string? raw)
    {
        var value = Meaningful(raw);
        if (value is null) return;
        target.Add(new ComponentSpecification
        {
            Key = key,
            Name = name,
            Section = section,
            Value = value,
            Status = VerificationStatus.SingleSource
        });
    }

    private static ComponentConnector BuildRootConnector(IReadOnlyList<ComponentPort> ports)
    {
        if (ports.Count != 1) return new ComponentConnector();
        var port = ports[0];
        return new ComponentConnector
        {
            Family = port.ConnectorFamily,
            Coding = port.ConnectorCoding,
            Pins = port.PinCount
        };
    }

    private static ReadinessStatus DetermineWiringReadiness(IReadOnlyList<ComponentPin> pins)
    {
        if (pins.Count == 0) return ReadinessStatus.NotReady;
        var unresolved = pins.Count(pin =>
        {
            var status = pin.PinStatus?.Trim();
            var intentionallyNoFunction = status is not null &&
                (status.Equals("NC", StringComparison.OrdinalIgnoreCase) ||
                 status.Equals("Reserved", StringComparison.OrdinalIgnoreCase) ||
                 status.Equals("Unused", StringComparison.OrdinalIgnoreCase) ||
                 status.Equals("NotApplicable", StringComparison.OrdinalIgnoreCase));
            return !intentionallyNoFunction && string.IsNullOrWhiteSpace(pin.Function);
        });
        return unresolved == 0 ? ReadinessStatus.Ready : ReadinessStatus.Partial;
    }

    private static NormalizedVoltage? ParseVoltage(string? raw)
    {
        var value = Meaningful(raw);
        if (value is null) return null;

        var range = VoltageRange.Match(value);
        if (range.Success &&
            TryDecimal(range.Groups["min"].Value, out var min) &&
            TryDecimal(range.Groups["max"].Value, out var max))
        {
            return new NormalizedVoltage
            {
                Min = min,
                Max = max,
                Unit = "V",
                Type = NormalizeVoltageType(range.Groups["type"].Value)
            };
        }

        var single = VoltageSingle.Match(value);
        if (single.Success && TryDecimal(single.Groups["value"].Value, out var nominal))
        {
            return new NormalizedVoltage
            {
                Min = nominal,
                Max = nominal,
                Unit = "V",
                Type = NormalizeVoltageType(single.Groups["type"].Value)
            };
        }
        return null;
    }

    private Uri? ResolveExistingRelativeFile(string? raw)
    {
        var relative = Meaningful(raw);
        if (relative is null || string.IsNullOrWhiteSpace(_workbookRoot)) return null;
        try
        {
            var root = Path.GetFullPath(_workbookRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(
                root,
                relative
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar)));
            var rootPrefix = root + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(fullPath) ? new Uri(fullPath, UriKind.Absolute) : null;
        }
        catch
        {
            return null;
        }
    }

    private static Uri? TryAbsoluteUri(string? raw) =>
        Uri.TryCreate(Meaningful(raw), UriKind.Absolute, out var uri) ? uri : null;

    private static Uri? FirstUri(params Uri?[] values) => values.FirstOrDefault(value => value is not null);

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(IXLWorksheet worksheet)
    {
        var headerRow = worksheet.RowsUsed()
            .FirstOrDefault(row => row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString())));
        if (headerRow is null) return Array.Empty<IReadOnlyDictionary<string, string>>();

        var headers = headerRow.CellsUsed()
            .Select(cell => (Column: cell.Address.ColumnNumber, Header: cell.GetString().Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Header))
            .ToArray();

        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in worksheet.RowsUsed().Where(row => row.RowNumber() > headerRow.RowNumber()))
        {
            if (!row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString()))) continue;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
                values[header.Header] = row.Cell(header.Column).GetString().Trim();
            rows.Add(values);
        }
        return rows;
    }

    private static string Required(IReadOnlyDictionary<string, string> row, string key) =>
        Meaningful(Get(row, key)) ?? throw new InvalidDataException($"Required central workbook field is blank: {key}");

    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : string.Empty;

    private static string NormalizeManufacturer(string? value) =>
        ManufacturerNormalizer.NormalizeKey(value) ?? string.Empty;

    private static string NormalizeModel(string? value) =>
        ModelNormalizer.Normalize(value)?.Canonical ?? value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string? Meaningful(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        return trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("NotApplicable", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("TBD", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static string? RawOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParsePositiveInt(string? raw) =>
        int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private static double? ParsePositiveDouble(string? raw) =>
        double.TryParse(raw?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private static bool TryDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string? NormalizeVoltageType(string raw)
    {
        if (raw.Equals("DC", StringComparison.OrdinalIgnoreCase)) return "DC";
        if (raw.Equals("AC", StringComparison.OrdinalIgnoreCase)) return "AC";
        return null;
    }

    private static ReadinessStatus ParseReadiness(string? raw) => raw?.Trim().ToUpperInvariant() switch
    {
        "READY" => ReadinessStatus.Ready,
        "REVIEW" => ReadinessStatus.Partial,
        "NEEDSDATA" => ReadinessStatus.NotReady,
        "NEEDS DATA" => ReadinessStatus.NotReady,
        _ => ReadinessStatus.Partial
    };

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}