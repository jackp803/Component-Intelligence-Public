using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private readonly Dictionary<string, SchematicCableLabel> _renderWireLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Symbol, string Pin), string> _renderPinLabels = new();
    private readonly Dictionary<string, string> _wireLabelWarnings = new(StringComparer.Ordinal);

    private void RelayoutWireLabels()
    {
        if (_renderingOutput || _gestureStart is not null) return;
        var project = _getProject();
        foreach (var page in project.Schematic?.Pages ?? [])
        {
            if (!_pageCanvases.TryGetValue(page.PageId, out var canvas) || canvas.Visibility != Visibility.Visible) continue;
            _renderCanvas = canvas;
            try
            {
                foreach (var element in canvas.Children.OfType<FrameworkElement>().Where(e => e.Tag is string tag &&
                    (tag.StartsWith("wire-label:") || tag.StartsWith("wire-leader:") || tag == "wire-label-warning")).ToArray())
                    canvas.Children.Remove(element);
                canvas.Height = page.Height * 3;
                var crossings = SchematicCrossingService.Analyze(project.Schematic!.Wires.Where(w => w.PageId == page.PageId).ToArray());
                PrepareWireLabels(project, page, crossings);
                RenderWireLabels(project, page, crossings);
            }
            finally { _renderCanvas = null; }
        }
    }

    private void PrepareWireLabels(ElectricalProject project, SchematicPage page, SchematicCrossingResult crossings)
    {
        _renderWireLabels.Clear(); _renderPinLabels.Clear();
        foreach (var wire in project.Schematic!.Wires.Where(w => w.PageId == page.PageId))
        {
            var caption = SchematicCableLabelPresentation.Resolve(project, wire, crossings);
            if (caption is null) continue;
            _renderWireLabels.Add(wire.WireId, caption);
            if (SchematicWireContacts.Ends(project, wire).Count < 2) continue;
            foreach (var end in new[] { wire.Start, wire.End }.Where(a => a.Kind == SchematicAttachmentKind.Pin))
            {
                var contact = SchematicWireContacts.Resolve(project, end);
                if (contact?.Pin is { } pin) _renderPinLabels[(end.SymbolId!, end.EndpointId!)] = pin.PinNumber;
            }
        }
    }

    private void RenderWireLabels(ElectricalProject project, SchematicPage page, SchematicCrossingResult crossings)
    {
        var doc = project.Schematic!;
        var occupied = doc.Symbols.Where(s => s.PageId == page.PageId).Select(s =>
        {
            var body = SchematicPortPresentation.GenericBodyBounds(doc, s, SchematicSymbolOwner.Resolve(project, s));
            var box = new Rect(body.X * 3, body.Y * 3, body.Width * 3, body.Height * 3); box.Inflate(6, 6); return box;
        }).ToList();
        foreach (var text in Sheet.Children.OfType<TextBlock>())
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = Canvas.GetLeft(text); var y = Canvas.GetTop(text);
            if (double.IsFinite(x) && double.IsFinite(y))
            {
                var box = new Rect(x, y, text.DesiredSize.Width, text.DesiredSize.Height); box.Inflate(3, 3); occupied.Add(box);
            }
        }
        var wires = doc.Wires.Where(w => w.PageId == page.PageId).ToArray();
        double Padding(SchematicWire w)
        {
            var zoom = _renderingOutput ? 1 : Zoom.Value;
            var stroke = Math.Max(1.5 / zoom, SchematicWirePresentation.StrokeWidthMm(SchematicWirePresentation.Resolve(project, w).Awg) * 3);
            stroke += w.WireId == _selectionId ? 1 : SchematicWireEvidence.Resolve(project, w).NeedsOutline ? 2 / zoom : 0;
            return Math.Max(3, stroke / 2 + 2);
        }
        var paddings = wires.ToDictionary(w => w.WireId, Padding, StringComparer.Ordinal);
        var wireLines = wires.SelectMany(w => w.Points.Zip(w.Points.Skip(1)).Select(e =>
            { var r = new Rect(new Point(e.First.X * 3, e.First.Y * 3), new Point(e.Second.X * 3, e.Second.Y * 3)); r.Inflate(paddings[w.WireId], paddings[w.WireId]); return r; }))
            .Concat(crossings.Crossovers.Select(c =>
            {
                var radius = 4.5 + paddings[c.HorizontalWireId];
                return new Rect(c.Position.X * 3 - radius, c.Position.Y * 3 - radius, radius * 2, radius * 2);
            })).ToArray();
        _wireLabelWarnings.Remove(page.PageId);
        var warnings = new List<string>();
        foreach (var wire in wires)
        {
            if (!_renderWireLabels.TryGetValue(wire.WireId, out var caption)) continue;
            var label = new TextBlock { Text = caption.Text, FontSize = 10.5, Foreground = Brushes.Black,
                Background = Brushes.White, Padding = new Thickness(2, 0, 2, 0), TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Min(540, (page.Width - 2 * page.Margin) * 3), Tag = "wire-label:" + wire.WireId,
                IsHitTestVisible = !_renderingOutput && !_wireMode, Cursor = Cursors.SizeAll,
                ToolTip = caption.Text + "\n" + (wire.SizingProposal?.Review ?? "") };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = label.DesiredSize;
            var margin = page.Margin * 3;
            var gap = Math.Max(5, paddings[wire.WireId] + 1);
            var desired = wire.LabelPosition is { } saved ? new Point(saved.X * 3, saved.Y * 3) :
                new Point(caption.Center.X * 3, caption.Center.Y * 3 - size.Height / 2 - gap);
            Rect At(Point p) => new(p.X - size.Width / 2, p.Y - size.Height / 2, size.Width, size.Height);
            bool Fits(Rect r) => r.Left >= margin && r.Top >= margin && r.Right <= page.Width * 3 - margin &&
                r.Bottom <= page.Height * 3 - margin && !occupied.Any(o => o.IntersectsWith(r)) && !wireLines.Any(o => o.IntersectsWith(r));
            Rect? chosen = null;
            if (Fits(At(desired))) chosen = At(desired);
            if (chosen is null && wire.LabelPosition is null)
                for (var x = 0d; x <= caption.AvailableLength * 1.5 && chosen is null; x += 18)
                foreach (var offset in new[] { x, -x })
                foreach (var side in new[] { -1, 1 })
                {
                    var point = new Point(caption.Center.X * 3 + offset, caption.Center.Y * 3 + side * (size.Height / 2 + gap));
                    if (Fits(At(point))) { chosen = At(point); break; }
                }
            if (chosen is null)
            {
                for (var radius = 14d; radius <= Math.Max(page.Width, page.Height) * 3 && chosen is null; radius += 14)
                for (var x = -radius; x <= radius && chosen is null; x += 24)
                foreach (var y in new[] { -radius, radius })
                {
                    var candidate = new Point(desired.X + x, desired.Y + y);
                    if (Fits(At(candidate))) { chosen = At(candidate); break; }
                }
            }
            if (chosen is not { } box)
            {
                if (_renderingOutput) throw new InvalidOperationException("導線標籤沒有足夠空間，請拉開模塊或移到其他頁面。");
                warnings.Add(caption.Text);
                continue;
            }
            var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            var onWire = new Point(caption.Center.X * 3, caption.Center.Y * 3);
            var endpoint = new Point(Math.Clamp(onWire.X, box.Left, box.Right), Math.Clamp(onWire.Y, box.Top, box.Bottom));
            if ((endpoint - onWire).Length > 6)
            {
                var color = (Brush)new BrushConverter().ConvertFromString(SchematicWireEvidence.Resolve(project, wire).ColorHex)!;
                if (color.ToString() == "#FFFFFFFF") color = Brushes.DimGray;
                var leader = new Line { X1 = onWire.X, Y1 = onWire.Y, X2 = endpoint.X, Y2 = endpoint.Y,
                    Stroke = color, StrokeThickness = 1, StrokeDashArray = new([2, 3]), IsHitTestVisible = false, Opacity = .7,
                    Tag = "wire-leader:" + wire.WireId };
                Panel.SetZIndex(leader, -1); Sheet.Children.Add(leader);
            }
            Canvas.SetLeft(label, box.X); Canvas.SetTop(label, box.Y); Sheet.Children.Add(label);
            var blocked = box; blocked.Inflate(4, 4); occupied.Add(blocked);
            if (!_renderingOutput) label.MouseLeftButtonDown += (_, e) =>
            {
                if (_wireMode || wire.Locked) return;
                _selectionId = wire.WireId; BeginGesture(e); _dragWireLabel = wire.WireId;
                _dragWireLabelOrigin = new(center.X / 3, center.Y / 3); e.Handled = true;
            };
        }
        if (warnings.Count > 0)
        {
            const string message = "導線標籤空間不足，未放入圖紙；請拉開模塊或移到其他頁面後再匯出。";
            _wireLabelWarnings[page.PageId] = message;
            var warning = new TextBlock { Text = message + "\n" + string.Join("\n", warnings), Foreground = Brushes.Firebrick,
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = (page.Width - 2 * page.Margin) * 3,
                Tag = "wire-label-warning", IsHitTestVisible = false };
            warning.Measure(new Size(warning.Width, double.PositiveInfinity));
            Canvas.SetLeft(warning, page.Margin * 3); Canvas.SetTop(warning, page.Height * 3 + 12);
            Sheet.Children.Add(warning); Sheet.Height += warning.DesiredSize.Height + 24;
            Status.Text = message;
        }
    }
}
