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
    private readonly Dictionary<UIElement, TranslateTransform> _dragTranslations = new();
    private readonly List<(SchematicWire Wire, Polyline[] Paths)> _dragIncidentRoutes = [];
    private bool _translationPrepared;
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
        if (!_translationPrepared)
        {
            foreach (var element in _symbolVisuals.GetValueOrDefault(_dragSymbol) ?? [])
            {
                _dragVisualTransforms.Add(element, element.RenderTransform);
                var translation = new TranslateTransform(); _dragTranslations.Add(element, translation);
                element.RenderTransform = new TransformGroup { Children = [element.RenderTransform, translation] };
            }
            var doc = _gestureStart.Schematic!;
            foreach (var marker in Sheet.Children.OfType<FrameworkElement>().Where(e => e.Tag is string tag &&
                (tag == "route-junction" || tag.StartsWith("route-conflict:")))) Hide(marker);
            foreach (var wire in doc.Wires.Where(w => w.PageId == _pageId && (w.Start.SymbolId == _dragSymbol || w.End.SymbolId == _dragSymbol)))
            {
            foreach (var target in _wireStrokeTargets.GetValueOrDefault(wire.PageId) ?? [])
                if (target.WireId == wire.WireId)
                    Hide(target.Shape);
            foreach (var label in Sheet.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "wire-label:" + wire.WireId))) Hide(label);
            foreach (var tick in Sheet.Children.OfType<FrameworkElement>().Where(t => Equals(t.Tag, "wire-caption-tick:" + wire.WireId))) Hide(tick);
            var evidence = SchematicWireEvidence.Resolve(_gestureStart, wire);
            var brush = (Brush)new BrushConverter().ConvertFromString(evidence.ColorHex)!;
            brush.Freeze();
            var paths = new List<Polyline>();
            if (evidence.NeedsOutline) paths.Add(AddRoute(wire.Points.ToList(), Brushes.DimGray, 3.5));
            paths.Add(AddRoute(wire.Points.ToList(), brush, 1.5)); _dragIncidentRoutes.Add((wire, paths.ToArray()));
            }
            _translationPrepared = true;
        }
        foreach (var translation in _dragTranslations.Values) { translation.X = dx * 3; translation.Y = dy * 3; }
        foreach (var (wire, paths) in _dragIncidentRoutes)
        {
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == _dragSymbol) points = TranslateEndpoint(points, dx, dy, true);
            if (wire.End.SymbolId == _dragSymbol) points = TranslateEndpoint(points, dx, dy, false);
            foreach (var path in paths) path.Points = new PointCollection(points.Select(p => new Point(p.X * 3, p.Y * 3)));
        }

        void Hide(UIElement element)
        {
            if (!_dragWireVisibility.ContainsKey(element)) _dragWireVisibility.Add(element, element.Visibility);
            element.Visibility = Visibility.Collapsed;
        }
        Polyline AddRoute(List<SchematicPoint> points, Brush brush, double weight)
        {
            var path = Path(points, brush, weight); path.IsHitTestVisible = false; path.Opacity = .8;
            Sheet.Children.Add(path); _dragWirePreviews.Add(path);
            return path;
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
        _dragTranslations.Clear(); _dragIncidentRoutes.Clear(); _translationPrepared = false;
    }
}
