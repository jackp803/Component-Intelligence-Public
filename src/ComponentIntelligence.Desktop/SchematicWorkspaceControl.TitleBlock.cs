using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void RenderTitleBlock(ElectricalProject project, SchematicPage page)
    {
        var slots = page.TitleBlockSlots.Count > 0 ? page.TitleBlockSlots :
            SchematicTitleBlock.DetectSlots(page.TemplateGeometry!);
        foreach (var slot in slots)
        {
            var value = SchematicTitleBlock.Value(project, page, slot.Field);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var width = slot.Bounds.Width * 3;
            var height = slot.Bounds.Height * 3;
            var label = new TextBlock { Text = value, Width = width, MaxHeight = height,
                FontSize = Math.Min(9, height * .75), Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false };
            label.Measure(new Size(width, double.PositiveInfinity));
            while (label.DesiredSize.Height > height && label.FontSize > 5)
            {
                label.FontSize -= .5;
                label.Measure(new Size(width, double.PositiveInfinity));
            }
            Canvas.SetLeft(label, slot.Bounds.X * 3);
            Canvas.SetTop(label, slot.Bounds.Y * 3);
            Sheet.Children.Add(label);
        }
    }

    private void TitleBlock_Click(object sender, RoutedEventArgs e)
    {
        var project = _getProject();
        var page = project.Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId);
        if (page is null) return;
        var values = project.Schematic!.TitleBlock;
        var dialog = new Window { Title = "公司圖框標題欄", Width = 430, Height = 580,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16) };
        if (page.TemplateGeometry is null ||
            (page.TitleBlockSlots.Count == 0 && SchematicTitleBlock.DetectSlots(page.TemplateGeometry).Count == 0))
            panel.Children.Add(new TextBlock { Text = "此頁圖框沒有可辨識的標題欄位；資料可以保存，但不會顯示在圖框上。",
                TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10), Foreground = Brushes.DarkRed });
        TextBox Field(string title, string value)
        {
            panel.Children.Add(new TextBlock { Text = title });
            var box = new TextBox { Text = value, Margin = new(0, 3, 0, 9) };
            panel.Children.Add(box);
            return box;
        }
        var projectName = Field("專案名稱", values.ProjectName ?? project.Name ?? "");
        var drawingNumber = Field("圖號", values.DrawingNumber ?? "");
        var drawnBy = Field("繪製", values.DrawnBy ?? "");
        var checkedBy = Field("審核", values.CheckedBy ?? "");
        var revision = Field("版本", values.Revision ?? "");
        panel.Children.Add(new TextBlock { Text = "本頁內容" });
        var automatic = new CheckBox { Content = "依本頁主要模塊更新", IsChecked = page.TitleBlockContentOverride is null,
            Margin = new(0, 4, 0, 6) };
        panel.Children.Add(automatic);
        var suggestion = SchematicTitleBlock.SuggestPageContent(project, page);
        var content = new TextBox { Text = page.TitleBlockContentOverride ?? suggestion, Margin = new(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap, MinHeight = 50, AcceptsReturn = true };
        panel.Children.Add(content);
        void Refresh() { content.IsEnabled = automatic.IsChecked != true; if (automatic.IsChecked == true) content.Text = suggestion; }
        automatic.Checked += (_, _) => Refresh(); automatic.Unchecked += (_, _) => Refresh(); Refresh();
        var ok = new Button { Content = "套用", Padding = new(8), IsDefault = true };
        ok.Click += (_, _) =>
        {
            var settings = new SchematicTitleBlockSettings { ProjectName = projectName.Text,
                DrawingNumber = drawingNumber.Text, DrawnBy = drawnBy.Text,
                CheckedBy = checkedBy.Text, Revision = revision.Text };
            if (Apply(p => _service.SetTitleBlock(p, page.PageId, settings,
                automatic.IsChecked == true ? null : content.Text), "已更新標題欄")) dialog.DialogResult = true;
        };
        panel.Children.Add(ok);
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.ShowDialog();
    }
}
