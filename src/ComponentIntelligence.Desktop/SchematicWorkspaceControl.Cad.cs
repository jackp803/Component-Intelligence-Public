using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;

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
                    element = new TextBlock { Text = primitive.Text, FontSize = Math.Max(1, primitive.TextHeight * 3),
                        Foreground = Brushes.Black, RenderTransform = new RotateTransform(primitive.Rotation) };
                    Canvas.SetLeft(element, primitive.Start.X * 3); Canvas.SetTop(element, (primitive.Start.Y - primitive.TextHeight) * 3);
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
            if (element is Shape shape)
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
        var imported = await PickCadAsset(); if (imported is null) return;
        Apply(p => _service.SetSymbolGeometry(p, symbol.SymbolId, imported.Value.Asset, imported.Value.Path), "已套用圖塊草稿；接點需明確綁定");
    }

    private async void ImportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var page = _getProject().Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId); if (page is null) return;
        var imported = await PickCadAsset(); if (imported is null) return;
        var dialog = new Window { Title = "公司圖框與格位", Width = 380, Height = 410, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) };
        TextBox Field(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label }); var box = new TextBox { Text = value, Margin = new(0, 3, 0, 8) }; panel.Children.Add(box); return box;
        }
        var width = Field("紙張寬度 mm", page.Width.ToString(CultureInfo.InvariantCulture));
        var height = Field("紙張高度 mm", page.Height.ToString(CultureInfo.InvariantCulture));
        var margin = Field("格位內框邊距 mm", page.Margin.ToString(CultureInfo.InvariantCulture));
        var columns = Field("格位欄數", page.GridColumns.ToString()); var rows = Field("格位列數", page.GridRows.ToString());
        var ok = new Button { Content = "套用", Padding = new(8) }; panel.Children.Add(ok);
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(width.Text, out var w) || !double.TryParse(height.Text, out var h) || !double.TryParse(margin.Text, out var m) ||
                !int.TryParse(columns.Text, out var c) || !int.TryParse(rows.Text, out var r)) { MessageBox.Show(dialog, "請輸入有效尺寸與格位數"); return; }
            if (Apply(p => _service.SetPageTemplate(p, page.PageId, imported.Value.Asset, imported.Value.Path, w, h, m, c, r), "已套用公司圖框與明確格位")) dialog.DialogResult = true;
        };
        dialog.Content = panel; dialog.ShowDialog();
    }

    private async Task<(SchematicCadAsset Asset, string Path)?> PickCadAsset()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "CAD 圖面|*.dxf;*.dwg;*.dwt", CheckFileExists = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return null;
        var dialog = new Window { Title = "CAD 匯入單位", Width = 360, Height = 170, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) }; var scale = new TextBox { Text = "1", Margin = new(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "每 CAD 單位對應毫米數（1 = mm）" }); panel.Children.Add(scale);
        var ok = new Button { Content = "讀取副本", Padding = new(8) }; ok.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(ok); dialog.Content = panel;
        if (dialog.ShowDialog() != true) return null;
        if (!double.TryParse(scale.Text, out var factor) || !double.IsFinite(factor) || factor <= 0) { Status.Text = "匯入單位必須是正數"; return null; }
        try
        {
            IsEnabled = false; Status.Text = "讀取 CAD 副本…";
            var asset = await new SchematicCadFileLoader().ReadAsync(picker.FileName, factor);
            if (!asset.Complete)
                MessageBox.Show(Window.GetWindow(this), string.Join("\n", asset.Diagnostics), "匯入有未支援項目；不可視為完整工程圖", MessageBoxButton.OK, MessageBoxImage.Warning);
            return (asset, picker.FileName);
        }
        catch (Exception error) { Status.Text = "匯入失敗：" + error.Message; return null; }
        finally { IsEnabled = true; }
    }
}
