using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private static Canvas CadCanvas(SchematicCadAsset asset, Brush? stroke = null, double thickness = 1, DoubleCollection? dash = null)
    {
        var canvas = new Canvas { Width = asset.Width * 3, Height = asset.Height * 3, IsHitTestVisible = false };
        foreach (var primitive in asset.Primitives)
        {
            FrameworkElement element;
            switch (primitive.Kind)
            {
                case "SOLID_HATCH":
                    var fill = new StreamGeometry { FillRule = FillRule.EvenOdd };
                    using (var context = fill.Open())
                        foreach (var loop in primitive.Contours.Where(loop => loop.Count >= 3))
                        {
                            context.BeginFigure(new Point(loop[0].X * 3, loop[0].Y * 3), true, true);
                            context.PolyLineTo(loop.Skip(1).Select(p => new Point(p.X * 3, p.Y * 3)).ToArray(), true, false);
                        }
                    fill.Freeze();
                    element = new System.Windows.Shapes.Path { Data = fill, Fill = stroke ?? Brushes.Black, StrokeThickness = 0 };
                    break;
                case "LINE" when primitive.End is not null:
                    element = new Line { X1 = primitive.Start.X * 3, Y1 = primitive.Start.Y * 3,
                        X2 = primitive.End.X * 3, Y2 = primitive.End.Y * 3, Stroke = Brushes.Black, StrokeThickness = 1 };
                    break;
                case "CIRCLE":
                    element = new Ellipse { Width = primitive.Radius * 6, Height = primitive.Radius * 6, Stroke = Brushes.Black, StrokeThickness = 1 };
                    Canvas.SetLeft(element, (primitive.Start.X - primitive.Radius) * 3); Canvas.SetTop(element, (primitive.Start.Y - primitive.Radius) * 3);
                    break;
                case "ARC":
                    var sweep = (primitive.EndAngle - primitive.StartAngle + 360) % 360;
                    Point At(double degrees) => new((primitive.Start.X + primitive.Radius * Math.Cos(degrees * Math.PI / 180)) * 3,
                        (primitive.Start.Y - primitive.Radius * Math.Sin(degrees * Math.PI / 180)) * 3);
                    var figure = new PathFigure { StartPoint = At(primitive.StartAngle), IsClosed = false };
                    figure.Segments.Add(new ArcSegment(At(primitive.EndAngle), new Size(primitive.Radius * 3, primitive.Radius * 3), 0, sweep > 180, SweepDirection.Counterclockwise, true));
                    element = new System.Windows.Shapes.Path { Data = new PathGeometry([figure]), Stroke = Brushes.Black, StrokeThickness = 1 };
                    break;
                case "TEXT":
                    var singleLine = new TextBlock { Text = primitive.DisplayText, FontSize = Math.Max(1, primitive.TextHeight * 3),
                        Foreground = Brushes.Black };
                    singleLine.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var offset = primitive.TextAnchorOffset(singleLine.DesiredSize.Width, singleLine.DesiredSize.Height, singleLine.BaselineOffset);
                    var textTransform = new TransformGroup();
                    textTransform.Children.Add(new ScaleTransform(primitive.TextWidthFactor, 1));
                    textTransform.Children.Add(new TranslateTransform(offset.X * primitive.TextWidthFactor, offset.Y));
                    textTransform.Children.Add(new RotateTransform(primitive.Rotation));
                    singleLine.RenderTransform = textTransform;
                    Canvas.SetLeft(singleLine, primitive.Start.X * 3); Canvas.SetTop(singleLine, primitive.Start.Y * 3);
                    element = singleLine;
                    break;
                case "MTEXT":
                    var text = new TextBlock { Text = primitive.Text, FontSize = Math.Max(1, primitive.TextHeight * 3),
                        Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
                    if (primitive.TextWidth > 0) text.Width = primitive.TextWidth * 3;
                    text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var attachment = primitive.TextAttachment ?? "TopLeft";
                    var dx = attachment.EndsWith("Right", StringComparison.Ordinal) ? -text.DesiredSize.Width :
                        attachment.EndsWith("Center", StringComparison.Ordinal) ? -text.DesiredSize.Width / 2 : 0;
                    var dy = attachment.StartsWith("Bottom", StringComparison.Ordinal) ? -text.DesiredSize.Height :
                        attachment.StartsWith("Middle", StringComparison.Ordinal) ? -text.DesiredSize.Height / 2 : 0;
                    var transform = new TransformGroup();
                    transform.Children.Add(new TranslateTransform(dx, dy));
                    transform.Children.Add(new RotateTransform(primitive.Rotation));
                    text.RenderTransform = transform;
                    Canvas.SetLeft(text, primitive.Start.X * 3); Canvas.SetTop(text, primitive.Start.Y * 3);
                    element = text;
                    break;
                default: continue;
            }
            if (element is Shape shape && primitive.Kind != "SOLID_HATCH")
            {
                shape.Stroke = stroke ?? Brushes.Black;
                shape.StrokeThickness = thickness;
                shape.StrokeDashArray = dash;
            }
            canvas.Children.Add(element);
        }
        return canvas;
    }

    private async void ImportSymbol_Click(object sender, RoutedEventArgs e)
    {
        var symbol = _getProject().Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (symbol is null) { Status.Text = "請先選取元件"; return; }
        if (symbol.CableInstanceId is not null) { Status.Text = "線材外觀綁定歸檔模板版本，請由線材庫選用修訂版本。"; return; }
        try
        {
            var componentId = _getProject().Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId).ComponentDefinitionId;
            if (!string.IsNullOrWhiteSpace(_archiveRoot) && !string.IsNullOrWhiteSpace(componentId))
            {
                var repository = new SymbolArchiveRepository(_archiveRoot);
                var resolver = new Cp3aDrawingAssetResolver(new SymbolResolver(repository, _catalog), repository);
                var approved = resolver.Resolve(componentId, DrawingRepresentationRole.Schematic);
                if (approved is not null)
                {
                    var choice = MessageBox.Show(Window.GetWindow(this),
                        $"已核准圖塊：{componentId}\n{approved.SourceType} / {approved.Revision}\nSHA-256: {approved.AssetHashSha256}\n\n是：套用此版本及明確接點綁定\n否：另選未核准外觀草稿\n取消：保持原圖",
                        "使用既有核准圖塊", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                    if (choice == MessageBoxResult.Cancel) return;
                    if (choice == MessageBoxResult.Yes)
                    {
                        var loaded = await PickCadAsset(approved.AssetPath); if (loaded is null) return;
                        var current = resolver.Resolve(componentId, DrawingRepresentationRole.Schematic);
                        if (current is null || current.Revision != approved.Revision || current.AssetHashSha256 != approved.AssetHashSha256 || current.AssetPath != approved.AssetPath)
                            throw new InvalidOperationException("讀取期間核准版本已變更，請重新選取。");
                        Apply(p => _service.SetApprovedSymbolGeometry(p, symbol.SymbolId, componentId, current, loaded.Value.Asset),
                            $"已套用核准 {current.Revision}；未綁定接點仍待確認");
                        return;
                    }
                }
            }
        }
        catch (Exception error) { Status.Text = "核准圖塊未套用：" + error.Message; return; }
        var imported = await PickCadAsset(); if (imported is null) return;
        Apply(p => _service.SetSymbolGeometry(p, symbol.SymbolId, imported.Value.Asset, imported.Value.Path), "已套用圖塊草稿；接點需明確綁定");
    }

    private void ImportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var page = _getProject().Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId); if (page is null) return;
        (SchematicCadAsset Asset, string Path)? imported = null;
        var dialog = new Window { Title = "公司圖框與格位", Width = 420, Height = 700, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) };
        var sourceLabel = new TextBlock { Text = page.TemplatePath is null ? "尚未套用公司模板" : System.IO.Path.GetFileName(page.TemplatePath), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(sourceLabel);
        var replace = new Button { Content = "選擇／更換模板", Margin = new(0, 4, 0, 12), Padding = new(8) };
        panel.Children.Add(replace);
        replace.Click += async (_, _) =>
        {
            dialog.IsEnabled = false;
            try
            {
                var candidate = await PickCadAsset(owner: dialog);
                if (candidate is null) return;
                imported = candidate; sourceLabel.Text = System.IO.Path.GetFileName(candidate.Value.Path);
            }
            finally { dialog.IsEnabled = true; }
        };
        TextBox Field(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label }); var box = new TextBox { Text = value, Margin = new(0, 3, 0, 8) }; panel.Children.Add(box); return box;
        }
        var width = Field("紙張寬度 mm", page.Width.ToString(CultureInfo.InvariantCulture));
        var height = Field("紙張高度 mm", page.Height.ToString(CultureInfo.InvariantCulture));
        var margin = Field("格位內框邊距 mm", page.Margin.ToString(CultureInfo.InvariantCulture));
        var columns = Field("格位欄數", page.GridColumns.ToString()); var rows = Field("格位列數", page.GridRows.ToString());
        var customGrid = new CheckBox { Content = "獨立格位範圍（mm，左上為原點）", IsChecked = page.CoordinateGrid is not null, Margin = new(0, 4, 0, 8) };
        panel.Children.Add(customGrid);
        var grid = page.EffectiveGrid();
        var gridX = Field("格位左邊 X", grid.X.ToString(CultureInfo.InvariantCulture));
        var gridY = Field("格位上邊 Y", grid.Y.ToString(CultureInfo.InvariantCulture));
        var gridWidth = Field("格位範圍寬", grid.Width.ToString(CultureInfo.InvariantCulture));
        var gridHeight = Field("格位範圍高", grid.Height.ToString(CultureInfo.InvariantCulture));
        void RefreshGridFields() { foreach (var box in new[] { gridX, gridY, gridWidth, gridHeight }) box.IsEnabled = customGrid.IsChecked == true; }
        customGrid.Checked += (_, _) => RefreshGridFields(); customGrid.Unchecked += (_, _) => RefreshGridFields(); RefreshGridFields();
        var ok = new Button { Content = "套用", Padding = new(8) }; panel.Children.Add(ok);
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(width.Text, out var w) || !double.TryParse(height.Text, out var h) || !double.TryParse(margin.Text, out var m) ||
                !int.TryParse(columns.Text, out var c) || !int.TryParse(rows.Text, out var r)) { MessageBox.Show(dialog, "請輸入有效尺寸與格位數"); return; }
            SchematicGridBounds? bounds = null;
            if (customGrid.IsChecked == true)
            {
                if (!double.TryParse(gridX.Text, out var x) || !double.TryParse(gridY.Text, out var y) ||
                    !double.TryParse(gridWidth.Text, out var gw) || !double.TryParse(gridHeight.Text, out var gh))
                { MessageBox.Show(dialog, "請輸入有效格位範圍"); return; }
                bounds = new(x, y, gw, gh);
            }
            if (Apply(p => imported is { } candidate
                ? _service.SetPageTemplate(p, page.PageId, candidate.Asset, candidate.Path, w, h, m, c, r, bounds)
                : _service.SetPageSettings(p, page.PageId, w, h, m, c, r, bounds), "已更新圖框／頁面設定")) dialog.DialogResult = true;
        };
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; dialog.ShowDialog();
    }

    private async Task<(SchematicCadAsset Asset, string Path)?> PickCadAsset(string? approvedPath = null, Window? owner = null)
    {
        owner ??= Window.GetWindow(this);
        var path = approvedPath;
        if (path is null)
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "CAD 圖面|*.dxf;*.dwg;*.dwt", CheckFileExists = true };
            if (picker.ShowDialog(owner) != true) return null;
            path = picker.FileName;
        }
        var dialog = new Window { Title = "CAD 匯入單位", Width = 360, Height = 170, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) }; var scale = new TextBox { Text = "1", Margin = new(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "每 CAD 單位對應毫米數（1 = mm）" }); panel.Children.Add(scale);
        var ok = new Button { Content = "讀取副本", Padding = new(8) }; ok.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(ok); dialog.Content = panel;
        if (dialog.ShowDialog() != true) return null;
        if (!double.TryParse(scale.Text, out var factor) || !double.IsFinite(factor) || factor <= 0) { Status.Text = "匯入單位必須是正數"; return null; }
        try
        {
            IsEnabled = false; Status.Text = "讀取 CAD 副本…";
            var asset = await new SchematicCadFileLoader().ReadAsync(path, factor);
            if (!asset.Complete)
                MessageBox.Show(owner, string.Join("\n", asset.Diagnostics), "匯入有未支援項目；不可視為完整工程圖", MessageBoxButton.OK, MessageBoxImage.Warning);
            return (asset, path);
        }
        catch (Exception error) { Status.Text = "匯入失敗：" + error.Message; return null; }
        finally { IsEnabled = true; }
    }
}
