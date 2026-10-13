using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private readonly Dictionary<string, UIElement[]> _symbolVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<UIElement, Transform> _dragVisualTransforms = new();
    private readonly Dictionary<UIElement, Visibility> _dragWireVisibility = new();
    private readonly List<Polyline> _dragWirePreviews = [];
    private Polyline? _wirePreviewElement;
    private Canvas? _wirePreviewOwner;

    private void ClearWirePreview()
    {
        if (_wirePreviewElement is not null) _wirePreviewOwner?.Children.Remove(_wirePreviewElement);
        _wirePreviewElement = null; _wirePreviewOwner = null;
    }

    private void RenderTrackedSymbol(ElectricalProject project, SchematicSymbol symbol)
    {
        var first = Sheet.Children.Count;
        RenderSymbol(project, symbol);
        if (!_renderingOutput) _symbolVisuals[symbol.SymbolId] = Sheet.Children.Cast<UIElement>().Skip(first).ToArray();
    }

    private void PreviewSymbolTranslation()
    {
        if (_gestureStart is null || _dragStart is null || _pointer is null || _dragSymbol is null) return;
        var dx = _pointer.X - _dragStart.X; var dy = _pointer.Y - _dragStart.Y;
        foreach (var element in _symbolVisuals.GetValueOrDefault(_dragSymbol) ?? [])
        {
            if (!_dragVisualTransforms.ContainsKey(element)) _dragVisualTransforms.Add(element, element.RenderTransform);
            element.RenderTransform = new TransformGroup { Children = [_dragVisualTransforms[element], new TranslateTransform(dx * 3, dy * 3)] };
        }
        foreach (var old in _dragWirePreviews) Sheet.Children.Remove(old);
        _dragWirePreviews.Clear();
        var doc = _gestureStart.Schematic!;
        foreach (var wire in doc.Wires.Where(w => w.PageId == _pageId && (w.Start.SymbolId == _dragSymbol || w.End.SymbolId == _dragSymbol)))
        {
            foreach (var target in _wireStrokeTargets.GetValueOrDefault(wire.PageId) ?? [])
                if (target.WireId == wire.WireId)
                    Hide(target.Shape);
            foreach (var label in Sheet.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "wire-label:" + wire.WireId))) Hide(label);
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == _dragSymbol) points = TranslateEndpoint(points, dx, dy, true);
            if (wire.End.SymbolId == _dragSymbol) points = TranslateEndpoint(points, dx, dy, false);
            var evidence = SchematicWireEvidence.Resolve(_gestureStart, wire);
            var brush = (Brush)new BrushConverter().ConvertFromString(evidence.ColorHex)!;
            if (evidence.NeedsOutline) AddRoute(points, Brushes.DimGray, 3.5);
            AddRoute(points, brush, 1.5);
        }

        void Hide(UIElement element)
        {
            if (!_dragWireVisibility.ContainsKey(element)) _dragWireVisibility.Add(element, element.Visibility);
            element.Visibility = Visibility.Collapsed;
        }
        void AddRoute(List<SchematicPoint> points, Brush brush, double weight)
        {
            var path = Path(points, brush, weight); path.IsHitTestVisible = false; path.Opacity = .8;
            Sheet.Children.Add(path); _dragWirePreviews.Add(path);
        }
    }

    private static List<SchematicPoint> TranslateEndpoint(List<SchematicPoint> route, double dx, double dy, bool start)
    {
        if (!start) route.Reverse();
        var old = route[0]; var next = route[1];
        var point = new SchematicPoint(old.X + dx, old.Y + dy);
        var lead = new SchematicPoint(point.X + Math.Sign(next.X - old.X) * 5, point.Y + Math.Sign(next.Y - old.Y) * 5);
        var result = new List<SchematicPoint> { point, lead };
        foreach (var tail in route.Skip(route.Count > 2 ? 2 : 1)) AppendOrthogonal(result, tail);
        if (!start) result.Reverse();
        return result;
    }

    private void ClearSymbolTranslation()
    {
        foreach (var (element, transform) in _dragVisualTransforms) element.RenderTransform = transform;
        foreach (var (element, visibility) in _dragWireVisibility) element.Visibility = visibility;
        foreach (var path in _dragWirePreviews) (path.Parent as Canvas)?.Children.Remove(path);
        _dragVisualTransforms.Clear(); _dragWireVisibility.Clear(); _dragWirePreviews.Clear();
    }
}
