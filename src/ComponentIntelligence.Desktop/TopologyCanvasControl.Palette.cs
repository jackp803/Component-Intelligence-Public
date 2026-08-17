using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private const string TopologyPaletteDataFormat = "ComponentIntelligence.Topology.PaletteItem";
    private Point _topologyPaletteMouseDown;
    private bool _topologyPaletteConfigured;
    private string? _topologyPaletteSignature;

    private void TopologyCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        if (_topologyPaletteConfigured) return;
        _topologyPaletteConfigured = true;

        // The palette is intentionally presentation-only. It moves an existing TopologyPlacement;
        // it never duplicates a BOM component or changes archived engineering data.
        ProjectChanged += TopologyCanvas_ProjectChangedRefreshVisuals;
        Surface.LayoutUpdated += (_, _) => RefreshTopologyPaletteIfNeeded();

        RefreshTopologyPaletteIfNeeded(force: true);
        ScheduleFinalTopologyVisualRefresh();
    }

    private void TopologyCanvas_ProjectChangedRefreshVisuals(object? sender, EventArgs e)
    {
        RefreshTopologyPaletteIfNeeded(force: true);
        ScheduleFinalTopologyVisualRefresh();
    }

    private void ScheduleFinalTopologyVisualRefresh()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_project is null || Surface is null) return;

            // A completed connection must never fall back to the old center-to-center Line. Rebuild
            // endpoint geometry after the component/Pin markers have their final sizes and positions,
            // then render the formal orthogonal route from the actual Endpoint marker centers.
            foreach (var legacy in Surface.Children.OfType<Line>().Where(line => line.Tag is string))
                legacy.Visibility = Visibility.Collapsed;

            ApplyRotatedPortVisuals();
            ApplyTerminalJunctionVisuals();
            EnsureEndpointModeVisuals();
            EnsureOrthogonalConnectionVisuals();
        }));
    }

    private void TopologyPalette_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _topologyPaletteMouseDown = e.GetPosition(TopologyPalette);
    }

    private void TopologyPalette_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || TopologyPalette.SelectedItem is not TopologyPaletteItem item)
            return;

        var current = e.GetPosition(TopologyPalette);
        if (Math.Abs(current.X - _topologyPaletteMouseDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _topologyPaletteMouseDown.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(
            TopologyPalette,
            new DataObject(TopologyPaletteDataFormat, item),
            DragDropEffects.Move);
    }

    private void Surface_TopologyPaletteDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TopologyPaletteDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Surface_TopologyPaletteDrop(object sender, DragEventArgs e)
    {
        if (_project is null || e.Data.GetData(TopologyPaletteDataFormat) is not TopologyPaletteItem item)
            return;

        _projection.EnsurePlacements(_project);
        var placement = _project.TopologyPlacements.FirstOrDefault(candidate =>
            string.Equals(candidate.ObjectId, item.ObjectId, StringComparison.OrdinalIgnoreCase));
        if (placement is null) return;

        var point = e.GetPosition(Surface);
        var maxX = Math.Max(0, Surface.Width - placement.Width);
        var maxY = Math.Max(0, Surface.Height - placement.Height);
        var x = Math.Clamp(point.X - placement.Width / 2d, 0, maxX);
        var y = Math.Clamp(point.Y - placement.Height / 2d, 0, maxY);

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Move topology node {item.ObjectId} from palette"));
        _projection.Move(_project, item.ObjectId, x, y);

        SelectionText.Text = item.Label;
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = $"已將 {item.Label} 移到 X={x:0}, Y={y:0}。可繼續從左側拖其他元件，或直接在畫布上拖曳微調後再接線。";

        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void RefreshTopologyPaletteIfNeeded(bool force = false)
    {
        if (_project is null || TopologyPalette is null) return;

        _projection.EnsurePlacements(_project);
        var placementById = _project.TopologyPlacements.ToDictionary(
            placement => placement.ObjectId,
            StringComparer.OrdinalIgnoreCase);

        var items = new List<TopologyPaletteItem>();
        foreach (var component in _project.Components)
        {
            placementById.TryGetValue(component.ComponentInstanceId, out var placement);
            var label = component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId;
            items.Add(new TopologyPaletteItem(
                component.ComponentInstanceId,
                "COMPONENT",
                label,
                BuildPaletteDisplay(label, "Component｜元件", placement)));
        }

        foreach (var block in _project.TerminalBlocks)
        {
            placementById.TryGetValue(block.TerminalBlockId, out var placement);
            var label = string.IsNullOrWhiteSpace(block.FunctionTag)
                ? block.ReferenceDesignator
                : $"{block.ReferenceDesignator} / {block.FunctionTag}";
            items.Add(new TopologyPaletteItem(
                block.TerminalBlockId,
                "TERMINAL_BLOCK",
                label,
                BuildPaletteDisplay(label, "Terminal Block｜端子台", placement)));
        }

        var ordered = items
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var signature = string.Join("|", ordered.Select(item => $"{item.ObjectId}:{item.Display}"));
        if (!force && string.Equals(signature, _topologyPaletteSignature, StringComparison.Ordinal)) return;

        _topologyPaletteSignature = signature;
        TopologyPalette.ItemsSource = ordered;
    }

    private static string BuildPaletteDisplay(string label, string kind, ComponentIntelligence.Electrical.Domain.TopologyPlacement? placement)
    {
        var position = placement is null
            ? "未定位"
            : $"X {placement.X.ToString("0", CultureInfo.InvariantCulture)}  Y {placement.Y.ToString("0", CultureInfo.InvariantCulture)}";
        return $"{label}\n{kind} · {position}";
    }

    private sealed record TopologyPaletteItem(
        string ObjectId,
        string ObjectKind,
        string Label,
        string Display);
}
