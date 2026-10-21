using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private bool PositionMatingCaption(ElectricalProject project, SchematicPage page, SchematicMarker marker, TextBlock caption)
    {
        var doc = project.Schematic!;
        var point = new Point(marker.Position.X * 3, marker.Position.Y * 3);
        var arrow = SchematicContinuationArrow.Points(doc, marker);
        var left = arrow[0].X < (arrow[1].X + arrow[2].X) / 2;
        var occupied = new List<Rect>();
        foreach (var text in Sheet.Children.OfType<TextBlock>())
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = Canvas.GetLeft(text); var y = Canvas.GetTop(text);
            if (!double.IsFinite(x) || !double.IsFinite(y)) continue;
            var box = text.RenderTransform.TransformBounds(new Rect(text.DesiredSize));
            box.Offset(x, y); box.Inflate(3, 3); occupied.Add(box);
        }
        foreach (var symbol in doc.Symbols.Where(s => s.PageId == page.PageId))
        {
            var owner = SchematicSymbolOwner.Resolve(project, symbol);
            var body = SchematicPortPresentation.GenericBodyBounds(doc, symbol, owner);
            var bounds = symbol.Geometry is not null ? CadContentBounds(doc, symbol, owner) :
                new Rect(body.X, body.Y, body.Width, body.Height);
            occupied.Add(new(bounds.X * 3, bounds.Y * 3, bounds.Width * 3, bounds.Height * 3));
        }
        occupied.AddRange(_renderCableDetailBounds.GetValueOrDefault(page.PageId) ?? []);
        if (page.TemplateGeometry is not null)
            occupied.Add(new Rect(Math.Max(page.Margin, page.Width - 185) * 3, (page.Height - 65) * 3, 185 * 3, 65 * 3));
        var lines = doc.Wires.Where(w => w.PageId == page.PageId).SelectMany(w => w.Points.Zip(w.Points.Skip(1)))
            .Select(line => { var box = new Rect(new Point(line.First.X * 3, line.First.Y * 3), new Point(line.Second.X * 3, line.Second.Y * 3)); box.Inflate(3, 3); return box; }).ToArray();
        var margin = page.Margin * 3;
        // Narrow paper-edge corridors can wrap the complete caption without moving it away from its arrow.
        foreach (var maxWidth in new[] { double.PositiveInfinity, 180, 120, 90, 60, 42, 28 })
        {
            caption.MaxWidth = maxWidth; caption.TextWrapping = double.IsFinite(maxWidth) ? TextWrapping.Wrap : TextWrapping.NoWrap;
            caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = caption.DesiredSize.Width; var height = caption.DesiredSize.Height;
            if (width > (page.Width - 2 * page.Margin) * 3 || height > (page.Height - 2 * page.Margin) * 3) continue;
            foreach (var gap in new[] { 9d, 18, 27, 36 })
            foreach (var below in new[] { false, true })
            foreach (var alignLeft in new[] { left, !left })
            {
                var box = new Rect(Math.Clamp(alignLeft ? point.X - width - 6 : point.X + 6, margin, page.Width * 3 - margin - width),
                    Math.Clamp(below ? point.Y + gap : point.Y - height - gap, margin, page.Height * 3 - margin - height), width, height);
                if (occupied.Any(o => o.IntersectsWith(box)) || lines.Any(o => o.IntersectsWith(box))) continue;
                Canvas.SetLeft(caption, box.X); Canvas.SetTop(caption, box.Y);
                caption.Tag = "continuation-label:" + marker.MarkerId;
                return true;
            }
        }
        return false;
    }
}
