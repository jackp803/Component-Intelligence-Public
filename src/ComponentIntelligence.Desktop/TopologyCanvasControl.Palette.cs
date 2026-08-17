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
    private bool _finalTopologyVisualRefreshScheduled;

    private void TopologyCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        if (_topologyPaletteConfigured) return;
        _topologyPaletteConfigured = true;

        // The palette is a queue of objects not yet placed on the canvas. Dragging an item creates
        // exactly one TopologyPlacement and removes it from this queue; the underlying BOM/project
        // component is never deleted or duplicated.
        ProjectChanged += TopologyCanvas_ProjectChangedRefreshVisuals;

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
        if (_finalTopologyVisualRefreshScheduled) return;
        _finalTopologyVisualRefreshScheduled = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _finalTopologyVisualRefreshScheduled = false;
            if (_project is null || Surface is null) return;

            // A completed connection must never fall back to the old center-to-center Line. Rebuild
            // endpoint geometry after the component/Pin markers have their final sizes and positions,
            // then render the formal orthogonal route from the actual Endpoint marker centers.
            foreach (var legacy in Surface.Children.OfType<Line>().Where(line => line.Tag is string))
                legacy.Visibility = Visibility.Collapsed;

            DecorateComponentImages();
            Surface_LayoutUpdated(null, EventArgs.Empty);
            EnsureCanvasContainsProjectContent();
            RefreshTopologyPaletteIfNeeded(force: true);
            ApplyWireLayerVisibility();
            ApplyTopologySelectionVisuals();
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

        var point = e.GetPosition(Surface);
        var placement = _projection.EnsurePlacement(_project, item.ObjectId, point.X, point.Y);
        var shift = ExpandCanvasForBounds(
            point.X - placement.Width / 2d,
            point.Y - placement.Height / 2d,
            point.X + placement.Width / 2d,
            point.Y + placement.Height / 2d);
        point = new Point(point.X + shift.X, point.Y + shift.Y);
        var maxX = Math.Max(0, Surface.Width - placement.Width);
        var maxY = Math.Max(0, Surface.Height - placement.Height);
        var x = Math.Clamp(point.X - placement.Width / 2d, 0, maxX);
        var y = Math.Clamp(point.Y - placement.Height / 2d, 0, maxY);

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Place/move topology node {item.ObjectId} from palette"));
        _projection.Move(_project, item.ObjectId, x, y);

        SelectionText.Text = item.Label;
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = $"已將 {item.Label} 放到 X={x:0}, Y={y:0}。它已從左側待放置清單移除；元件仍完整保存在 BOM／專案中。";

        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void RefreshTopologyPaletteIfNeeded(bool force = false)
    {
        if (_project is null || TopologyPalette is null) return;

        var placementById = _project.TopologyPlacements.ToDictionary(
            placement => placement.ObjectId,
            StringComparer.OrdinalIgnoreCase);

        var items = new List<TopologyPaletteItem>();
        foreach (var component in _project.Components)
        {
            if (placementById.ContainsKey(component.ComponentInstanceId)) continue;
            var label = component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId;
            items.Add(new TopologyPaletteItem(
                component.ComponentInstanceId,
                "COMPONENT",
                label,
                BuildPaletteDisplay(label, "Component｜元件")));
        }

        foreach (var block in _project.TerminalBlocks)
        {
            if (placementById.ContainsKey(block.TerminalBlockId)) continue;
            var label = string.IsNullOrWhiteSpace(block.FunctionTag)
                ? block.ReferenceDesignator
                : $"{block.ReferenceDesignator} / {block.FunctionTag}";
            items.Add(new TopologyPaletteItem(
                block.TerminalBlockId,
                "TERMINAL_BLOCK",
                label,
                BuildPaletteDisplay(label, "Terminal Block｜端子台")));
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

    private static string BuildPaletteDisplay(string label, string kind) =>
        $"{label}\n{kind} · 待放置";

    private sealed record TopologyPaletteItem(
        string ObjectId,
        string ObjectKind,
        string Label,
        string Display);
}
