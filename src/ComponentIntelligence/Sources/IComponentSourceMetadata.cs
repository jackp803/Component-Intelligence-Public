using ComponentIntelligence.Resolution;

namespace ComponentIntelligence.Sources;

public interface IComponentSourceMetadata
{
    string SourceName { get; }
    IReadOnlyCollection<string> SupportedManufacturers { get; }
    bool CanHandle(string manufacturer, string model);
}

public static class ComponentSourceRoutingExtensions
{
    public static bool CanHandle(this IComponentSource source, string manufacturer, string model)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source is not IComponentSourceMetadata metadata || metadata.CanHandle(manufacturer, model);
    }

    public static string DisplayName(this IComponentSource source) =>
        source is IComponentSourceMetadata metadata ? metadata.SourceName : source.GetType().Name;
}
