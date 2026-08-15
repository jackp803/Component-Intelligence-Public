using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Repository;

/// <summary>
/// Notion-backed central electrical-material knowledge store.
///
/// Design constraints:
/// - optional: a missing token disables the store and never blocks local/offline use;
/// - evidence-preserving: Unknown/Inferred/Conflict are retained and never promoted to Verified;
/// - structured: Components, Documents, Ports, Pins and Specifications are separate related data sources;
/// - local runtime state (Topology X/Y, Layout placement, Undo/Redo) is intentionally not stored here.
/// </summary>
public sealed class NotionComponentKnowledgeStore : IComponentKnowledgeStore
{
    private static readonly Regex VoltageRange = new(
        @"(?<min>-?\d+(?:[.,]\d+)?)\s*(?:\.\.\.?|…|to|-)\s*(?<max>-?\d+(?:[.,]\d+)?)\s*V(?:\s*(?<type>AC|DC))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VoltageSingle = new(
        @"(?<!\d)(?<value>-?\d+(?:[.,]\d+)?)\s*V(?:\s*(?<type>AC|DC))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly NotionKnowledgeStoreOptions _options;

    public NotionComponentKnowledgeStore(NotionKnowledgeStoreOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? new HttpClient { BaseAddress = options.ApiBaseAddress };
        if (_http.BaseAddress is null) _http.BaseAddress = options.ApiBaseAddress;
    }

    public bool IsEnabled => _options.IsEnabled;

    public async Task<ComponentKnowledgeLookup> FindByIdentityAsync(
        string manufacturer,
        string model,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return new ComponentKnowledgeLookup(null, ["NOTION_CENTRAL_DISABLED_NO_TOKEN"]);
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
            return new ComponentKnowledgeLookup(null, ["NOTION_CENTRAL_IDENTITY_INCOMPLETE"]);

        try
        {
            var canonicalKey = CanonicalKey(manufacturer, model);
            var page = await QuerySingleAsync(
                _options.ComponentsDataSourceId,
                RichTextFilter("Canonical Key", canonicalKey),
                cancellationToken);
            if (page is null)
                return new ComponentKnowledgeLookup(null, ["NOTION_CENTRAL_MISS"]);

            var pageId = GetString(page, "id");
            if (string.IsNullOrWhiteSpace(pageId))
                return new ComponentKnowledgeLookup(null, ["NOTION_CENTRAL_COMPONENT_PAGE_ID_MISSING"]);

            var properties = GetProperties(page);
            var componentManufacturer = GetText(properties, "Manufacturer") ?? manufacturer;
            var componentModel = GetText(properties, "Model / Part Number") ?? model;

            var ports = await LoadPortsAsync(pageId, cancellationToken);
            var pins = await LoadPinsAsync(pageId, cancellationToken);
            var specifications = await LoadSpecificationsAsync(pageId, cancellationToken);
            var documents = await LoadDocumentsAsync(pageId, cancellationToken);
            var connectorFamily = GetText(properties, "Connector");
            var connectorCoding = ports
                .Select(port => port.Coding)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1
                    ? ports.Select(port => port.Coding).First(value => !string.IsNullOrWhiteSpace(value))
                    : null;

            var topologyReadiness = ParseReadiness(GetSelect(properties, "Topology Readiness"));
            var component = new ComponentIR
            {
                Identity = new ComponentIrIdentity
                {
                    ComponentId = $"notion:{pageId}",
                    Manufacturer = componentManufacturer,
                    Model = componentModel,
                    Mpn = componentModel
                },
                Classification = new ComponentClassification
                {
                    Category = GetText(properties, "Category")
                },
                Power = new ComponentPower
                {
                    OperatingVoltage = ParseVoltage(GetText(properties, "Voltage"))
                },
                Io = new ComponentIo
                {
                    OutputType = GetText(properties, "Output Type")
                },
                Connector = new ComponentConnector
                {
                    Family = connectorFamily,
                    Coding = connectorCoding
                },
                Ports = ports.Select(port => port.Port).ToArray(),
                Pins = pins,
                Specifications = specifications,
                Documents = documents,
                Assets = new ComponentAssets
                {
                    ProductPageUrl = ParseUri(GetUrl(properties, "Product URL")),
                    DatasheetUrl = ParseUri(GetUrl(properties, "Datasheet URL")),
                    ImageUrl = ParseUri(GetUrl(properties, "Image URL"))
                },
                Readiness = new ComponentReadiness
                {
                    Topology = topologyReadiness,
                    Wiring = topologyReadiness == ReadinessStatus.Ready ? ReadinessStatus.Partial : topologyReadiness,
                    Validation = ReadinessStatus.Partial,
                    Drawing = ReadinessStatus.Partial
                }
            };

            return new ComponentKnowledgeLookup(
                component,
                [
                    "NOTION_CENTRAL_HIT",
                    $"NOTION_CENTRAL_PORTS:{component.Ports.Count}",
                    $"NOTION_CENTRAL_PINS:{component.Pins.Count}",
                    $"NOTION_CENTRAL_SPECIFICATIONS:{component.Specifications.Count}",
                    $"NOTION_CENTRAL_DOCUMENTS:{component.Documents.Count}"
                ]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ComponentKnowledgeLookup(
                null,
                [$"NOTION_CENTRAL_READ_FAILED:{exception.GetType().Name}:{exception.Message}"]);
        }
    }

    public async Task<ComponentKnowledgeWriteResult> UpsertAsync(
        ComponentIR component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!IsEnabled)
            return new ComponentKnowledgeWriteResult(false, ["NOTION_CENTRAL_DISABLED_NO_TOKEN"]);

        try
        {
            var pageId = await UpsertComponentPageAsync(component, cancellationToken);
            foreach (var port in component.Ports)
                await UpsertPortAsync(pageId, component, port, cancellationToken);
            foreach (var pin in component.Pins)
                await UpsertPinAsync(pageId, component, pin, cancellationToken);
            foreach (var specification in component.Specifications)
                await UpsertSpecificationAsync(pageId, component, specification, cancellationToken);
            foreach (var document in component.Documents)
                await UpsertDocumentAsync(pageId, component, document, cancellationToken);

            return new ComponentKnowledgeWriteResult(
                true,
                [
                    "NOTION_CENTRAL_SYNC_OK",
                    $"NOTION_CENTRAL_SYNC_PORTS:{component.Ports.Count}",
                    $"NOTION_CENTRAL_SYNC_PINS:{component.Pins.Count}",
                    $"NOTION_CENTRAL_SYNC_SPECIFICATIONS:{component.Specifications.Count}",
                    $"NOTION_CENTRAL_SYNC_DOCUMENTS:{component.Documents.Count}"
                ]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ComponentKnowledgeWriteResult(
                false,
                [$"NOTION_CENTRAL_SYNC_FAILED:{exception.GetType().Name}:{exception.Message}"]);
        }
    }

    internal static string CanonicalKey(string manufacturer, string model) =>
        $"{manufacturer.Trim().ToUpperInvariant()}::{model.Trim().ToUpperInvariant()}";

    private async Task<string> UpsertComponentPageAsync(ComponentIR component, CancellationToken cancellationToken)
    {
        var key = CanonicalKey(component.Identity.Manufacturer, component.Identity.Model);
        var existing = await QuerySingleAsync(
            _options.ComponentsDataSourceId,
            RichTextFilter("Canonical Key", key),
            cancellationToken);

        var properties = new JsonObject
        {
            ["Component"] = TitleProperty($"{component.Identity.Manufacturer} {component.Identity.Model}"),
            ["Manufacturer"] = RichTextProperty(component.Identity.Manufacturer),
            ["Model / Part Number"] = RichTextProperty(component.Identity.Model),
            ["Canonical Key"] = RichTextProperty(key),
            ["Category"] = RichTextProperty(component.Classification.Category),
            ["Voltage"] = RichTextProperty(FormatVoltage(component.Power.OperatingVoltage)),
            ["Output Type"] = RichTextProperty(component.Io.OutputType),
            ["Protocol"] = RichTextProperty(string.Join(" | ", component.Ports.Select(port => port.Protocol).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase))),
            ["Connector"] = RichTextProperty(component.Connector.Family),
            ["Port / Pin Summary"] = RichTextProperty($"Ports: {component.Ports.Count}; Pins: {component.Pins.Count}"),
            ["Product URL"] = UrlProperty(component.Assets.ProductPageUrl),
            ["Datasheet URL"] = UrlProperty(component.Assets.DatasheetUrl),
            ["Image URL"] = UrlProperty(component.Assets.ImageUrl),
            ["PDF SHA256"] = RichTextProperty(component.Documents.Select(document => document.Sha256).FirstOrDefault(NotBlank)),
            ["Verification Status"] = SelectProperty(OverallVerification(component)),
            ["Topology Readiness"] = SelectProperty(FormatTopologyReadiness(component.Readiness.Topology)),
            ["IR Version"] = RichTextProperty("notion-central-v1")
        };

        if (existing is not null)
        {
            var id = GetString(existing, "id") ?? throw new InvalidOperationException("Existing Notion component has no page id.");
            await PatchPageAsync(id, properties, cancellationToken);
            return id;
        }

        return await CreatePageAsync(_options.ComponentsDataSourceId, properties, cancellationToken);
    }

    private async Task UpsertPortAsync(
        string componentPageId,
        ComponentIR component,
        ComponentPort port,
        CancellationToken cancellationToken)
    {
        var existing = await QuerySingleAsync(
            _options.PortsDataSourceId,
            AndFilter(RelationFilter("Component", componentPageId), RichTextFilter("Logical Port ID", port.PortId)),
            cancellationToken);
        var properties = new JsonObject
        {
            ["Port"] = TitleProperty($"{component.Identity.Manufacturer} {component.Identity.Model} :: {port.PortId}"),
            ["Component"] = RelationProperty(componentPageId),
            ["Logical Port ID"] = RichTextProperty(port.PortId),
            ["Port Type"] = RichTextProperty(port.PortType),
            ["Connector Family"] = RichTextProperty(port.ConnectorFamily),
            ["Connector Coding"] = RichTextProperty(string.Equals(port.ConnectorFamily, component.Connector.Family, StringComparison.OrdinalIgnoreCase) ? component.Connector.Coding : null),
            ["Signal Type"] = RichTextProperty(port.SignalType),
            ["Direction"] = RichTextProperty(port.Direction),
            ["Voltage Domain"] = RichTextProperty(port.VoltageDomain),
            ["Protocol"] = RichTextProperty(port.Protocol),
            ["Allowed Connections"] = RichTextProperty(string.Join(" | ", port.AllowedConnections)),
            ["Verification Status"] = SelectProperty("Unknown")
        };
        await CreateOrPatchAsync(_options.PortsDataSourceId, existing, properties, cancellationToken);
    }

    private async Task UpsertPinAsync(
        string componentPageId,
        ComponentIR component,
        ComponentPin pin,
        CancellationToken cancellationToken)
    {
        var filters = new List<JsonObject>
        {
            RelationFilter("Component", componentPageId),
            RichTextFilter("Pin Number", pin.PinNumber)
        };
        if (!string.IsNullOrWhiteSpace(pin.PortId))
            filters.Add(RichTextFilter("Port ID", pin.PortId.Trim()));

        var existing = await QuerySingleAsync(
            _options.PinsDataSourceId,
            AndFilter(filters.ToArray()),
            cancellationToken);
        var evidence = PreferredEvidence(pin.Evidence);
        var verification = evidence?.VerificationStatus ?? (string.IsNullOrWhiteSpace(pin.Function) ? VerificationStatus.NotAvailable : VerificationStatus.SingleSource);
        var owner = string.IsNullOrWhiteSpace(pin.PortId) ? "Unassigned" : pin.PortId.Trim();
        var properties = new JsonObject
        {
            ["Pin"] = TitleProperty($"{component.Identity.Manufacturer} {component.Identity.Model} :: {owner} Pin {pin.PinNumber}"),
            ["Component"] = RelationProperty(componentPageId),
            ["Port ID"] = RichTextProperty(pin.PortId),
            ["Pin Number"] = RichTextProperty(pin.PinNumber),
            ["Function"] = RichTextProperty(pin.Function),
            ["Signal Type"] = RichTextProperty(pin.SignalType),
            ["Direction"] = RichTextProperty(pin.Direction),
            ["Voltage Domain"] = RichTextProperty(pin.VoltageDomain),
            ["Description"] = RichTextProperty(pin.Description),
            ["Verification Status"] = SelectProperty(FormatVerification(verification)),
            ["Evidence Summary"] = RichTextProperty(EvidenceSummary(evidence)),
            ["Source URL"] = UrlProperty(evidence?.DocumentUrl ?? evidence?.SourceUrl),
            ["Document SHA256"] = RichTextProperty(evidence?.DocumentHashSha256),
            ["Source Trust"] = RichTextProperty(evidence?.SourceType.ToString()),
            ["Source Page"] = NumberProperty(evidence?.PageNumber)
        };
        await CreateOrPatchAsync(_options.PinsDataSourceId, existing, properties, cancellationToken);
    }

    private async Task UpsertSpecificationAsync(
        string componentPageId,
        ComponentIR component,
        ComponentSpecification specification,
        CancellationToken cancellationToken)
    {
        var identityProperty = string.IsNullOrWhiteSpace(specification.Key) ? "Name" : "Key";
        var identityValue = string.IsNullOrWhiteSpace(specification.Key) ? specification.Name : specification.Key!;
        var existing = await QuerySingleAsync(
            _options.SpecificationsDataSourceId,
            AndFilter(RelationFilter("Component", componentPageId), RichTextFilter(identityProperty, identityValue)),
            cancellationToken);
        var evidence = PreferredEvidence(specification.Evidence);
        var properties = new JsonObject
        {
            ["Specification"] = TitleProperty($"{component.Identity.Manufacturer} {component.Identity.Model} :: {identityValue}"),
            ["Component"] = RelationProperty(componentPageId),
            ["Key"] = RichTextProperty(specification.Key),
            ["Name"] = RichTextProperty(specification.Name),
            ["Section"] = RichTextProperty(specification.Section),
            ["Value"] = RichTextProperty(specification.Value),
            ["Raw Value"] = RichTextProperty(evidence?.RawValue),
            ["Verification Status"] = SelectProperty(FormatVerification(specification.Status)),
            ["Source Trust"] = RichTextProperty(evidence?.SourceType.ToString()),
            ["Evidence Summary"] = RichTextProperty(EvidenceSummary(evidence)),
            ["Source URL"] = UrlProperty(evidence?.DocumentUrl ?? evidence?.SourceUrl),
            ["Document SHA256"] = RichTextProperty(evidence?.DocumentHashSha256),
            ["Source Page"] = NumberProperty(evidence?.PageNumber)
        };
        await CreateOrPatchAsync(_options.SpecificationsDataSourceId, existing, properties, cancellationToken);
    }

    private async Task UpsertDocumentAsync(
        string componentPageId,
        ComponentIR component,
        ComponentDocument document,
        CancellationToken cancellationToken)
    {
        var existing = await QuerySingleAsync(
            _options.DocumentsDataSourceId,
            AndFilter(RelationFilter("Component", componentPageId), UrlFilter("Source URL", document.Url.AbsoluteUri)),
            cancellationToken);
        var properties = new JsonObject
        {
            ["Document"] = TitleProperty($"{component.Identity.Manufacturer} {component.Identity.Model} :: {document.Type}"),
            ["Component"] = RelationProperty(componentPageId),
            ["Manufacturer"] = RichTextProperty(component.Identity.Manufacturer),
            ["Model / Part Number"] = RichTextProperty(component.Identity.Model),
            ["Document Type"] = SelectProperty(FormatDocumentType(document.Type)),
            ["Source Trust"] = SelectProperty(FormatSourceTrust(document.SourceType)),
            ["Source URL"] = UrlProperty(document.Url),
            ["SHA256"] = RichTextProperty(document.Sha256),
            ["Identity Status"] = SelectProperty("Confirmed"),
            ["Verification Status"] = SelectProperty("Verified")
        };
        await CreateOrPatchAsync(_options.DocumentsDataSourceId, existing, properties, cancellationToken);
    }

    private async Task<IReadOnlyList<PortWithCoding>> LoadPortsAsync(string componentPageId, CancellationToken cancellationToken)
    {
        var pages = await QueryManyAsync(_options.PortsDataSourceId, RelationFilter("Component", componentPageId), cancellationToken);
        return pages.Select(page =>
        {
            var p = GetProperties(page);
            return new PortWithCoding(
                new ComponentPort
                {
                    PortId = GetText(p, "Logical Port ID") ?? GetText(p, "Port") ?? "UNKNOWN-PORT",
                    PortType = GetText(p, "Port Type"),
                    ConnectorFamily = GetText(p, "Connector Family"),
                    SignalType = GetText(p, "Signal Type"),
                    Direction = GetText(p, "Direction"),
                    VoltageDomain = GetText(p, "Voltage Domain"),
                    Protocol = GetText(p, "Protocol"),
                    AllowedConnections = SplitPipe(GetText(p, "Allowed Connections"))
                },
                GetText(p, "Connector Coding"));
        }).ToArray();
    }

    private async Task<IReadOnlyList<ComponentPin>> LoadPinsAsync(string componentPageId, CancellationToken cancellationToken)
    {
        var pages = await QueryManyAsync(_options.PinsDataSourceId, RelationFilter("Component", componentPageId), cancellationToken);
        return pages.Select(page =>
        {
            var p = GetProperties(page);
            var function = GetText(p, "Function");
            var evidence = BuildEvidence(
                GetText(p, "Source Trust"),
                GetUrl(p, "Source URL"),
                GetText(p, "Document SHA256"),
                GetNumberAsInt(p, "Source Page"),
                function,
                ParseVerification(GetSelect(p, "Verification Status")));
            return new ComponentPin
            {
                PortId = GetText(p, "Port ID"),
                PinNumber = GetText(p, "Pin Number") ?? "?",
                Function = function,
                SignalType = GetText(p, "Signal Type"),
                Direction = GetText(p, "Direction"),
                VoltageDomain = GetText(p, "Voltage Domain"),
                Description = GetText(p, "Description"),
                Evidence = evidence is null ? Array.Empty<Evidence>() : [evidence]
            };
        }).ToArray();
    }

    private async Task<IReadOnlyList<ComponentSpecification>> LoadSpecificationsAsync(string componentPageId, CancellationToken cancellationToken)
    {
        var pages = await QueryManyAsync(_options.SpecificationsDataSourceId, RelationFilter("Component", componentPageId), cancellationToken);
        return pages.Select(page =>
        {
            var p = GetProperties(page);
            var value = GetText(p, "Value");
            var verification = ParseVerification(GetSelect(p, "Verification Status"));
            var evidence = BuildEvidence(
                GetText(p, "Source Trust"),
                GetUrl(p, "Source URL"),
                GetText(p, "Document SHA256"),
                GetNumberAsInt(p, "Source Page"),
                GetText(p, "Raw Value") ?? value,
                verification);
            return new ComponentSpecification
            {
                Key = GetText(p, "Key"),
                Name = GetText(p, "Name") ?? GetText(p, "Specification") ?? "Unknown",
                Section = GetText(p, "Section"),
                Value = value,
                Status = verification,
                Evidence = evidence is null ? Array.Empty<Evidence>() : [evidence]
            };
        }).ToArray();
    }

    private async Task<IReadOnlyList<ComponentDocument>> LoadDocumentsAsync(string componentPageId, CancellationToken cancellationToken)
    {
        var pages = await QueryManyAsync(_options.DocumentsDataSourceId, RelationFilter("Component", componentPageId), cancellationToken);
        var output = new List<ComponentDocument>();
        foreach (var page in pages)
        {
            var p = GetProperties(page);
            var url = ParseUri(GetUrl(p, "Source URL"));
            if (url is null) continue;
            output.Add(new ComponentDocument
            {
                Type = GetSelect(p, "Document Type") ?? "Other",
                Url = url,
                Sha256 = GetText(p, "SHA256"),
                SourceType = ParseDocumentSourceType(GetSelect(p, "Source Trust"), GetSelect(p, "Document Type"))
            });
        }
        return output;
    }

    private async Task CreateOrPatchAsync(string dataSourceId, JsonObject? existing, JsonObject properties, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            await CreatePageAsync(dataSourceId, properties, cancellationToken);
            return;
        }
        var id = GetString(existing, "id") ?? throw new InvalidOperationException("Existing Notion page has no id.");
        await PatchPageAsync(id, properties, cancellationToken);
    }

