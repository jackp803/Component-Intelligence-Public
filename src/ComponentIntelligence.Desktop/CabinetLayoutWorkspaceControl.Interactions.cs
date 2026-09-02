using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;

namespace ComponentIntelligence.Desktop;

public sealed partial class CabinetLayoutWorkspaceControl
{
    private const double MinimumLayoutZoom = 0.25d;
    private const double MaximumLayoutZoom = 4d;

    private readonly ScrollViewer _layoutScrollViewer = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly ScaleTransform _layoutZoomTransform = new(1d, 1d);
    private readonly HashSet<LayoutSelection> _selectedLayoutItems = [];
    private double _layoutZoom = 1d;
    private Point _layoutMarqueeStart;
    private bool _layoutMarqueeCaptured;
    private bool _layoutMarqueeAdditive;
    private Rectangle? _layoutMarqueeRectangle;

    private void ConfigureLayoutInteractions()
    {
        _canvas.LayoutTransform = _layoutZoomTransform;
        _layoutScrollViewer.PreviewMouseWheel += LayoutScrollViewer_PreviewMouseWheel;
        _canvas.PreviewMouseLeftButtonDown += LayoutCanvas_PreviewMouseLeftButtonDown;
        _canvas.PreviewMouseMove += LayoutCanvas_PreviewMouseMove;
        _canvas.PreviewMouseLeftButtonUp += LayoutCanvas_PreviewMouseLeftButtonUp;
    }

    private void LayoutScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var previous = _layoutZoom;
        var factor = e.Delta > 0 ? 1.1d : 1d / 1.1d;
        var next = Math.Clamp(previous * factor, MinimumLayoutZoom, MaximumLayoutZoom);
        if (Math.Abs(next - previous) < 0.0001d)
        {
            e.Handled = true;
            return;
        }

