using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentPort = ComponentIntelligence.Electrical.Domain.ComponentPort;

internal static partial class Program
{
    private static void RunBoundsRegression(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        foreach (var scale in new[] { .5, 1d, 1.5 })
        {
            var service = new SchematicAuthoringService();
            var project = BoundsProject();
            var id = project.Schematic!.Symbols[0].SymbolId;
            project = service.TransformSymbol(project, id, new(10, 10), rotation);
            project = service.ScaleCadSymbol(project, id, scale);
            var wiredPort = project.Cables[0].ArchivedCable!.Ports[1].PortId;
            var contact = SchematicPortPresentation.ConnectionPoint(project, project.Schematic.Symbols[0], wiredPort);
            var freeEnd = new SchematicPoint(contact.X + 40, contact.Y);
            project = service.DrawWire(project, "page", SchematicAttachment.Port(id, wiredPort), SchematicAttachment.Free(), [contact, freeEnd]);
            var electrical = JsonSerializer.Serialize(new { project.Cables, project.Connections, project.Nets });
            var before = JsonSerializer.Serialize(project);
            var history = new ComponentIntelligence.Electrical.Editing.ProjectMutationHistory();
            var commits = 0;
            var control = new SchematicWorkspaceControl(() => project, (next, label) =>
                { history.RecordBeforeMutation(project, label); project = next; commits++; },
                () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            var window = new Window { Content = control }; // Focus scope only; never shown.
            Set(control, "_selectionId", id); Set(control, "_editModuleMode", true);
            control.RefreshWorkspace();
            var sheet = (Canvas)GetSheet(control);
            Layout(sheet);
            var frame = SelectionFrame(sheet);
            var horizontal = rotation % 180 == 0;
            Require(frame.Width < (horizontal ? 80 : 20) * scale + 9,
                $"CAD selection retains horizontal empty space at rotation {rotation}, scale {scale}: {frame}.");
            Require(frame.Height < (horizontal ? 20 : 80) * scale + 9,
                $"CAD selection retains vertical empty space at rotation {rotation}, scale {scale}: {frame}.");
            Require(frame.Width > (horizontal ? 80 : 20) * scale && frame.Height > (horizontal ? 20 : 80) * scale,
                "Selection frame cuts off visible CAD strokes or contacts.");
            foreach (var port in project.Cables[0].ArchivedCable!.Ports)
            {
                var point = SchematicPortPresentation.ConnectionPoint(project, project.Schematic.Symbols[0], port.PortId);
                Require(frame.Contains(new Point(point.X, point.Y)), "Selection frame excludes a CAD Port.");
            }
            var corner = sheet.Children.OfType<Border>().Single(b => b.Cursor == Cursors.SizeNWSE);
            var cornerPoint = new SchematicPoint((Canvas.GetLeft(corner) + 7) / 3, (Canvas.GetTop(corner) + 7) / 3);
            Require(Math.Abs(cornerPoint.X - frame.Right) < .001 && Math.Abs(cornerPoint.Y - frame.Bottom) < .001,
                "Resize corner does not follow the tight CAD selection.");
            var reference = sheet.Children.OfType<TextBlock>().Single(t => t.Text == "Reference 未設定");
            Require(Canvas.GetTop(reference) / 3 <= frame.Top - 4 && Canvas.GetTop(reference) / 3 >= frame.Top - 9,
                "Reference remains anchored to empty source margins.");
            var caption = sheet.Children.OfType<TextBlock>().Single(t => t.Text.Contains("長度待填"));
            Require(Math.Abs(Canvas.GetTop(caption) / 3 - frame.Bottom - 2) < .001,
                "Cable caption remains below empty source margins.");
            Require(JsonSerializer.Serialize(project) == before && commits == 0,
                "Automatic frame fitting rewrites stored CAD geometry or project data.");

            Set(control, "_gestureStart", project); Set(control, "_dragResizeSymbol", id);
            Set(control, "_dragStart", cornerPoint); Set(control, "_pointer", cornerPoint);
            Invoke(control, "PreviewSymbolResize");
            var stationary = (ElectricalProject)Get(control, "_gesturePreview")!;
            Require(Math.Abs(stationary.Schematic!.Symbols[0].Width - 120 * scale) < 1e-8,
                "Picking up the tight resize handle changes the scale before moving.");
            var destination = new SchematicPoint(10 + (horizontal ? 100 : 60) * scale / 2 + (horizontal ? 2.5 : 7d / 6),
                10 + (horizontal ? 60 : 100) * scale / 2 + (horizontal ? 7d / 6 : 2.5));
            Set(control, "_pointer", destination);
            Invoke(control, "PreviewSymbolResize");
            var preview = (ElectricalProject)Get(control, "_gesturePreview")!;
            Require(Math.Abs(preview.Schematic!.Symbols[0].Width - 60 * scale) < 1e-8,
                "Tight-frame resize is not proportional to the handle drag.");
            Require(JsonSerializer.Serialize(project) == before, "Resize preview mutates the saved project.");
            Invoke(control, "Sheet_Up", sheet, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            Require(commits == 1, "Tight CAD resize did not commit exactly once.");
            var wireStart = rotation switch {
                0 => new SchematicPoint(10 + 50 * scale, 10 + 25 * scale),
                90 => new SchematicPoint(10 + 25 * scale, 10 + 50 * scale),
                180 => new SchematicPoint(10 + 10 * scale, 10 + 25 * scale),
                _ => new SchematicPoint(10 + 25 * scale, 10 + 10 * scale)
            };
            var wire = project.Schematic!.Wires[0];
            Require(Math.Abs(wire.Points[0].X - wireStart.X) < 1e-7 && Math.Abs(wire.Points[0].Y - wireStart.Y) < 1e-7 && wire.Points[^1] == freeEnd,
                "Tight-frame resize loses the routed Port attachment or moves the free end.");
            Require(electrical == JsonSerializer.Serialize(new { project.Cables, project.Connections, project.Nets }),
                "Tight-frame resizing changes electrical connectivity or archived cable metadata.");
            SchematicAuthoringService.Validate(project);
            control.RefreshWorkspace(); Layout(sheet);
            var dragged = SelectionFrame(sheet);
            Require(Math.Abs(dragged.Right - destination.X) < .01 && Math.Abs(dragged.Bottom - destination.Y) < .01,
                "Fixed selection padding makes the resized corner drift away from the pointer.");
            Require(history.TryUndo(project, out project, out _) && JsonSerializer.Serialize(project) == before,
                "Undo cannot restore the pre-resize CAD data.");
            control.RefreshWorkspace(); Layout(sheet);
            var restored = SelectionFrame(sheet);
            Require(restored == frame, "Undo does not restore the automatic frame.");
            if (rotation == 0 && scale == 1)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(RenderSheet(sheet)));
                using (var file = File.Create(System.IO.Path.Combine(root, "tight-selection.png"))) encoder.Save(file);
                var bitmap = Output(control, project);
                SchematicPdfWriter.Write(project, System.IO.Path.Combine(root, "tight-cad-frame.pdf"), (p, _) => Output(control, p));
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect((int)(frame.Left / 25.4 * 300), (int)((frame.Top + 5) / 25.4 * 300), 1, 1), pixel, 4, 0);
                Require(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240, "Tight selection frame leaks into exported output.");
            }
            window.Content = null;
        }
        RunBoundsPrimitiveCases();
        RunExpandedPortDragFrameRegression();
        Console.WriteLine("PASS: 12 tight CAD frame cases, rotations/scales, source microtext, contacts, labels, handle tracks pointer, no-jump preview, routed Port, one commit, undo and output exclusion; 5 primitive/fallback cases.");
    }

    private static void RunExpandedPortDragFrameRegression()
    {
        var project = BoundsProject(narrow: true);
        var symbol = project.Schematic!.Symbols[0];
        var port = project.Cables[0].ArchivedCable!.Ports[1];
        project = new SchematicAuthoringService().TogglePortCollapsed(project, symbol.SymbolId, port.PortId);
        var before = JsonSerializer.Serialize(project);
        var control = new SchematicWorkspaceControl(() => project, (_, _) => throw new InvalidOperationException("Unexpected commit."),
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        Set(control, "_selectionId", symbol.SymbolId); Set(control, "_editModuleMode", true); control.RefreshWorkspace();
        var sheet = (Canvas)GetSheet(control); Layout(sheet);
        var handle = sheet.Children.OfType<Border>().Single(b => b.Width == 17);
        var pointer = new SchematicPoint((Canvas.GetLeft(handle) + 8) / 3, (Canvas.GetTop(handle) + 8) / 3 - 2);
        var current = project.Schematic.Symbols[0];
        var target = SchematicPortPresentation.DropTarget(current, pointer);
        Require(target.Side == "Right", "Small expanded CAD Port handle drag is reinterpreted as a different native edge.");
        Require(before == JsonSerializer.Serialize(project), "Displaying the fitted frame changes Port bindings.");
        Console.WriteLine("PASS: expanded Port control retains its native drag coordinate contract.");
    }

    private static void RunBoundsPrimitiveCases()
    {
        foreach (var kind in new[] { "curves", "wrapped", "empty", "unsupported", "contacts-only" })
        {
            var project = BoundsProject();
            var original = project.Schematic!.Symbols[0];
            var primitives = kind switch
            {
                "curves" => new SchematicCadPrimitive[] {
                    new() { Kind = "CIRCLE", Start = new(80, 50), Radius = 10 },
                    new() { Kind = "ARC", Start = new(50, 50), Radius = 20, StartAngle = 0, EndAngle = 90 },
                    new() { Kind = "SOLID_HATCH", Start = new(20, 40), Contours = [new SchematicPoint[] { new(20, 40), new(30, 40), new(30, 60), new(20, 60) }] }
                },
                "wrapped" => [new() { Kind = "MTEXT", Start = new(120, 35), Text = "VISIBLE WRAPPED CAD LABEL", TextHeight = 3,
                    TextWidth = 18, TextAttachment = "TopRight" }],
                "empty" => [new() { Kind = "TEXT", Start = new(0, 0), Text = "   ", TextHeight = 20 }],
                "contacts-only" => [new() { Kind = "TEXT", Start = new(0, 0), Text = "MICROTEXT", TextHeight = .000003 }],
                _ => [new() { Kind = "UNSUPPORTED", Start = new(0, 0) }]
            };
            project.Schematic.Symbols[0] = original with { Geometry = original.Geometry! with { Primitives = primitives },
                Anchors = kind is "empty" or "unsupported" ? [] : original.Anchors,
                CadPortBindings = kind is "empty" or "unsupported" ? [] : original.CadPortBindings };
            var before = JsonSerializer.Serialize(project);
            var control = new SchematicWorkspaceControl(() => project, (_, _) => throw new InvalidOperationException("Unexpected commit."),
                () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
            Set(control, "_selectionId", original.SymbolId); control.RefreshWorkspace();
            var sheet = (Canvas)GetSheet(control); Layout(sheet);
            var frame = SelectionFrame(sheet);
            if (kind is "empty" or "unsupported")
                Require(frame == new Rect(10, 10, 120, 100), "Empty CAD does not fall back to a usable native frame.");
            else if (kind == "contacts-only")
                Require(Math.Abs(frame.Width - 85) < .001 && Math.Abs(frame.Height - 5) < .001,
                    "Visible contacts with no visible CAD ink retain empty native margins.");
            else if (kind == "curves")
            {
                Require(frame.Height < 40 && frame.Width < 90, "Curves/hatches retain source margins.");
                Require(frame.Contains(new Rect(90 - 10, 60 - 10, 20, 20)), "Circle is cut by the fitted frame.");
                Require(frame.Contains(new Point(60, 40)) && frame.Contains(new Point(80, 60)), "Arc endpoints are cut by the fitted frame.");
                Require(frame.Contains(new Rect(30, 50, 10, 20)), "Solid hatch is cut by the fitted frame.");
            }
            else
            {
                var text = Elements(sheet).OfType<TextBlock>().Single(t => t.Text == "VISIBLE WRAPPED CAD LABEL");
                var rendered = text.TransformToAncestor(sheet).TransformBounds(VisualTreeHelper.GetDescendantBounds(text));
                Require(frame.Contains(new Rect(rendered.X / 3, rendered.Y / 3, rendered.Width / 3, rendered.Height / 3)),
                    "Wrapped, right-attached MTEXT is cut by the fitted frame.");
                Require(frame.Height < 70, "MTEXT-only CAD retains source vertical margins.");
            }
            Require(before == JsonSerializer.Serialize(project), "Fitting primitive bounds changes CAD evidence.");
        }
    }

    private static ElectricalProject BoundsProject(bool narrow = false)
    {
        var left = narrow ? 50 : 20; var right = narrow ? 70 : 100;
        var template = new ArchivedCableTemplate { TemplateId = "bounds", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Ports = Enumerable.Range(1, 2).Select(i => new ComponentPort { PortId = "P" + i, Name = "P" + i,
                Pins = [new() { PinId = "pin" + i, PinNumber = "1" }] }).ToList() };
        var asset = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 120, Height = 100, MillimetresPerUnit = 1,
            Primitives = [
                new() { Kind = "LINE", Start = new(left, 40), End = new(right, 40) },
                new() { Kind = "LINE", Start = new(right, 40), End = new(right, 60) },
                new() { Kind = "LINE", Start = new(right, 60), End = new(left, 60) },
                new() { Kind = "LINE", Start = new(left, 60), End = new(left, 40) },
                new() { Kind = "TEXT", Start = new(55, 50), Text = "ID", TextHeight = 3, TextAttachment = "MiddleCenter" },
                new() { Kind = "TEXT", Start = new(0, 0), Text = "SOURCE MICROTEXT", TextHeight = .000003 },
                new() { Kind = "MTEXT", Start = new(120, 100), Text = "SOURCE\nMICROTEXT", TextHeight = .000003, TextWidth = .00003 }
            ],
            ConnectionPoints = [new("c1", "", new(left, 50), Direction: "Left"), new("c2", "", new(right, 50), Direction: "Right")] };
        var project = new ElectricalProject { ProjectId = "tight-bounds", Schematic = new() {
            Pages = [new() { PageId = "page", Title = "Synthetic bounds", Width = 210, Height = 210 }] } };
        return new SchematicAuthoringService().AddArchivedCable(project, template, CableConstructionType.Unknown, asset,
            new Dictionary<string, string> { ["P1"] = "c1", ["P2"] = "c2" }, "page", new(10, 10), assetApproved: true);
    }

    private static object GetSheet(SchematicWorkspaceControl control) => typeof(SchematicWorkspaceControl)
        .GetProperty("Sheet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(control)!;

    private static void Layout(Canvas sheet)
    { sheet.Measure(new Size(sheet.Width, sheet.Height)); sheet.Arrange(new Rect(0, 0, sheet.Width, sheet.Height)); sheet.UpdateLayout(); }

    private static IEnumerable<FrameworkElement> Elements(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Elements(child)) yield return nested;
        }
    }

    private static Rect SelectionFrame(Canvas sheet)
    {
        var border = Elements(sheet).OfType<Border>().Single(b => b.BorderThickness == new Thickness(2) && b.BorderBrush == Brushes.DarkCyan);
        var bounds = border.TransformToAncestor(sheet).TransformBounds(new Rect(0, 0, border.ActualWidth, border.ActualHeight));
        return new Rect(bounds.X / 3, bounds.Y / 3, bounds.Width / 3, bounds.Height / 3);
    }

    private static BitmapSource RenderSheet(Canvas sheet)
    {
        var bitmap = new RenderTargetBitmap((int)sheet.Width, (int)sheet.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(sheet); bitmap.Freeze(); return bitmap;
    }
}
