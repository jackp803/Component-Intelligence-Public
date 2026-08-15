using System.Text.Json.Nodes;

namespace ComponentIntelligence.Repository;

internal static class JsonNodeCompatibilityExtensions
{
    public static bool TryGetValue<T>(this JsonNode node, out T value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<T>(out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default!;
        return false;
    }
}