    private async Task<string> CreatePageAsync(string dataSourceId, JsonObject properties, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["parent"] = new JsonObject { ["type"] = "data_source_id", ["data_source_id"] = dataSourceId },
            ["properties"] = properties
        };
        var response = await SendAsync(HttpMethod.Post, "pages", body, cancellationToken);
        return GetString(response, "id") ?? throw new InvalidOperationException("Notion create-page response omitted id.");
    }

    private Task<JsonObject> PatchPageAsync(string pageId, JsonObject properties, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Patch, $"pages/{pageId}", new JsonObject { ["properties"] = properties }, cancellationToken);

    private async Task<JsonObject?> QuerySingleAsync(string dataSourceId, JsonObject filter, CancellationToken cancellationToken)
    {
        var pages = await QueryManyAsync(dataSourceId, filter, cancellationToken, pageSize: 1);
        return pages.FirstOrDefault();
    }

    private async Task<IReadOnlyList<JsonObject>> QueryManyAsync(
        string dataSourceId,
        JsonObject filter,
        CancellationToken cancellationToken,
        int pageSize = 100)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            $"data_sources/{dataSourceId}/query",
            new JsonObject { ["filter"] = filter, ["page_size"] = Math.Clamp(pageSize, 1, 100) },
            cancellationToken);
        return response["results"] is JsonArray results
            ? results.OfType<JsonObject>().ToArray()
            : Array.Empty<JsonObject>();
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string relativeUri, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.TryAddWithoutValidation("Notion-Version", NotionKnowledgeStoreOptions.ApiVersion);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Notion API {(int)response.StatusCode}: {TrimForDiagnostic(json, 500)}");
        return JsonNode.Parse(json) as JsonObject
               ?? throw new JsonException("Notion API response was not a JSON object.");
    }

    private static JsonObject GetProperties(JsonObject page) =>
        page["properties"] as JsonObject ?? new JsonObject();

    private static string? GetText(JsonObject properties, string name)
    {
        if (properties[name] is not JsonObject property) return null;
        foreach (var key in new[] { "title", "rich_text" })
        {
            if (property[key] is not JsonArray items) continue;
            var text = string.Concat(items.OfType<JsonObject>().Select(item =>
                GetString(item, "plain_text") ?? GetNestedString(item, "text", "content") ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return null;
    }

    private static string? GetSelect(JsonObject properties, string name) =>
        properties[name] is JsonObject property && property["select"] is JsonObject select
            ? GetString(select, "name")
            : null;

    private static string? GetUrl(JsonObject properties, string name) =>
        properties[name] is JsonObject property ? property["url"]?.GetValue<string?>() : null;

    private static int? GetNumberAsInt(JsonObject properties, string name)
    {
        if (properties[name] is not JsonObject property || property["number"] is null) return null;
        return property["number"]!.TryGetValue<double>(out var value) ? (int)Math.Round(value) : null;
    }

    private static string? GetString(JsonObject value, string name) => value[name]?.GetValue<string?>();

    private static string? GetNestedString(JsonObject value, string objectName, string name) =>
        value[objectName] is JsonObject nested ? nested[name]?.GetValue<string?>() : null;

    private static JsonObject TitleProperty(string? value) => new()
    {
        ["title"] = TextArray(value)
    };

    private static JsonObject RichTextProperty(string? value) => new()
    {
        ["rich_text"] = TextArray(value)
    };

    private static JsonObject SelectProperty(string? value) => new()
    {
        ["select"] = string.IsNullOrWhiteSpace(value) ? null : new JsonObject { ["name"] = value }
    };

    private static JsonObject UrlProperty(Uri? value) => UrlProperty(value?.AbsoluteUri);

    private static JsonObject UrlProperty(string? value) => new()
    {
        ["url"] = string.IsNullOrWhiteSpace(value) ? null : value
    };

    private static JsonObject NumberProperty(int? value) => new()
    {
        ["number"] = value
    };

    private static JsonObject RelationProperty(string pageId) => new()
    {
        ["relation"] = new JsonArray(new JsonObject { ["id"] = pageId })
    };

    private static JsonArray TextArray(string? value)
    {
        var array = new JsonArray();
        if (!string.IsNullOrWhiteSpace(value))
            array.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = new JsonObject { ["content"] = TrimForDiagnostic(value, 1900) }
            });
        return array;
    }

    private static JsonObject RichTextFilter(string property, string equals) => new()
    {
        ["property"] = property,
        ["rich_text"] = new JsonObject { ["equals"] = equals }
    };

    private static JsonObject UrlFilter(string property, string equals) => new()
    {
        ["property"] = property,
        ["url"] = new JsonObject { ["equals"] = equals }
    };

    private static JsonObject RelationFilter(string property, string pageId) => new()
    {
        ["property"] = property,
        ["relation"] = new JsonObject { ["contains"] = pageId }
    };

    private static JsonObject AndFilter(params JsonObject[] filters) => new()
    {
        ["and"] = new JsonArray(filters.Select(filter => (JsonNode)filter).ToArray())
    };

    private static string OverallVerification(ComponentIR component)
    {
        var statuses = component.Specifications.Select(specification => specification.Status)
            .Concat(component.Pins.SelectMany(pin => pin.Evidence).Select(evidence => evidence.VerificationStatus))
            .ToArray();
        if (statuses.Contains(VerificationStatus.Conflict)) return "Conflict";
        if (statuses.Contains(VerificationStatus.Verified)) return "Verified";
        if (statuses.Contains(VerificationStatus.UserConfirmed)) return "UserConfirmed";
        if (statuses.Contains(VerificationStatus.Inferred)) return "Inferred";
        return "Unknown";
    }

    private static string FormatVerification(VerificationStatus status) => status switch
    {
        VerificationStatus.Verified => "Verified",
        VerificationStatus.UserConfirmed => "UserConfirmed",
        VerificationStatus.SingleSource => "SingleSource",
        VerificationStatus.Inferred => "Inferred",
        VerificationStatus.Conflict => "Conflict",
        _ => "Unknown"
    };

    private static VerificationStatus ParseVerification(string? value) => value?.Trim() switch
    {
        "Verified" => VerificationStatus.Verified,
        "UserConfirmed" => VerificationStatus.UserConfirmed,
        "SingleSource" => VerificationStatus.SingleSource,
        "Inferred" => VerificationStatus.Inferred,
        "Conflict" => VerificationStatus.Conflict,
        _ => VerificationStatus.NotAvailable
    };

    private static string FormatTopologyReadiness(ReadinessStatus readiness) => readiness switch
    {
        ReadinessStatus.Ready => "Ready",
        ReadinessStatus.Partial => "Review",
        _ => "Needs Data"
    };

    private static ReadinessStatus ParseReadiness(string? value) => value switch
    {
        "Ready" => ReadinessStatus.Ready,
        "Review" => ReadinessStatus.Partial,
        _ => ReadinessStatus.NotReady
    };

    private static string FormatDocumentType(string? type)
    {
        var value = type ?? string.Empty;
        if (value.Contains("datasheet", StringComparison.OrdinalIgnoreCase)) return "Datasheet";
        if (value.Contains("manual", StringComparison.OrdinalIgnoreCase)) return "Manual";
        if (value.Contains("product", StringComparison.OrdinalIgnoreCase)) return "Product Page";
        if (value.Contains("drawing", StringComparison.OrdinalIgnoreCase)) return "Drawing";
        return "Other";
    }

    private static string FormatSourceTrust(ComponentSourceType sourceType) => sourceType switch
    {
        ComponentSourceType.ManufacturerDatasheet or
        ComponentSourceType.ManufacturerProductPage or
        ComponentSourceType.ManufacturerManual or
        ComponentSourceType.ManufacturerDownloadCenter => "Manufacturer",
        ComponentSourceType.User => "User File",
        ComponentSourceType.AuthorizedDistributor => "Authorized Distributor",
        ComponentSourceType.TrustedThirdParty => "Trusted Third Party",
        _ => "Generic Web"
    };

    private static ComponentSourceType ParseDocumentSourceType(string? sourceTrust, string? documentType)
    {
        if (string.Equals(sourceTrust, "User File", StringComparison.OrdinalIgnoreCase)) return ComponentSourceType.User;
        if (string.Equals(sourceTrust, "Authorized Distributor", StringComparison.OrdinalIgnoreCase)) return ComponentSourceType.AuthorizedDistributor;
        if (string.Equals(sourceTrust, "Trusted Third Party", StringComparison.OrdinalIgnoreCase)) return ComponentSourceType.TrustedThirdParty;
        if (!string.Equals(sourceTrust, "Manufacturer", StringComparison.OrdinalIgnoreCase)) return ComponentSourceType.GenericWeb;
        return documentType switch
        {
            "Manual" => ComponentSourceType.ManufacturerManual,
            "Product Page" => ComponentSourceType.ManufacturerProductPage,
            _ => ComponentSourceType.ManufacturerDatasheet
        };
    }

    private static ComponentSourceType ParseSourceType(string? sourceTrust) => sourceTrust?.Trim() switch
    {
        nameof(ComponentSourceType.ManufacturerDatasheet) or "Manufacturer" => ComponentSourceType.ManufacturerDatasheet,
        nameof(ComponentSourceType.ManufacturerProductPage) => ComponentSourceType.ManufacturerProductPage,
        nameof(ComponentSourceType.ManufacturerManual) => ComponentSourceType.ManufacturerManual,
        nameof(ComponentSourceType.ManufacturerDownloadCenter) => ComponentSourceType.ManufacturerDownloadCenter,
        nameof(ComponentSourceType.AuthorizedDistributor) or "Authorized Distributor" => ComponentSourceType.AuthorizedDistributor,
        nameof(ComponentSourceType.TrustedThirdParty) or "Trusted Third Party" => ComponentSourceType.TrustedThirdParty,
        nameof(ComponentSourceType.User) or "User File" => ComponentSourceType.User,
        nameof(ComponentSourceType.AiInference) => ComponentSourceType.AiInference,
        _ => ComponentSourceType.GenericWeb
    };

    private static Evidence? BuildEvidence(
        string? sourceTrust,
        string? sourceUrl,
        string? documentHash,
        int? pageNumber,
        string? rawValue,
        VerificationStatus verificationStatus)
    {
        var uri = ParseUri(sourceUrl);
        if (uri is null && string.IsNullOrWhiteSpace(documentHash) && pageNumber is null) return null;
        return new Evidence
        {
            SourceType = ParseSourceType(sourceTrust),
            SourceUrl = uri,
            DocumentUrl = uri,
            DocumentHashSha256 = documentHash,
            PageNumber = pageNumber,
            ExtractionMethod = ExtractionMethod.UserInput,
            RawValue = rawValue,
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = verificationStatus
        };
    }

    private static Evidence? PreferredEvidence(IReadOnlyList<Evidence> evidence) => evidence
        .OrderByDescending(item => item.VerificationStatus == VerificationStatus.Verified)
        .ThenByDescending(item => item.VerificationStatus == VerificationStatus.UserConfirmed)
        .ThenByDescending(item => SourceScore(item.SourceType))
        .FirstOrDefault();

    private static int SourceScore(ComponentSourceType source) => source switch
    {
        ComponentSourceType.ManufacturerDatasheet => 100,
        ComponentSourceType.ManufacturerManual => 95,
        ComponentSourceType.ManufacturerProductPage => 90,
        ComponentSourceType.ManufacturerDownloadCenter => 85,
        ComponentSourceType.User => 80,
        ComponentSourceType.AuthorizedDistributor => 70,
        ComponentSourceType.TrustedThirdParty => 60,
        ComponentSourceType.GenericWeb => 40,
        ComponentSourceType.AiInference => 10,
        _ => 0
    };

    private static string? EvidenceSummary(Evidence? evidence)
    {
        if (evidence is null) return null;
        var parts = new List<string> { evidence.SourceType.ToString(), evidence.ExtractionMethod.ToString() };
        if (evidence.PageNumber is not null) parts.Add($"page {evidence.PageNumber}");
        if (!string.IsNullOrWhiteSpace(evidence.RawValue)) parts.Add(evidence.RawValue!);
        return string.Join(" | ", parts);
    }

    private static string? FormatVoltage(NormalizedVoltage? voltage)
    {
        if (voltage is null || voltage.Min is null && voltage.Max is null) return null;
        var type = string.IsNullOrWhiteSpace(voltage.Type) ? string.Empty : $" {voltage.Type}";
        if (voltage.Min is not null && voltage.Max is not null && voltage.Min != voltage.Max)
            return $"{voltage.Min.Value.ToString(CultureInfo.InvariantCulture)}...{voltage.Max.Value.ToString(CultureInfo.InvariantCulture)} {voltage.Unit}{type}";
        var value = voltage.Min ?? voltage.Max;
        return $"{value!.Value.ToString(CultureInfo.InvariantCulture)} {voltage.Unit}{type}";
    }

    private static NormalizedVoltage? ParseVoltage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var range = VoltageRange.Match(text);
        if (range.Success && TryDecimal(range.Groups["min"].Value, out var min) && TryDecimal(range.Groups["max"].Value, out var max))
            return new NormalizedVoltage { Min = min, Max = max, Unit = "V", Type = EmptyToNull(range.Groups["type"].Value)?.ToUpperInvariant() };
        var single = VoltageSingle.Match(text);
        if (single.Success && TryDecimal(single.Groups["value"].Value, out var value))
            return new NormalizedVoltage { Min = value, Max = value, Unit = "V", Type = EmptyToNull(single.Groups["type"].Value)?.ToUpperInvariant() };
        return null;
    }

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static Uri? ParseUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static IReadOnlyList<string> SplitPipe(string? value) => string.IsNullOrWhiteSpace(value)
        ? Array.Empty<string>()
        : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string TrimForDiagnostic(string? value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private sealed record PortWithCoding(ComponentPort Port, string? Coding);
}
