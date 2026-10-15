using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

internal static partial class Program
{
    private static void CompactWireStressProof(ElectricalProject baseline, string output)
    {
        var p = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(baseline))!;
        var descriptions = new[] { "Common for 24 V supply and non-isolated communication reference",
            "Device supply with installation-specific voltage-drop and thermal review required",
            "Optional external sensor input for Pt1000, Ni1000, NTC10k, active 0..10 V or switching contact",
            "BACnet MS/TP or Modbus RTU differential data negative",
            "BACnet MS/TP or Modbus RTU differential data positive", "Feedback" };
        foreach (var component in p.Components)
        {
            component.ReferenceDesignator = component.ComponentInstanceId == "Valve" ? "EnergyValveControlValve1" : "LongNameIOController1";
            component.Ports[0].Name = "CONTROL_LEADS";
            for (var i = 0; i < component.Ports[0].Pins.Count; i++) component.Ports[0].Pins[i].Function = descriptions[i];
        }
        for (var i = 0; i < p.Schematic!.Symbols.Count; i++)
        {
            var symbol = p.Schematic.Symbols[i];
            p.Schematic.Symbols[i] = symbol with { Position = new(i == 0 ? 30 : 150, 40), Height = 65,
                Anchors = symbol.Anchors.Select((a, index) => a with { Position = new(i == 0 ? 40 : 0, 12 + index * 7) }).ToList() };
        }
        for (var i = 0; i < p.Schematic.Wires.Count; i++) p.Schematic.Wires[i] = p.Schematic.Wires[i] with
        { Designation = "EnergyValveControlValve1_CONTROL_LEADS_" + (i + 1), ManualDesignation = true,
            Points = [new(70, 52 + i * 7), new(150, 52 + i * 7)], LabelPosition = new(300, 200 + i * 10) };
        p.Schematic.Wires[3] = p.Schematic.Wires[3] with { LabelPosition = new(110, 73 + 3.4) };
        var type = typeof(SchematicWorkspaceControl);
        var workspace = new SchematicWorkspaceControl(() => p, (next, _) => p = next,
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var render = type.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        type.GetField("_pageId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(workspace, "main");
        Canvas Draw()
        {
            render.Invoke(workspace, [p]);
            var canvas = (Canvas)type.GetProperty("Sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
            return canvas;
        }
        void Save(Canvas canvas, string name)
        {
            var image = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32); image.Render(canvas);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
        }
        var before = JsonSerializer.Serialize(p);
        var dense = Draw();
        Require(before == JsonSerializer.Serialize(p), "Narrow label reflow mutated electrical data or full manual names.");
        try
        {
            type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [p, p.Schematic!.Pages[0]]);
            throw new InvalidOperationException("In-sheet unconnected Pin text left no clearance, but output silently omitted a conductor caption.");
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException inner && inner.Message.Contains("導線標籤")) { }
        // Preserve full manual names in data/tooltips while checking expanded-port layout as well.
        var author = new SchematicAuthoringService();
        p = author.TransformSymbol(p, p.Schematic!.Symbols[1].SymbolId, new(230, 40), 0);
        p = author.SetPortFanout(p, p.Schematic!.Symbols[0].SymbolId, p.Components[0].Ports[0].PortId, true, 12, 25);
        p = author.AlignPortFanout(p, p.Schematic!.Symbols[0].SymbolId, p.Components[0].Ports[0].PortId);
        before = JsonSerializer.Serialize(p);
        dense = Draw();
        var labels = dense.Children.OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("wire-label:")).ToArray();
        Save(dense, "compact-dense-diagnostic.png");
        Require(labels.Select(t => t.Tag).Distinct().Count() == 5, "Dense long-description fixture omitted wires: " + string.Join("; ", labels.Select(l => l.Text)));
        Require(!dense.Children.OfType<FrameworkElement>().Any(e => e.Tag is string tag && tag.StartsWith("wire-leader:")), "Old saved remote captions created long leaders.");
        foreach (var label in labels)
        {
            var wire = p.Schematic!.Wires.Single(w => Equals(label.Tag, "wire-label:" + w.WireId));
            var box = new Rect(Canvas.GetLeft(label), Canvas.GetTop(label), label.DesiredSize.Width, label.DesiredSize.Height);
            Require(Math.Min(Math.Abs(box.Top - wire.Points[0].Y * 3), Math.Abs(box.Bottom - wire.Points[0].Y * 3)) <= 8,
                "Saved remote caption was not reflowed next to its wire.");
            Require(box.Bottom < wire.Points[0].Y * 3 || dense.Children.OfType<System.Windows.Shapes.Line>().Any(l => Equals(l.Tag, "wire-caption-tick:" + wire.WireId)),
                "A below-wire caption is missing its adjacent attribution tick.");
            Require(label.ToolTip?.ToString()?.Contains(descriptions[p.Schematic.Wires.IndexOf(wire)]) == true,
                "Compact caption lost its original full function tooltip.");
            foreach (var other in p.Schematic.Wires)
            {
                var line = new Rect(new Point(other.Points[0].X * 3, other.Points[0].Y * 3), new Point(other.Points[^1].X * 3, other.Points[^1].Y * 3));
                line.Inflate(2, 2); Require(!line.IntersectsWith(box), "Dense caption covers a wire.");
            }
        }
        Require(before == JsonSerializer.Serialize(p), "Dense caption reflow mutated project data or manual wire names.");
        Save(dense, "compact-dense-wires.png");
        Require(SchematicPdfWriter.Write(p, Path.Combine(output, "compact-dense-wires.pdf"), (project, page) =>
            (BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [project, page])!, includeWireDetails: true) == 2,
            "Dense drawing did not export its full details page.");

