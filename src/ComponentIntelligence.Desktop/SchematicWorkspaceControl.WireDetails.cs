using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void RefreshWireNames_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveInspectorDraft()) Apply(p => _service.RefreshAutomaticWireNames(p), "已更新自動線號；手動線號與鎖定導線保留");
    }

    private void WireDetails_Click(object sender, RoutedEventArgs e)
    {
        var grid = CreateWireDetailsGrid(SchematicWireLegendPresentation.Entries(_getProject()));
        new Window { Title = "導線明細", Content = grid, Width = 1200, Height = 650, MinWidth = 700, MinHeight = 400,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }

    private static DataGrid CreateWireDetailsGrid(IReadOnlyList<SchematicWireLegendEntry> entries)
    {
        var view = new CollectionViewSource { Source = entries };
        view.GroupDescriptions.Add(new PropertyGroupDescription("Specification.Text"));
        var grid = new DataGrid { ItemsSource = view.View, AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(12) };
        foreach (var (title, path, width) in new[] { ("索引", "Index", 60d), ("完整線號", "Designation", 220d),
            ("頁面", "PageNumber", 70d), ("接點", "Contacts", 300d), ("功能", "Functions", 350d),
            ("線色", "Color", 90d), ("核對備註", "Review", 280d) })
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4)));
            style.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding(path)));
            grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, ElementStyle = style });
        }
        var header = new FrameworkElementFactory(typeof(TextBlock));
        header.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        header.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        header.SetValue(TextBlock.MarginProperty, new Thickness(8));
        grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = header } });
        return grid;
    }
}
