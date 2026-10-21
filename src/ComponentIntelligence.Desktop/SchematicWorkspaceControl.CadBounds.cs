using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private static Rect CadContentBounds(SchematicDocument doc, SchematicSymbol symbol, SchematicSymbolOwner owner)
    {
        var cad = SchematicSymbolPresentation.GeometryForOwner(symbol, owner)!;
        return CadContentBounds(doc, symbol, owner, CadCanvas(SchematicSymbolPresentation.BodyGeometry(cad)),
            CadCanvas(SchematicSymbolPresentation.UprightCadText(symbol, cad)));
    }

    private static Rect CadContentBounds(SchematicDocument doc, SchematicSymbol symbol, SchematicSymbolOwner owner,
        Canvas drawing, Canvas labels)
    {
        // The native coordinate frame stays unchanged; only the editor's visible frame fits the ink.
        var bounds = Rect.Empty;
        var ink = CanvasInkBounds(drawing);
        if (!ink.IsEmpty)
            foreach (var corner in new[] { ink.TopLeft, ink.TopRight, ink.BottomLeft, ink.BottomRight })
            {
                var world = SchematicSymbolPresentation.WorldPoint(symbol, new(corner.X / 3, corner.Y / 3));
                bounds.Union(new Point(world.X, world.Y));
            }
        var textInk = CanvasInkBounds(labels);
        if (!textInk.IsEmpty) bounds.Union(new Rect(textInk.X / 3, textInk.Y / 3, textInk.Width / 3, textInk.Height / 3));
        var native = SchematicPortPresentation.GenericBodyBounds(doc, symbol, owner);
        foreach (var anchor in symbol.Anchors.Where(a => !SchematicPortPresentation.IsCollapsedPin(doc, symbol, owner, a)))
        {
            var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
            bounds.Union(new Rect(point.X - 7d / 6, point.Y - 7d / 6, 7d / 3, 7d / 3));
        }
        foreach (var port in owner.Ports.Where(p => SchematicPortPresentation.IsRepresented(symbol, p) &&
            (SchematicPortPresentation.IsPortCollapsed(symbol, p) || SchematicPortPresentation.HasPortRoute(doc, symbol, p.PortId))))
        {
            var point = SchematicPortPresentation.GroupContact(doc, symbol, owner, port, native).Position;
            bounds.Union(new Rect(point.X - 1.5, point.Y - 1.5, 3, 3));
        }
        if (bounds.IsEmpty) return new(native.X, native.Y, native.Width, native.Height);
        bounds.Inflate(1, 1);
        return bounds;
    }

    private static Rect CanvasInkBounds(Canvas canvas)
    {
        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        var bounds = Rect.Empty;
        foreach (FrameworkElement child in canvas.Children)
        {
            if (child is TextBlock text && string.IsNullOrWhiteSpace(text.Text)) continue;
            var ink = VisualTreeHelper.GetDescendantBounds(child);
            if (ink.IsEmpty) continue;
            ink = child.TransformToAncestor(canvas).TransformBounds(ink);
            // Subpixel source annotations remain rendered, but must not enlarge the selection frame.
            if (child is TextBlock && ink.Height < .01) continue;
            bounds.Union(ink);
        }
        return bounds;
    }
}
