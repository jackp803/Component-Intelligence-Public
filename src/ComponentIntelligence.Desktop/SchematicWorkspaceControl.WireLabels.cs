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
    private Dictionary<string, SchematicWireLegendEntry> _renderWireDetails = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Symbol, string Pin), string> _renderPinLabels = new();
    private readonly Dictionary<string, string> _wireLabelWarnings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Rect>> _renderCableDetailBounds = new(StringComparer.Ordinal);

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
                    (tag.StartsWith("wire-label:") || tag.StartsWith("wire-leader:") || tag.StartsWith("wire-caption-tick:") || tag is "wire-label-warning" or "wire-spec-legend")).ToArray())
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
        _renderWireDetails = SchematicWireLegendPresentation.Entries(project, page.PageId).ToDictionary(e => e.WireId, StringComparer.Ordinal);
        foreach (var wire in project.Schematic!.Wires.Where(w => w.PageId == page.PageId))
        {
            var caption = SchematicCableLabelPresentation.Resolve(project, wire, crossings, _renderWireDetails.GetValueOrDefault(wire.WireId));
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
        // Keep notes outside the company title block and inline manufacturing details.
        if (page.TemplateGeometry is not null)
            occupied.Add(new Rect(Math.Max(page.Margin, page.Width - 185) * 3, (page.Height - 65) * 3, 185 * 3, 65 * 3));
        occupied.AddRange(_renderCableDetailBounds.GetValueOrDefault(page.PageId) ?? []);
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
            }))
            .Concat(doc.Symbols.Where(s => s.PageId == page.PageId).SelectMany(s => s.EndpointDisplayPlacements.SelectMany(p =>
                SchematicAuthoringService.PortFanoutLeadPoints(s, p.EndpointId).Zip(SchematicAuthoringService.PortFanoutLeadPoints(s, p.EndpointId).Skip(1))
                    .Select(segment => { var box = new Rect(new Point(segment.First.X * 3, segment.First.Y * 3), new Point(segment.Second.X * 3, segment.Second.Y * 3)); box.Inflate(2, 2); return box; }))))
            .ToArray();
        _wireLabelWarnings.Remove(page.PageId);
        var warnings = new List<string>();
        var groups = SchematicWireLegendPresentation.Groups(_renderWireDetails.Values);
        var standalone = groups.Where(g => g.Entries.Count == 1).Select(g => g.Entries[0].WireId).ToHashSet(StringComparer.Ordinal);
        var inlineSpecs = new HashSet<string>(StringComparer.Ordinal);
        var compactNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wire in wires)
        {
            if (!_renderWireLabels.TryGetValue(wire.WireId, out var caption)) continue;
            var label = new TextBlock { Text = caption.Text, FontSize = 10.5, Foreground = Brushes.Black,
                Background = Brushes.White, Padding = new Thickness(2, 0, 2, 0), TextWrapping = caption.IsOrdinaryWire ? TextWrapping.NoWrap : TextWrapping.Wrap,
                MaxWidth = Math.Min(540, (page.Width - 2 * page.Margin) * 3), Tag = "wire-label:" + wire.WireId,
                IsHitTestVisible = !_renderingOutput && !_wireMode, Cursor = Cursors.SizeAll,
                ToolTip = caption.FullText };
            var vertical = caption.IsOrdinaryWire && caption.Rotation == -90;
            var size = new Size();
            var margin = page.Margin * 3;
            var gap = Math.Max(4, paddings[wire.WireId] + .5);
            var desired = new Point();
            Rect At(Point p) => new(p.X - size.Width / 2, p.Y - size.Height / 2, size.Width, size.Height);
            bool Fits(Rect r) => r.Left >= margin && r.Top >= margin && r.Right <= page.Width * 3 - margin &&
                r.Bottom <= page.Height * 3 - margin && !occupied.Any(o => o.IntersectsWith(r)) && !wireLines.Any(o => o.IntersectsWith(r));
            Rect? chosen = null;
            var chosenSide = -1;
            bool TryLocal(int preferredSide)
            {
            foreach (var font in caption.IsOrdinaryWire ? new[] { 10.5, 8.5 } : new[] { 10.5 })
            {
                label.FontSize = font;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                size = vertical ? new(label.DesiredSize.Height, label.DesiredSize.Width) : label.DesiredSize;
                var across = vertical ? size.Width : size.Height;
                var along = vertical ? size.Height : size.Width;
                var maximumOffset = Math.Max(0, (caption.AvailableLength * 3 - along) / 2);
                var origin = new Point(caption.Center.X * 3, caption.Center.Y * 3);
                desired = vertical ? new(origin.X + preferredSide * (across / 2 + gap), origin.Y) :
                    new(origin.X, origin.Y + preferredSide * (across / 2 + gap));
                if (wire.LabelPosition is { } saved)
                {
                    var savedPoint = new Point(saved.X * 3, saved.Y * 3);
                    var distance = vertical ? Math.Abs(savedPoint.X - origin.X) : Math.Abs(savedPoint.Y - origin.Y);
                    var offset = vertical ? Math.Abs(savedPoint.Y - origin.Y) : Math.Abs(savedPoint.X - origin.X);
                    var sameSide = vertical ? Math.Sign(savedPoint.X - origin.X) == preferredSide : Math.Sign(savedPoint.Y - origin.Y) == preferredSide;
                    if (!caption.IsOrdinaryWire || sameSide && distance <= across / 2 + gap + 2 && offset <= maximumOffset)
                        desired = savedPoint;
                }
                if (Fits(At(desired)) && (!caption.IsOrdinaryWire || along <= caption.AvailableLength * 3)) chosen = At(desired);
                if (chosen is null && (!caption.IsOrdinaryWire || along <= caption.AvailableLength * 3))
                    for (var step = 0d; step <= maximumOffset && chosen is null; step += 18)
                    foreach (var offset in new[] { step, -step })
                    foreach (var side in caption.IsOrdinaryWire ? new[] { preferredSide } : new[] { -1, 1 })
                    {
                        var point = vertical ? new Point(origin.X + side * (across / 2 + gap), origin.Y + offset) :
                            new Point(origin.X + offset, origin.Y + side * (across / 2 + gap));
                        if (Fits(At(point))) { chosen = At(point); break; }
                    }
                if (chosen is not null) break;
            }
            if (chosen is not null) chosenSide = preferredSide;
            return chosen is not null;
            }
            var baseText = label.Text;
            if (standalone.Contains(wire.WireId) && _renderWireDetails.TryGetValue(wire.WireId, out var detail))
            {
                label.Text += "\n" + detail.Specification.Text;
                if (TryLocal(-1) || TryLocal(1)) inlineSpecs.Add(wire.WireId);
                else label.Text = baseText;
            }
            if (chosen is null) TryLocal(-1);
            if (chosen is null && caption.IsOrdinaryWire) TryLocal(1);
            if (chosen is null && caption.IsOrdinaryWire && _renderWireDetails.TryGetValue(wire.WireId, out var fallback))
            {
                label.Text = fallback.Index + (fallback.ShortFunction.Length > 0 ? " · " + fallback.ShortFunction : "");
                if (TryLocal(-1) || TryLocal(1)) compactNames.Add(wire.WireId);
            }
            if (chosen is null && !caption.IsOrdinaryWire)
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
                warnings.Add(_renderWireDetails.GetValueOrDefault(wire.WireId)?.DisplayName ?? caption.Text);
                continue;
            }
            var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
            if (caption.IsOrdinaryWire && chosenSide == 1)
            {
                var tick = new Line { X1 = vertical ? caption.Center.X * 3 : center.X,
                    Y1 = vertical ? center.Y : caption.Center.Y * 3,
                    X2 = vertical ? box.Left : center.X, Y2 = vertical ? center.Y : box.Top,
                    Stroke = Brushes.DarkCyan, StrokeThickness = .8, IsHitTestVisible = false, Tag = "wire-caption-tick:" + wire.WireId };
                Sheet.Children.Add(tick);
            }
            var onWire = new Point(caption.Center.X * 3, caption.Center.Y * 3);
            var endpoint = new Point(Math.Clamp(onWire.X, box.Left, box.Right), Math.Clamp(onWire.Y, box.Top, box.Bottom));
            if (!caption.IsOrdinaryWire && (endpoint - onWire).Length > 6)
            {
                var color = (Brush)new BrushConverter().ConvertFromString(SchematicWireEvidence.Resolve(project, wire).ColorHex)!;
                if (color.ToString() == "#FFFFFFFF") color = Brushes.DimGray;
                var leader = new Line { X1 = onWire.X, Y1 = onWire.Y, X2 = endpoint.X, Y2 = endpoint.Y,
                    Stroke = color, StrokeThickness = 1, StrokeDashArray = new([2, 3]), IsHitTestVisible = false, Opacity = .7,
                    Tag = "wire-leader:" + wire.WireId };
                Panel.SetZIndex(leader, -1); Sheet.Children.Add(leader);
            }
            if (vertical) label.RenderTransform = new RotateTransform(-90);
            Canvas.SetLeft(label, box.X); Canvas.SetTop(label, vertical ? box.Bottom : box.Y); Sheet.Children.Add(label);
            var blocked = box; blocked.Inflate(4, 4); occupied.Add(blocked);
            if (!_renderingOutput) label.MouseLeftButtonDown += (_, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SelectWire(wire.WireId, true); e.Handled = true; return; }
                if (_wireMode || wire.Locked) return;
                _selectedWires.Clear(); _selectedWires.Add(wire.WireId);
                _selectionId = wire.WireId; BeginGesture(e); _dragWireLabel = wire.WireId;
                _dragWireLabelOrigin = new(center.X / 3, center.Y / 3); e.Handled = true;
            };
        }
        var remaining = groups.Where(g => !g.Entries.All(e => inlineSpecs.Contains(e.WireId))).ToArray();
        if (remaining.Length > 0 || compactNames.Count > 0)
        {
            var legendText = remaining.Select(g => g.Text).Concat(_renderWireDetails.Values.Where(e => compactNames.Contains(e.WireId))
                .Select(e => e.Index + " = " + e.DisplayName + (e.ShortFunction.Length > 0 ? " · " + e.ShortFunction : "")));
            var legend = new TextBlock { Text = string.Join("\n", legendText), FontSize = 10.5,
                Foreground = Brushes.Black, Background = Brushes.White, Padding = new Thickness(4),
                TextWrapping = TextWrapping.Wrap, Width = Math.Min(650, (page.Width - 2 * page.Margin) * 3),
                Tag = "wire-spec-legend", IsHitTestVisible = false };
            legend.Measure(new Size(legend.Width, double.PositiveInfinity));
            Rect? placement = null;
            var margin = page.Margin * 3;
            for (var y = margin + 24; y + legend.DesiredSize.Height <= page.Height * 3 - margin && placement is null; y += 12)
            for (var x = page.Width * 3 - margin - legend.DesiredSize.Width; x >= margin && placement is null; x -= 18)
            {
                var candidate = new Rect(x, y, legend.DesiredSize.Width, legend.DesiredSize.Height);
                if (!occupied.Any(o => o.IntersectsWith(candidate)) && !wireLines.Any(o => o.IntersectsWith(candidate))) placement = candidate;
            }
            if (placement is { } box)
            { Canvas.SetLeft(legend, box.X); Canvas.SetTop(legend, box.Y); Sheet.Children.Add(legend); }
            else
            {
                if (_renderingOutput) throw new InvalidOperationException("導線規格標示沒有足夠空間，請拉開模塊或移到其他頁面。");
                warnings.Add(legend.Text);
            }
        }
        if (warnings.Count > 0)
        {
            const string message = "標示空間不足：請展開 Port 引線或調整圖塊位置；完整資料可在導線明細核對。";
            _wireLabelWarnings[page.PageId] = message;
            Status.Text = message + "\n" + string.Join("、", warnings);
            Status.ToolTip = string.Join("\n", warnings);
        }
    }
}
