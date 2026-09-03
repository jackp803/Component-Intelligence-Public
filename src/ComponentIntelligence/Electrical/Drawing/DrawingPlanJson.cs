using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.Electrical.Drawing;

public static class DrawingPlanJson
{
    private static readonly Regex HashPattern = new("^[A-F0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static readonly IReadOnlyDictionary<string, string> StableIdFields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pages"] = "pageId", ["groups"] = "groupId", ["placements"] = "representationId", ["routes"] = "routeId",
        ["crossPageRelations"] = "relationId", ["cableDetailTemplates"] = "templateId", ["issues"] = "issueId"
    };
    private static readonly HashSet<string> SortedScalarArrays = new(StringComparer.Ordinal) { "groupIds", "representationIds", "allowedRotations" };

    public static string Serialize(DrawingPlanDocument plan)
    {
        ArgumentNullException.ThrowIfNull(plan); Validate(plan, requireHash: false);
        var node = JsonSerializer.SerializeToNode(plan with { DrawingPlanHash = null }, Options)!.AsObject();
        var canonical = Canonicalize(node, null)!.AsObject(); canonical["drawingPlanHash"] = Sha256(CanonicalText(canonical));
        return CanonicalText(Canonicalize(canonical, null));
    }

    public static DrawingPlanDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var plan = JsonSerializer.Deserialize<DrawingPlanDocument>(json, Options) ?? throw new InvalidDataException("Drawing Plan is empty.");
        Validate(plan, requireHash: true);
        var node = JsonSerializer.SerializeToNode(plan with { DrawingPlanHash = null }, Options)!.AsObject();
        var expected = Sha256(CanonicalText(Canonicalize(node, null)));
        if (!string.Equals(expected, plan.DrawingPlanHash, StringComparison.Ordinal)) throw new InvalidDataException("drawingPlanHash mismatch.");
        return JsonSerializer.Deserialize<DrawingPlanDocument>(Serialize(plan with { DrawingPlanHash = null }), Options)!;
    }

    public static DrawingPlanDocument Rehash(DrawingPlanDocument plan) => Deserialize(Serialize(plan with { DrawingPlanHash = null }));

    private static void Validate(DrawingPlanDocument plan, bool requireHash)
    {
        if (!string.Equals(plan.SchemaVersion, DrawingPlanDocument.V1, StringComparison.Ordinal)) throw new InvalidDataException("Unsupported Drawing Plan schema.");
        if (string.IsNullOrWhiteSpace(plan.ProjectId)) throw new InvalidDataException("ProjectId is required.");
        RequireHash(plan.SourcePlanningInputHash, nameof(plan.SourcePlanningInputHash)); RequireHash(plan.SourcePagePlanHash, nameof(plan.SourcePagePlanHash)); if (requireHash) RequireHash(plan.DrawingPlanHash, nameof(plan.DrawingPlanHash));
        EnsureUnique(plan.Pages, x => x.PageId, "pageId"); EnsureUnique(plan.Groups, x => x.GroupId, "groupId"); EnsureUnique(plan.Placements, x => x.RepresentationId, "representationId"); EnsureUnique(plan.Routes, x => x.RouteId, "routeId"); EnsureUnique(plan.CrossPageRelations, x => x.RelationId, "relationId"); EnsureUnique(plan.CableDetailTemplates, x => x.TemplateId, "templateId");
        var pageIds = plan.Pages.Select(x => x.PageId).ToHashSet(StringComparer.Ordinal); var groupIds = plan.Groups.Select(x => x.GroupId).ToHashSet(StringComparer.Ordinal); var templateIds = plan.CableDetailTemplates.Select(x => x.TemplateId).ToHashSet(StringComparer.Ordinal);
        if (plan.Pages.Select(x => x.Order).Distinct().Count() != plan.Pages.Count) throw new InvalidDataException("Drawing Plan page order must be unique.");
        var placementPages = plan.Placements.ToDictionary(x => x.RepresentationId, x => x.PageId, StringComparer.Ordinal);
        foreach (var template in plan.CableDetailTemplates)
        {
            if (string.IsNullOrWhiteSpace(template.EndAInterfaceLayoutFamily) || string.IsNullOrWhiteSpace(template.EndBInterfaceLayoutFamily)) throw new InvalidDataException("Cable template interface families are required.");
            if (!new[] { "M12", "RJ45", "LooseLead", "Special", "Other" }.Contains(template.EndAInterfaceLayoutFamily, StringComparer.Ordinal) || !new[] { "M12", "RJ45", "LooseLead", "Special", "Other" }.Contains(template.EndBInterfaceLayoutFamily, StringComparer.Ordinal)) throw new InvalidDataException("Cable template interface family is invalid.");
        }
        foreach (var placement in plan.Placements)
        {
            if (!pageIds.Contains(placement.PageId) || !groupIds.Contains(placement.GroupId)) throw new InvalidDataException("Placement page/group reference is invalid.");
            if (placement.Width <= 0 || placement.Height <= 0) throw new InvalidDataException("Placement bounds are invalid.");
            if (placement.AllowedRotations.Count == 0 || placement.AllowedRotations.Any(x => x is not (0 or 90 or 180 or 270)) || !placement.AllowedRotations.Contains(placement.RotationDegrees)) throw new InvalidDataException("Placement rotation is not allowed.");
            if (placement.CableTemplateId is not null && !templateIds.Contains(placement.CableTemplateId)) throw new InvalidDataException("Placement CableTemplateId is unknown.");
        }
        var routes = new Dictionary<string, DrawingRoute>(StringComparer.Ordinal);
        foreach (var route in plan.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.PageId) || !pageIds.Contains(route.PageId)) throw new InvalidDataException("Route pageId is unknown.");
            if (string.IsNullOrWhiteSpace(route.ConnectionId) || string.IsNullOrWhiteSpace(route.EndpointAId) || string.IsNullOrWhiteSpace(route.EndpointBId)) throw new InvalidDataException("Route engineering identity is required.");
            if (route.Points.Count < 2) throw new InvalidDataException("Route requires at least two points.");
            for (var i = 1; i < route.Points.Count; i++) if (route.Points[i - 1].X != route.Points[i].X && route.Points[i - 1].Y != route.Points[i].Y) throw new InvalidDataException("Route geometry must be orthogonal.");
            routes[route.RouteId] = route;
        }
        foreach (var relation in plan.CrossPageRelations)
        {
            if (string.IsNullOrWhiteSpace(relation.SourceRepresentationId) || string.IsNullOrWhiteSpace(relation.DestinationRepresentationId) || string.IsNullOrWhiteSpace(relation.RelationKind) || string.IsNullOrWhiteSpace(relation.EngineeringId)) throw new InvalidDataException("Cross-page relation identity is required.");
            if (relation.SourcePageId is not null && !pageIds.Contains(relation.SourcePageId)) throw new InvalidDataException("Cross-page relation sourcePageId is unknown.");
            if (relation.DestinationPageId is not null && !pageIds.Contains(relation.DestinationPageId)) throw new InvalidDataException("Cross-page relation destinationPageId is unknown.");
            if (string.Equals(relation.RelationKind, "ElectricalConnectionContinuation", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(relation.SourcePageId) || string.IsNullOrWhiteSpace(relation.DestinationPageId) || string.IsNullOrWhiteSpace(relation.SourceRouteId) || string.IsNullOrWhiteSpace(relation.DestinationRouteId)) throw new InvalidDataException("ElectricalConnectionContinuation requires page and route references.");
                if (string.Equals(relation.SourcePageId, relation.DestinationPageId, StringComparison.Ordinal)) throw new InvalidDataException("ElectricalConnectionContinuation must cross pages.");
                if (!routes.TryGetValue(relation.SourceRouteId, out var sourceRoute) || !routes.TryGetValue(relation.DestinationRouteId, out var destinationRoute)) throw new InvalidDataException("ElectricalConnectionContinuation route reference is unknown.");
                if (!string.Equals(sourceRoute.PageId, relation.SourcePageId, StringComparison.Ordinal) || !string.Equals(destinationRoute.PageId, relation.DestinationPageId, StringComparison.Ordinal)) throw new InvalidDataException("ElectricalConnectionContinuation route/page mismatch.");
                if (!string.Equals(sourceRoute.ConnectionId, relation.EngineeringId, StringComparison.Ordinal) || !string.Equals(destinationRoute.ConnectionId, relation.EngineeringId, StringComparison.Ordinal)) throw new InvalidDataException("ElectricalConnectionContinuation engineering identity mismatch.");
            }
            else
            {
                if (relation.SourceRouteId is not null || relation.DestinationRouteId is not null) throw new InvalidDataException("Non-connection cross-page relation cannot claim route references.");
                if (relation.SourcePageId is not null && placementPages.TryGetValue(relation.SourceRepresentationId, out var sourcePage) && !string.Equals(sourcePage, relation.SourcePageId, StringComparison.Ordinal)) throw new InvalidDataException("Cross-page relation sourcePageId mismatch.");
                if (relation.DestinationPageId is not null && placementPages.TryGetValue(relation.DestinationRepresentationId, out var destinationPage) && !string.Equals(destinationPage, relation.DestinationPageId, StringComparison.Ordinal)) throw new InvalidDataException("Cross-page relation destinationPageId mismatch.");
            }
        }
    }

    private static void RequireHash(string? value, string name) { if (string.IsNullOrWhiteSpace(value) || !HashPattern.IsMatch(value)) throw new InvalidDataException($"{name} must be uppercase SHA-256."); }
    private static void EnsureUnique<T>(IEnumerable<T> values, Func<T, string> selector, string name) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var value in values) { var id = selector(value)?.Trim(); if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw new InvalidDataException($"Duplicate or empty {name}."); } }

    private static JsonNode? Canonicalize(JsonNode? node, string? parentKey)
    {
        if (node is JsonObject obj) { var result = new JsonObject(); foreach (var pair in obj.OrderBy(x => x.Key, StringComparer.Ordinal)) result[pair.Key] = Canonicalize(pair.Value?.DeepClone(), pair.Key); return result; }
        if (node is JsonArray array)
        {
            var items = array.Select(x => Canonicalize(x?.DeepClone(), parentKey)).ToList();
            if (parentKey is not null && StableIdFields.TryGetValue(parentKey, out var stableId)) items = items.OrderBy(x => x?[stableId]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal).ToList();
            else if (parentKey is not null && SortedScalarArrays.Contains(parentKey)) items = items.OrderBy(x => x?.ToJsonString() ?? string.Empty, StringComparer.Ordinal).ToList();
            var result = new JsonArray(); foreach (var item in items) result.Add(item); return result;
        }
        if (node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) && Math.Abs(number % 1) < double.Epsilon) return JsonValue.Create((long)number);
        return node?.DeepClone();
    }

    private static string CanonicalText(JsonNode? node) => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static JsonSerializerOptions CreateOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, WriteIndented = false }; options.Converters.Add(new JsonStringEnumConverter()); return options; }
}