        var entries = SchematicWireLegendPresentation.Entries(p);
        var gridFactory = type.GetMethod("CreateWireDetailsGrid", BindingFlags.Static | BindingFlags.NonPublic);
        Require(gridFactory is not null, "Wire details grid cannot be validated independently of opening a live window.");
        var grid = (DataGrid)gridFactory!.Invoke(null, [entries])!;
        grid.Measure(new Size(1100, 600)); grid.Arrange(new Rect(0, 0, 1100, 600)); grid.UpdateLayout();
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
        Require(grid.IsReadOnly && grid.Columns.Count == 7 && view.Groups?.Count == 2,
            "Wire details grid must retain full metadata columns and one header per exact specification group.");
        Require(grid.Columns.OfType<DataGridTextColumn>().All(column => column.ElementStyle.Setters.OfType<Setter>().Any(setter =>
            setter.Property == TextBlock.TextWrappingProperty && Equals(setter.Value, TextWrapping.Wrap))),
            "Long full-function cells in the details dialog must wrap instead of being clipped.");
        Require(view.Cast<SchematicWireLegendEntry>().Select(e => e.FullText).SequenceEqual(entries.Select(e => e.FullText)),
            "Wire details grid lost full original wire descriptions.");
        var many = Enumerable.Range(0, 160).Select(i => entries[i % entries.Count] with
            { WireId = "stress-" + i, Index = "W" + (i + 1).ToString("D3") }).ToArray();
        var page = p.Schematic!.Pages[0];
        var tiles = SchematicWireDetailsRenderer.Plan(many, page, 1);
        var planned = tiles.SelectMany(t => t.Blocks).SelectMany(b => b.Rows).Select(r => r.Entry.WireId).ToArray();
        Require(tiles.Count > 1 && planned.Length == many.Length && planned.Distinct().Count() == many.Length,
            "Automatic details pagination dropped or duplicated rows.");
        Require(many.All(e => planned.Contains(e.WireId)), "Details pagination omitted original wire identities.");
        var blank = page with { PageId = "details-background", Title = "Wire details" };
        var background = (BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, [p, blank])!;
        foreach (var index in new[] { 0, tiles.Count - 1 })
        {
            var appendix = SchematicWireDetailsRenderer.Render(blank, tiles[index], background);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(appendix));
            using var file = File.Create(Path.Combine(output, "paginated-details-" + (index + 1) + ".png")); encoder.Save(file);
        }
        var maxOverflow = 0d;
        foreach (var row in tiles.SelectMany(t => t.Blocks).SelectMany(b => b.Rows))
        {
            var widths = new[] { .04, .18, .23, .30, .07, .18 };
            var cells = new[] { row.Entry.Index, row.Entry.Designation, row.Entry.Contacts, row.Entry.Functions, row.Entry.Color, row.Entry.Review };
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = new TextBlock { Text = cells[i], Width = (page.Width - 2 * page.Margin - 8) * widths[i] * 4 - 1,
                    FontFamily = new("Microsoft JhengHei"), FontSize = 12, Padding = new Thickness(4), TextWrapping = TextWrapping.Wrap };
                cell.Measure(new Size(cell.Width, double.PositiveInfinity));
                maxOverflow = Math.Max(maxOverflow, cell.DesiredSize.Height / 4 - row.Height);
            }
        }
        Require(maxOverflow <= .001, "Rendered details cell exceeds the row height measured by the paginator.");
        var portrait = page with { Width = 210, Height = 297, Margin = 10 };
        var portraitWidth = portrait.Width - 2 * portrait.Margin - 8;
        var headerMethod = typeof(SchematicWireDetailsRenderer).GetMethod("HeaderHeight", BindingFlags.Static | BindingFlags.NonPublic);
        var headerHeight = headerMethod is not null ? (double)headerMethod.Invoke(null, [portraitWidth])! :
            (double)typeof(SchematicWireDetailsRenderer).GetField("HeaderHeight", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var headers = new[] { "索引", "完整線號", "接點", "完整功能", "線色", "核對備註" };
        var fractions = new[] { .04, .18, .23, .30, .07, .18 };
        for (var i = 0; i < headers.Length; i++)
        {
            var header = new TextBlock { Text = headers[i], Width = portraitWidth * fractions[i] * 4 - 1,
                FontFamily = new("Microsoft JhengHei"), FontSize = 12, Padding = new Thickness(4), TextWrapping = TextWrapping.Wrap };
            header.Measure(new Size(header.Width, double.PositiveInfinity));
            Require(header.DesiredSize.Height + 1 <= headerHeight * 4 + .001,
                "A4 portrait details header clips wrapped text inside its border.");
        }
        var portraitTiles = SchematicWireDetailsRenderer.Plan(many, portrait, 1);
        foreach (var tile in portraitTiles)
        {
            var usedHeight = tile.Blocks.Sum(block =>
            {
                var title = new TextBlock { Text = block.Specification.Text, Width = portraitWidth * 4,
                    FontFamily = new("Microsoft JhengHei"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(4), TextWrapping = TextWrapping.Wrap };
                title.Measure(new Size(title.Width, double.PositiveInfinity));
                return title.DesiredSize.Height / 4 + headerHeight + block.Rows.Sum(row => row.Height);
            });
            Require(usedHeight <= portrait.Height - 2 * portrait.Margin - 22 - 16 + .001,
                "Wrapped details headers were not reserved in pagination.");
        }
        var portraitBlank = portrait with { PageId = "portrait-details-background", Title = "Wire details" };
        var portraitBackground = (BitmapSource)type.GetMethod("RenderOutputPage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(workspace, [p, portraitBlank])!;
        var portraitImage = SchematicWireDetailsRenderer.Render(portraitBlank, portraitTiles[0], portraitBackground);
        var portraitEncoder = new PngBitmapEncoder(); portraitEncoder.Frames.Add(BitmapFrame.Create(portraitImage));
        using (var file = File.Create(Path.Combine(output, "portrait-details-1.png"))) portraitEncoder.Save(file);
        for (var sample = 0; sample < 100; sample++)
        {
            var narrow = page with { Width = 210 + sample * .1 };
            var entry = entries[0] with { Functions = new string('M', 400), Contacts = "", Designation = "", Review = "", Color = "" };
            var row = SchematicWireDetailsRenderer.Plan([entry], narrow, 1).Single().Blocks.Single().Rows.Single();
            var cell = new TextBlock { Text = entry.Functions, Width = (narrow.Width - 2 * narrow.Margin - 8) * .30 * 4 - 1,
                FontFamily = new("Microsoft JhengHei"), FontSize = 12, Padding = new Thickness(4), TextWrapping = TextWrapping.Wrap };
            cell.Measure(new Size(cell.Width, double.PositiveInfinity));
            Require(cell.DesiredSize.Height / 4 <= row.Height + .001,
                "Near-wrap-boundary details text is taller than its measured PDF row.");
        }
        try
        {
            SchematicWireDetailsRenderer.Plan([entries[0] with { Functions = new string('x', 100000) }], page, 1);
            throw new InvalidOperationException("Oversized detail cell was silently truncated.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("未截斷")) { }

        var performance = new ElectricalProject { ProjectId = "caption-batch", Schematic = new() { Pages = [page] } };
        for (var i = 0; i < 1000; i++)
        {
            performance.Connections.Add(new() { ConnectionId = "con-" + i, FromEndpointId = "from-" + i, ToEndpointId = "to-" + i });
            performance.Schematic.Wires.Add(new() { WireId = "batch-" + i, PageId = "main", ConnectionId = "con-" + i,
                Designation = "SyntheticWire" + i, Points = [new(40, 40), new(200, 40)] });
        }
        p = performance;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        type.GetMethod("PrepareWireLabels", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace,
            [p, page, SchematicCrossingService.Analyze([])]);
        watch.Stop();
        Require(watch.Elapsed.TotalMilliseconds < 2000, "Caption preparation repeats global wire/connection index scans for every caption: " + watch.Elapsed.TotalMilliseconds);

        foreach (var edge in new[] { (Vertical: false, Points: new List<SchematicPoint> { new(80, 13), new(200, 13) }),
            (Vertical: true, Points: new List<SchematicPoint> { new(13, 80), new(13, 200) }) })
        {
            p = new() { ProjectId = "edge-caption", Schematic = new() { Pages = [page], Wires = [new()
                { WireId = "edge", PageId = "main", Designation = "EdgeWire", Points = edge.Points }] } };
            var edgeCanvas = Draw();
            Require(edgeCanvas.Children.OfType<TextBlock>().Any(t => Equals(t.Tag, "wire-label:edge")),
                "Wire near paper margin lost caption even though the opposite adjacent side was clear.");
            var tick = edgeCanvas.Children.OfType<System.Windows.Shapes.Line>().Single(t => Equals(t.Tag, "wire-caption-tick:edge"));
            Require(new Vector(tick.X2 - tick.X1, tick.Y2 - tick.Y1).Length <= 6,
                "Opposite-side caption uses a remote leader instead of a short adjacent attribution tick.");
        }

        var tablePage = page with { Width = 297, Height = 210 };
        var template = new ArchivedCableTemplate { TemplateId = "table-overlap", TemplateRevision = "draft", AssetSha256 = new('a', 64),
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentIntelligence.Electrical.Domain.ComponentPort { PortId = "P" + i, Name = "P" + i,
                Pins = Enumerable.Range(1, 8).Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = $"p{i}.{n}", PinNumber = n.ToString(),
                    Function = "A complete function description" }).ToList() }).ToList(),
            Mapping = Enumerable.Range(1, 8).Select(n => new CablePinMapping("p1." + n, "p2." + n)).ToList() };
        var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        var detail = new SchematicCableDetailBinding(cable.CableInstanceId, null) { DetailId = "table", TablePosition = new(10, 10),
            TableWidth = 180, SketchVisible = false };
        tablePage = tablePage with { InlineCableDetails = [detail] };
        p = new() { ProjectId = "legend-table-clearance", Cables = [cable], Schematic = new() { Pages = [tablePage], Wires = [new()
            { WireId = "table-wire", PageId = "main", Designation = "TableWire", Points = [new(210, 180), new(270, 180)] }] } };
        var actual = new SchematicCableDetailService().BuildArchived(p, tablePage, detail).PartBounds[SchematicCableDetailPart.Table];
        Require(actual.Height > 70, "Table-overlap fixture failed to exceed the former fixed 70 mm reservation.");
        var tableCanvas = Draw();
        var note = tableCanvas.Children.OfType<TextBlock>().Single(t => Equals(t.Tag, "wire-spec-legend") || Equals(t.Tag, "wire-label:table-wire"));
        var tableBounds = new Rect(actual.X * 3, actual.Y * 3, actual.Width * 3, actual.Height * 3);
        Require(!tableBounds.IntersectsWith(new Rect(Canvas.GetLeft(note), Canvas.GetTop(note), note.DesiredSize.Width, note.DesiredSize.Height)),
            "Group specification legend obscures the lower rows of a tall manufacturing table.");
        Save(tableCanvas, "compact-table-clearance.png");

        // Keep the actual contacts on top/bottom module edges for a true vertical route.
        p = JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(baseline))!;
        foreach (var component in p.Components) component.Ports[0].Pins.RemoveRange(1, component.Ports[0].Pins.Count - 1);
        p.Schematic!.Symbols[0] = p.Schematic.Symbols[0] with { Position = new(100, 30), Height = 30,
            Anchors = [p.Schematic.Symbols[0].Anchors[0] with { Position = new(20, 30), Direction = "Bottom" }] };
        p.Schematic.Symbols[1] = p.Schematic.Symbols[1] with { Position = new(100, 180), Height = 30,
            Anchors = [p.Schematic.Symbols[1].Anchors[0] with { Position = new(20, 0), Direction = "Top" }] };
        p.Schematic.Wires.RemoveRange(1, p.Schematic.Wires.Count - 1);
        p.Schematic.Wires[0] = p.Schematic.Wires[0] with { Points = [new(120, 60), new(120, 180)], LabelPosition = null };
        var vertical = Draw();
        var caption = vertical.Children.OfType<TextBlock>().Single(t => t.Tag is string tag && tag.StartsWith("wire-label:"));
        Require(caption.RenderTransform is RotateTransform { Angle: -90 }, "Vertical wire caption was not aligned along its own span.");
        var bounds = new Rect(Canvas.GetLeft(caption), Canvas.GetTop(caption) - caption.DesiredSize.Width, caption.DesiredSize.Height, caption.DesiredSize.Width);
        Require(bounds.Right < 120 * 3 || bounds.Left > 120 * 3, "Vertical caption covers its own line.");
        Save(vertical, "compact-vertical-wire.png");
        File.WriteAllText(Path.Combine(output, "compact-wire-stress.json"), JsonSerializer.Serialize(new
        { Passed = true, DenseLabels = labels.Length, RemoteSavedLabelsReflowed = true, FullTooltipsRetained = true,
            ElectricalSnapshotUnchanged = true, PaginationRows = many.Length, PaginationPages = tiles.Count,
            MeasuredCellOverflow = maxOverflow, PortraitHeaderHeightMm = headerHeight,
            PortraitHeadersAndPaginationVerified = true, OversizedCellsRejected = true, VerticalCaptionAligned = true,
            CaptionBatch1000Ms = watch.Elapsed.TotalMilliseconds, PaperMarginFallbackVerified = true, TallTableClearanceVerified = true }));
    }
}