        var pointer = e.GetPosition(_layoutScrollViewer);
        var logicalX = (_layoutScrollViewer.HorizontalOffset + pointer.X) / previous;
        var logicalY = (_layoutScrollViewer.VerticalOffset + pointer.Y) / previous;
        _layoutZoom = next;
        _layoutZoomTransform.ScaleX = next;
        _layoutZoomTransform.ScaleY = next;
        _layoutScrollViewer.UpdateLayout();
        _layoutScrollViewer.ScrollToHorizontalOffset(logicalX * next - pointer.X);
        _layoutScrollViewer.ScrollToVerticalOffset(logicalY * next - pointer.Y);
        _status($"Layout 縮放：{next:P0}。Ctrl + 滾輪繼續縮放；普通滾輪維持捲動。");
        e.Handled = true;
    }

    private void LayoutCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsLayoutNodeSource(e.OriginalSource as DependencyObject)) return;
        _layoutMarqueeStart = e.GetPosition(_canvas);
        _layoutMarqueeCaptured = true;
        _layoutMarqueeAdditive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!_layoutMarqueeAdditive)
        {
            _selectedLayoutItems.Clear();
            _selection = null;
        }
        RemoveLayoutMarquee();
        ApplyLayoutSelectionVisuals();
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void LayoutCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_layoutMarqueeCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(_canvas);
        if (_layoutMarqueeRectangle is null &&
            Math.Abs(current.X - _layoutMarqueeStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _layoutMarqueeStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _layoutMarqueeRectangle ??= CreateLayoutMarquee();
        var bounds = NormalizeLayoutBounds(_layoutMarqueeStart, current);
        _layoutMarqueeRectangle.Width = bounds.Width;
        _layoutMarqueeRectangle.Height = bounds.Height;
        Canvas.SetLeft(_layoutMarqueeRectangle, bounds.Left);
        Canvas.SetTop(_layoutMarqueeRectangle, bounds.Top);
        e.Handled = true;
    }

    private void LayoutCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_layoutMarqueeCaptured) return;
        var current = e.GetPosition(_canvas);
        var bounds = NormalizeLayoutBounds(_layoutMarqueeStart, current);
        var madeBox = _layoutMarqueeRectangle is not null && bounds.Width >= 2d && bounds.Height >= 2d;
        _canvas.ReleaseMouseCapture();
        _layoutMarqueeCaptured = false;
        RemoveLayoutMarquee();

        if (madeBox)
        {
            var project = _projectAccessor();
            var container = SelectedContainer(project);
            var surface = _surfaceCombo.SelectedItem is MountingSurface selectedSurface
                ? selectedSurface
                : MountingSurface.Backplate;
            if (container is not null)
            {
                foreach (var target in AllObjects(project).Where(target =>
                             target.Placement is not null && target.Footprint is not null &&
                             string.Equals(target.Placement.ParentContainerId, container.ContainerId, StringComparison.OrdinalIgnoreCase) &&
                             target.Placement.Surface == surface))
                {
                    var projection = PhysicalFootprintProjection.Project(target.Footprint!, target.Placement!);
                    var objectBounds = new Rect(
                        target.Placement!.XMm * _scale,
                        target.Placement.YMm * _scale,
                        projection.WidthMm * _scale,
                        projection.HeightMm * _scale);
                    if (bounds.Contains(objectBounds))
                        _selectedLayoutItems.Add(new LayoutSelection(target.Kind, target.ObjectId));
                }
            }
            _selection = _selectedLayoutItems.LastOrDefault();
        }
        else if (!_layoutMarqueeAdditive)
        {
            _selectedLayoutItems.Clear();
            _selection = null;
        }

        ApplyLayoutSelectionVisuals();
        LoadSelection();
        _status(_selectedLayoutItems.Count == 0
            ? "Layout 未選取物件。"
            : $"Layout 已選取 {_selectedLayoutItems.Count} 個物件；拖曳可群組移動，右鍵可整組旋轉 90°。");
        e.Handled = true;
    }

    private bool SelectLayoutItemForDrag(LayoutSelection clicked)
    {
        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (additive)
        {
            if (!_selectedLayoutItems.Add(clicked))
            {
                _selectedLayoutItems.Remove(clicked);
                _selection = _selectedLayoutItems.LastOrDefault();
                ApplyLayoutSelectionVisuals();
                LoadSelection();
                return false;
            }
        }
        else if (!_selectedLayoutItems.Contains(clicked))
        {
            _selectedLayoutItems.Clear();
            _selectedLayoutItems.Add(clicked);
        }
        _selection = clicked;
        ApplyLayoutSelectionVisuals();
        return true;
    }

    private void ClearLayoutSelection()
    {
        _selectedLayoutItems.Clear();
        _selection = null;
        RemoveLayoutMarquee();
        ApplyLayoutSelectionVisuals();
        LoadSelection();
    }

    private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not NodeTag tag) return;
        var clicked = new LayoutSelection(tag.Kind, tag.ObjectId);
        if (!_selectedLayoutItems.Contains(clicked))
        {
            _selectedLayoutItems.Clear();
            _selectedLayoutItems.Add(clicked);
            _selection = clicked;
        }
        RotateSelectedLayoutItems();
        e.Handled = true;
    }

    private void RotateSelectedLayoutItems()
    {
        if (_selection is null) return;
        var project = _projectAccessor();
        var primary = ResolveObject(project, _selection.ObjectId, _selection.Kind);
        if (primary?.Placement is null) return;
        var origins = ResolvePhysicalDragGroup(project, primary);
        var targets = origins.Keys
            .Select(selection => ResolveObject(project, selection.ObjectId, selection.Kind))
            .Where(target => target?.Placement is not null && target.Footprint is not null)
            .Cast<LayoutTarget>()
            .ToArray();
        if (targets.Length == 0) return;

        var current = targets.Select(target =>
        {
            var projection = PhysicalFootprintProjection.Project(target.Footprint!, target.Placement!);
            return new
            {
                Target = target,
                CenterX = target.Placement!.XMm + projection.WidthMm / 2d,
                CenterY = target.Placement.YMm + projection.HeightMm / 2d
            };
        }).ToArray();
        var minX = current.Min(item => item.Target.Placement!.XMm);
        var minY = current.Min(item => item.Target.Placement!.YMm);
        var maxRight = current.Max(item => item.Target.Placement!.XMm +
            PhysicalFootprintProjection.Project(item.Target.Footprint!, item.Target.Placement).WidthMm);
        var maxBottom = current.Max(item => item.Target.Placement!.YMm +
            PhysicalFootprintProjection.Project(item.Target.Footprint!, item.Target.Placement).HeightMm);
        var groupCenterX = (minX + maxRight) / 2d;
        var groupCenterY = (minY + maxBottom) / 2d;

        _recordMutation($"Rotate {targets.Length} cabinet layout object(s) 90 degrees");
        foreach (var item in current)
        {
            var dx = item.CenterX - groupCenterX;
            var dy = item.CenterY - groupCenterY;
            item.Target.Placement!.RotationDegrees = PhysicalFootprintProjection.NormalizeRotation(
                item.Target.Placement.RotationDegrees + 90);
            var rotated = PhysicalFootprintProjection.Project(item.Target.Footprint!, item.Target.Placement);
            var newCenterX = groupCenterX - dy;
            var newCenterY = groupCenterY + dx;
            item.Target.Placement.XMm = newCenterX - rotated.WidthMm / 2d;
            item.Target.Placement.YMm = newCenterY - rotated.HeightMm / 2d;
        }

        var shiftX = Math.Max(0d, -targets.Min(target => target.Placement!.XMm));
        var shiftY = Math.Max(0d, -targets.Min(target => target.Placement!.YMm));
        foreach (var target in targets)
        {
            target.Placement!.XMm += shiftX;
            target.Placement.YMm += shiftY;
            _selectedLayoutItems.Add(new LayoutSelection(target.Kind, target.ObjectId));
        }

        // Re-pack terminal strips along the narrow projected edge immediately after a group
        // rotation. This preserves a compact DIN-rail strip at 0/90/180/270 degrees.
        _terminalGroupingPolicy.ArrangeContiguously(
            project,
            primary.Placement.ParentContainerId,
            primary.Placement.Surface);

        _projectChanged();
        _status($"已將 {_selectedLayoutItems.Count} 個 Layout 物件以群組中心旋轉 90°。");
        RefreshCanvasAndFit();
        LoadSelection();
    }

    private void ApplyLayoutSelectionVisuals()
    {
        foreach (var border in _canvas.Children.OfType<Border>().Where(border => border.Tag is NodeTag))
        {
            var tag = (NodeTag)border.Tag;
            border.Effect = _selectedLayoutItems.Contains(new LayoutSelection(tag.Kind, tag.ObjectId))
                ? SelectedLayoutEffect()
                : null;
        }
    }

    private static DropShadowEffect SelectedLayoutEffect() => new()
    {
        Color = Colors.DodgerBlue,
        BlurRadius = 14d,
        ShadowDepth = 0d,
        Opacity = 0.85d
    };

    private Rectangle CreateLayoutMarquee()
    {
        var rectangle = new Rectangle
        {
            Stroke = Brushes.DodgerBlue,
            StrokeThickness = 1.5d,
            StrokeDashArray = new DoubleCollection { 4d, 3d },
            Fill = new SolidColorBrush(Color.FromArgb(34, 30, 144, 255)),
            IsHitTestVisible = false
        };
        Panel.SetZIndex(rectangle, 30_000);
        _canvas.Children.Add(rectangle);
        return rectangle;
    }

    private void RemoveLayoutMarquee()
    {
        if (_layoutMarqueeRectangle is null) return;
        _canvas.Children.Remove(_layoutMarqueeRectangle);
        _layoutMarqueeRectangle = null;
    }

    private static Rect NormalizeLayoutBounds(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X),
        Math.Abs(first.Y - second.Y));

    private static bool IsLayoutNodeSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: NodeTag }) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
