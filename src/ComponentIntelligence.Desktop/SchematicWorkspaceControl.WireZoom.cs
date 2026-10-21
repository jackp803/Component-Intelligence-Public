using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private sealed record WireStrokeTarget(Shape Shape, double PhysicalWeight, bool Selected, bool Outline, string? WireId);
    private readonly Dictionary<string, List<WireStrokeTarget>> _wireStrokeTargets = new(StringComparer.Ordinal);

    private void TrackWireStrokes(string pageId, FrameworkElement element, double physicalWeight, bool selected, bool outline = false, string? wireId = null)
    {
        if (_renderingOutput) return;
        var shapes = element is Shape shape ? new[] { shape } : element is Canvas canvas ? canvas.Children.OfType<Shape>() : [];
        foreach (var target in shapes)
            _wireStrokeTargets[pageId].Add(new(target, physicalWeight, selected, outline, wireId));
    }

    private void UpdateWireZoom(double zoom)
    {
        // Preserve CAD instances; only the small set of wire strokes depends on the view scale.
        foreach (var (pageId, targets) in _wireStrokeTargets)
        {
            if (!_pageCanvases.TryGetValue(pageId, out var canvas) || canvas.Visibility != Visibility.Visible) continue;
            foreach (var target in targets)
                target.Shape.StrokeThickness = Math.Max(1.5 / zoom, target.PhysicalWeight) + (target.Selected ? 1 : 0) + (target.Outline ? 2 / zoom : 0);
        }
    }
}
