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

    private sealed record InlineWireCaption(TextBlock Label, Rect Bounds, SchematicLabelSpan Span, int Side);

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
        occupied.AddRange(SchematicWagoGroups.Build(doc, page.PageId).Select(g =>
            new Rect(g.Bounds.X * 3 - 6, g.Bounds.Y * 3 - 6, g.Bounds.Width * 3 + 12, g.Bounds.Height * 3 + 12)));
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
        var margin = page.Margin * 3;
        bool Fits(Rect box) => box.Left >= margin && box.Top >= margin && box.Right <= page.Width * 3 - margin &&
            box.Bottom <= page.Height * 3 - margin && !occupied.Any(o => o.IntersectsWith(box)) && !wireLines.Any(o => o.IntersectsWith(box));

        InlineWireCaption? Place(SchematicWire wire, SchematicCableLabel caption, params (string Text, bool Wrap)[] variants)
        {
            var spans = caption.Spans.Count > 0 ? caption.Spans : [new SchematicLabelSpan(caption.Center, caption.Rotation, caption.AvailableLength)];
            foreach (var font in new[] { 10.5, 8.5 })
            foreach (var variant in variants)
            foreach (var span in spans)
            {
                if (span.AvailableLength <= 0) continue;
                var label = new TextBlock { Text = variant.Text, FontSize = font, Foreground = Brushes.Black,
                    Padding = new Thickness(2, 0, 2, 0), TextWrapping = variant.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MaxWidth = variant.Wrap ? Math.Min(540, span.AvailableLength * 3) : double.PositiveInfinity,
                    Tag = "wire-label:" + wire.WireId, IsHitTestVisible = !_renderingOutput && !_wireMode,
                    Cursor = Cursors.SizeAll, ToolTip = caption.FullText };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var vertical = span.Rotation == -90;
                var size = vertical ? new Size(label.DesiredSize.Height, label.DesiredSize.Width) : label.DesiredSize;
                var across = vertical ? size.Width : size.Height; var along = vertical ? size.Height : size.Width;
                if (along > span.AvailableLength * 3 + .001) continue;
                var maxOffset = Math.Max(0, (span.AvailableLength * 3 - along) / 2);
                var origin = new Point(span.Center.X * 3, span.Center.Y * 3);
                var gap = Math.Max(4, paddings[wire.WireId] + .5);
                var offsets = new List<double> { 0, maxOffset, -maxOffset };
                for (var step = 18d; step < maxOffset; step += 18) { offsets.Add(step); offsets.Add(-step); }
                foreach (var side in new[] { -1, 1 })
                {
                    var centers = offsets.Select(offset => vertical ? new Point(origin.X + side * (across / 2 + gap), origin.Y + offset)
                        : new Point(origin.X + offset, origin.Y + side * (across / 2 + gap))).ToList();
                    if (wire.LabelPosition is { } saved)
                    {
                        var point = new Point(saved.X * 3, saved.Y * 3);
                        var distance = vertical ? point.X - origin.X : point.Y - origin.Y;
                        var offset = vertical ? point.Y - origin.Y : point.X - origin.X;
                        if (Math.Sign(distance) == side && Math.Abs(distance) <= across / 2 + gap + 2 && Math.Abs(offset) <= maxOffset)
                            centers.Insert(0, point);
                    }
                    foreach (var center in centers)
                    {
                        var box = new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
                        if (Fits(box)) return new(label, box, span, side);
                    }
                }
            }
            return null;
        }

        void Add(SchematicWire wire, InlineWireCaption placed)
        {
            var (label, box, span, side) = placed;
            var vertical = span.Rotation == -90;
            if (side == 1)
            {
                var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                Sheet.Children.Add(new Line { X1 = vertical ? span.Center.X * 3 : center.X, Y1 = vertical ? center.Y : span.Center.Y * 3,
                    X2 = vertical ? box.Left : center.X, Y2 = vertical ? center.Y : box.Top, Stroke = Brushes.DarkCyan,
                    StrokeThickness = .8, IsHitTestVisible = false, Tag = "wire-caption-tick:" + wire.WireId });
            }
            if (vertical) label.RenderTransform = new RotateTransform(-90);
            Canvas.SetLeft(label, box.X); Canvas.SetTop(label, vertical ? box.Bottom : box.Y); Sheet.Children.Add(label);
            var blocked = box; blocked.Inflate(3, 3); occupied.Add(blocked);
            if (!_renderingOutput) label.MouseLeftButtonDown += (_, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SelectWire(wire.WireId, true); e.Handled = true; return; }
                if (_wireMode || wire.Locked) return;
                _selectedWires.Clear(); _selectedWires.Add(wire.WireId); _selectionId = wire.WireId;
                BeginGesture(e); _dragWireLabel = wire.WireId;
                _dragWireLabelOrigin = new((box.X + box.Width / 2) / 3, (box.Y + box.Height / 2) / 3); e.Handled = true;
            };
        }

        if (_renderingOutput) _outputLabelWarnings.Remove(page.PageId);
        else _wireLabelWarnings.Remove(page.PageId);
        var warnings = new List<string>();
        var warningIds = new List<string>();
        foreach (var wire in wires)
        {
            if (!_renderWireLabels.TryGetValue(wire.WireId, out var caption)) continue;
            var gauge = _renderWireDetails.GetValueOrDefault(wire.WireId)?.Specification.GaugeText;
            var text = caption.Text + (gauge is null ? "" : "；" + gauge);
            var placed = Place(wire, caption, (text, false), (gauge is null ? text : caption.Text + "\n" + gauge, false), (text, true));
            if (placed is not null) { Add(wire, placed); continue; }
            var parts = caption.Text.Split(" → ", StringSplitOptions.None);
            var start = occupied.Count;
            var pieces = new List<InlineWireCaption>();
            var fragments = parts.Length > 1 ? parts.Select((part, i) => part + (i < parts.Length - 1 ? " →" : "")).ToList() : new List<string> { caption.Text };
            if (gauge is not null) fragments.Add(gauge);
            foreach (var part in fragments)
            {
                var piece = Place(wire, caption, (part, false), (part, true));
                if (piece is null) break;
                pieces.Add(piece); var box = piece.Bounds; box.Inflate(3, 3); occupied.Add(box);
            }
            occupied.RemoveRange(start, occupied.Count - start);
            if (pieces.Count == fragments.Count) { foreach (var piece in pieces) Add(wire, piece); continue; }
            warnings.Add(_renderWireDetails.GetValueOrDefault(wire.WireId)?.DisplayName ?? caption.Text);
            warningIds.Add(wire.WireId);
        }
        if (_renderingOutput)
        {
            if (warnings.Count > 0)
                _outputLabelWarnings[page.PageId] = new(project.Schematic!.Pages.FindIndex(p => p.PageId == page.PageId) + 1,
                    page.PageId, page.Title, warningIds.AsReadOnly(), warnings.AsReadOnly());
        }
        else if (warnings.Count > 0)
        {
            const string message = "標示空間不足：已嘗試各線段與分行，請使用「依接線對齊接點」或調整引線間距。";
            _wireLabelWarnings[page.PageId] = message;
            Status.Text = message + "\n" + string.Join("、", warnings); Status.ToolTip = string.Join("\n", warnings);
        }
    }
}
