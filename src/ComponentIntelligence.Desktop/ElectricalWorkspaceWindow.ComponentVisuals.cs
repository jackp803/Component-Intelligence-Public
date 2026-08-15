using ComponentIntelligence.Electrical.Bridging;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private async Task<Uri?> ResolveComponentImageAsync(string componentDefinitionId)
    {
        try
        {
            var catalog = new ComponentIrCatalogReader(_databasePath);
            var component = await catalog.GetByIdAsync(componentDefinitionId);
            if (component?.Assets.ImageUrl is not null) return component.Assets.ImageUrl;

            // Placeholder instances keep their project-specific definition ID when enriched so that
            // topology references/connections remain stable. Fall back to the visible identity rather
            // than mutating that project ID only to make an image appear.
            var instance = _project.Components.FirstOrDefault(item =>
                string.Equals(item.ComponentDefinitionId, componentDefinitionId, StringComparison.OrdinalIgnoreCase));
            if (instance is null || string.IsNullOrWhiteSpace(instance.DisplayName)) return null;
            var identity = instance.DisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (identity.Length != 2) return null;
            component = await catalog.FindByIdentityAsync(identity[0], identity[1]);
            return component?.Assets.ImageUrl;
        }
        catch
        {
            return null;
        }
    }
}
