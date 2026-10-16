using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Bridging;

public sealed record BomTopologySyncResult(
    int BomRowCount,
    int AddedInstances,
    int RichInstances,
    int PlaceholderInstances,
    int UnknownQuantityRows,
    int SkippedSpareOnlyRows,
    int DeferredConnectionMaterialRows)
{
    public IReadOnlyList<BomConnectionMaterialOption> ConnectionMaterials { get; init; } =
        Array.Empty<BomConnectionMaterialOption>();
}

public sealed record BomConnectionMaterialOption(
    string CableDefinitionId,
    string Manufacturer,
    string Model,
    string Category,
    int? AvailableQuantity)
{
    public string DisplayLabel =>
        $"{Manufacturer} {Model} · {Category} · BOM Qty {(AvailableQuantity is int quantity ? quantity : "?")}";
}

/// <summary>
/// Projects the working BOM into installed electrical objects for the topology workspace.
/// UsedQuantity drives installed device instances; TotalQuantity/SpareQuantity never creates topology nodes.
/// Structured Cable/Wire/Cable Assembly knowledge is deferred to the connection-material workflow instead of
/// being rendered as a fake device/sensor node. Missing/unknown component knowledge remains a visible placeholder.
/// </summary>
public sealed class BomTopologySynchronizer
{
    private readonly ComponentProjectBridge _bridge = new();

    public async Task<BomTopologySyncResult> SynchronizeAsync(
        ElectricalProject project,
        IReadOnlyList<BomRow> rows,
        Func<string, string, CancellationToken, Task<ComponentIR?>> componentLookup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(componentLookup);

        var added = 0;
        var rich = 0;
        var placeholders = 0;
        var unknownQuantity = 0;
        var spareOnly = 0;
        var deferredConnectionMaterial = 0;
        var connectionMaterials = new Dictionary<string, BomConnectionMaterialOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manufacturer = row.Manufacturer?.Trim();
            var model = row.ModelOrPartNumber?.Trim();
            if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
                continue;

            var quantityUnknown = row.UsedQuantity is null;
            var installedQuantity = row.UsedQuantity.GetValueOrDefault();
            if (!quantityUnknown && installedQuantity <= 0)
            {
                spareOnly++;
                continue;
            }

            ComponentIR? component = null;
            try
            {
                component = await componentLookup(manufacturer, model, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Topology import must still expose unresolved device-like BOM rows when knowledge lookup fails.
                component = null;
            }

            // Cable, wire, harness and cable-assembly rows are material for an electrical connection.
            // Do not create a sensor/device-looking node just because the material appears in the BOM.
            // Classification is intentionally based only on structured Component IR facts; unknown stays visible.
            if (component is not null &&
                ComponentMaterialRolePolicy.Classify(component) == BomTopologyDisposition.DeferredConnectionMaterial)
            {
                deferredConnectionMaterial++;
                AddConnectionMaterial(connectionMaterials, component, row);
                if (quantityUnknown) unknownQuantity++;
                continue;
            }

            if (quantityUnknown)
            {
                unknownQuantity++;
                var instanceId = BuildStableInstanceId(row, manufacturer, model, 1);
                if (project.Components.Any(item => string.Equals(item.ComponentInstanceId, instanceId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                project.Components.Add(new ComponentInstance
                {
                    ComponentInstanceId = instanceId,
                    ComponentDefinitionId = $"bom-unresolved:{Sanitize(row.RowId)}:{Sanitize(manufacturer)}:{Sanitize(model)}",
                    TypeKey = "BOM_ITEM_QTY_UNKNOWN",
                    DisplayName = InstanceDisplayName(manufacturer, model, 1, 1, quantityUnknown: true),
                    ReferenceSource = ReferenceSource.Imported,
                    ResponsibilityScope = ResponsibilityScope.Unknown
                });
                placeholders++;
                added++;
                continue;
            }

            for (var index = 1; index <= installedQuantity; index++)
            {
                var instanceId = BuildStableInstanceId(row, manufacturer, model, index);
                if (project.Components.Any(item => string.Equals(item.ComponentInstanceId, instanceId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                ComponentInstance instance;
                if (component is not null)
                {
                    instance = _bridge.CreateInstance(component, instanceId);
                    instance.Footprint ??= ComponentPhysicalKnowledgeMapper.TryCreateFootprint(component);
                    instance.DisplayName = InstanceDisplayName(manufacturer, model, index, installedQuantity, quantityUnknown: false);
                    instance.ResponsibilityScope = ResponsibilityScope.InScope;
                    rich++;
                }
                else
                {
                    instance = new ComponentInstance
                    {
                        ComponentInstanceId = instanceId,
                        ComponentDefinitionId = $"bom-unresolved:{Sanitize(row.RowId)}:{Sanitize(manufacturer)}:{Sanitize(model)}",
                        TypeKey = "BOM_ITEM",
                        DisplayName = InstanceDisplayName(manufacturer, model, index, installedQuantity, quantityUnknown: false),
                        ReferenceSource = ReferenceSource.Imported,
                        ResponsibilityScope = ResponsibilityScope.Unknown
                    };
                    placeholders++;
                }

                project.Components.Add(instance);
                added++;
            }
        }

        return new BomTopologySyncResult(
            rows.Count,
            added,
            rich,
            placeholders,
            unknownQuantity,
            spareOnly,
            deferredConnectionMaterial)
        {
            ConnectionMaterials = connectionMaterials.Values
                .OrderBy(item => item.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Model, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void AddConnectionMaterial(
        IDictionary<string, BomConnectionMaterialOption> materials,
        ComponentIR component,
        BomRow row)
    {
        var definitionId = component.Identity.ComponentId.Trim();
        var manufacturer = component.Identity.Manufacturer.Trim();
        var model = component.Identity.Model.Trim();
        var category = string.IsNullOrWhiteSpace(component.Classification.Category)
            ? "Connection Material"
            : component.Classification.Category.Trim();
        var quantity = row.UsedQuantity;

        if (materials.TryGetValue(definitionId, out var existing))
        {
            quantity = existing.AvailableQuantity is int previous && quantity is int current
                ? previous + current
                : null;
        }

        materials[definitionId] = new BomConnectionMaterialOption(
            definitionId,
            manufacturer,
            model,
            category,
            quantity);
    }

    private static string InstanceDisplayName(string manufacturer, string model, int index, int count, bool quantityUnknown)
    {
        var baseName = $"{manufacturer} {model}".Trim();
        if (quantityUnknown) return $"{baseName} · Qty ?";
        return count > 1 ? $"{baseName} · #{index}" : baseName;
    }

    private static string BuildStableInstanceId(BomRow row, string manufacturer, string model, int index) =>
        $"bom:{Sanitize(row.RowId)}:{Sanitize(manufacturer)}:{Sanitize(model)}:{index}";

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var normalized = value.Trim().Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray();
        return string.Join('-', new string(normalized).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
