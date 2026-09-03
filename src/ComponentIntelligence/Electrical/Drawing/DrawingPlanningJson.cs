using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingPlanningJson
{
    private static readonly Regex HashPattern = new("^[A-F0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly IReadOnlyDictionary<string, string> StableIdFields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["representations"] = "representationId", ["connections"] = "connectionId", ["cables"] = "cableInstanceId",
        ["controllerModules"] = "controllerModuleId", ["networks"] = "networkId", ["seriesChains"] = "seriesChainId",
        ["heavyDutyConnectors"] = "heavyDutyConnectorId", ["powerDomains"] = "powerDomainId", ["wiringRules"] = "wiringRuleId",
        ["issues"] = "issueId"
    };
    private static readonly HashSet<string> SortedScalarArrays = new(StringComparer.Ordinal)
    {
        "allowedRotations", "contactIds", "representationIds", "connectionIds"
    };

    public static string Serialize(DrawingPlanningInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);
        var node = JsonSerializer.SerializeToNode(input with { PlanningInputHash = null }, Options)!.AsObject();
        var canonicalWithoutHash = Canonicalize(node, null);
        var hash = Sha256(CanonicalText(canonicalWithoutHash));
        canonicalWithoutHash["planningInputHash"] = hash;
        return CanonicalText(Canonicalize(canonicalWithoutHash, null));
    }

    public static DrawingPlanningInput Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var input = JsonSerializer.Deserialize<DrawingPlanningInput>(json, Options)
            ?? throw new InvalidDataException("Drawing planning input is empty.");
        Validate(input);
        if (string.IsNullOrWhiteSpace(input.PlanningInputHash) || !HashPattern.IsMatch(input.PlanningInputHash))
            throw new InvalidDataException("planningInputHash must be uppercase SHA-256.");
        var node = JsonSerializer.SerializeToNode(input with { PlanningInputHash = null }, Options)!.AsObject();
        var expected = Sha256(CanonicalText(Canonicalize(node, null)));
        if (!string.Equals(expected, input.PlanningInputHash, StringComparison.Ordinal))
            throw new InvalidDataException("planningInputHash mismatch.");
        var canonical = Serialize(input with { PlanningInputHash = null });
        return JsonSerializer.Deserialize<DrawingPlanningInput>(canonical, Options)!;
    }

    public static string ComputeHash(DrawingPlanningInput input)
    {
        var json = Serialize(input with { PlanningInputHash = null });
        return JsonNode.Parse(json)!["planningInputHash"]!.GetValue<string>();
    }

    private static void Validate(DrawingPlanningInput input)
    {
        if (!string.Equals(input.SchemaVersion, DrawingPlanningInput.V1, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported planning input schema.");
        if (string.IsNullOrWhiteSpace(input.ProjectId)) throw new InvalidDataException("ProjectId is required.");
        EnsureUnique(input.Representations, x => x.RepresentationId, "representationId");
        EnsureUnique(input.Connections, x => x.ConnectionId, "connectionId");
        EnsureUnique(input.Cables, x => x.CableInstanceId, "cableInstanceId");
        foreach (var rep in input.Representations)
        {
            if (string.IsNullOrWhiteSpace(rep.RepresentationId) || string.IsNullOrWhiteSpace(rep.OwnerId))
                throw new InvalidDataException("Representation stable identities are required.");
            if (rep.AllowedRotations.Count == 0 || rep.AllowedRotations.Distinct().Count() != rep.AllowedRotations.Count ||
                rep.AllowedRotations.Any(x => x is not (0 or 90 or 180 or 270)))
                throw new InvalidDataException("allowedRotations must be explicit subset of 0/90/180/270.");
            EnsureUnique(rep.PortBindings, x => x.EngineeringEndpointId, "engineeringEndpointId");
            if (rep.AssetHashSha256 is not null && !HashPattern.IsMatch(rep.AssetHashSha256))
                throw new InvalidDataException("assetHashSha256 must be uppercase SHA-256.");
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, Func<T, string> selector, string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var id = selector(value)?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw new InvalidDataException($"Duplicate or empty {name}.");
        }
    }

    private static JsonNode? Canonicalize(JsonNode? node, string? parentKey)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var pair in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
                result[pair.Key] = Canonicalize(pair.Value?.DeepClone(), pair.Key);
            return result;
        }
        if (node is JsonArray array)
        {
            var items = array.Select(x => Canonicalize(x?.DeepClone(), parentKey)).ToList();
            if (parentKey is not null && StableIdFields.TryGetValue(parentKey, out var stableId))
                items = items.OrderBy(x => x?[stableId]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal).ToList();
            else if (parentKey is not null && SortedScalarArrays.Contains(parentKey))
                items = items.OrderBy(x => x?.ToJsonString() ?? string.Empty, StringComparer.Ordinal).ToList();
            var result = new JsonArray();
            foreach (var item in items) result.Add(item);
            return result;
        }
        if (node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) && Math.Abs(number % 1) < double.Epsilon)
            return JsonValue.Create((long)number);
        return node?.DeepClone();
    }

    private static string CanonicalText(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
