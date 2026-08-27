using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ComponentIntelligence.Desktop;

/// <summary>Scrollable checkpoint before producing an isolated AutoCAD Electrical staging project.</summary>
public sealed class AutocadPreflightDialog : Window
{
    public AutocadPreflightDialog(
        IReadOnlyList<AutocadReviewIssue> issues,
        bool graphAvailable,
        string bindingSidecarPath,
        string drawingEvidenceSidecarPath,
        string symbolAcceptanceRegistryPath)
    {
        ArgumentNullException.ThrowIfNull(issues);

        Title = "AutoCAD Electrical Preflight";
        Width = 1080;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var rows = new ObservableCollection<AutocadReviewIssue>(issues);
        var errorCount = rows.Count(issue => issue.Severity == "Error");
        var warningCount = rows.Count(issue => issue.Severity == "Warning");
        var infoCount = rows.Count(issue => issue.Severity == "Info");
        var canContinue = errorCount == 0 && graphAvailable;

        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var state = new TextBlock
        {
            Text = canContinue
                ? "Continue may start AutoCAD and writes only to a new isolated staging folder."
                : "Resolve Error items before starting an isolated staging review.",
            Foreground = canContinue ? Brushes.DimGray : Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        footer.Children.Add(state);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 8, 0) };
        var continueButton = new Button { Content = "Continue: Build Staging Project", IsDefault = true, IsEnabled = canContinue, Padding = new Thickness(18, 7, 18, 7) };
        continueButton.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(continueButton);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "AutoCAD Electrical preflight", FontSize = 21, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = $"Errors: {errorCount}   Warnings: {warningCount}   Info: {infoCount}",
            FontWeight = FontWeights.SemiBold,
            Foreground = errorCount > 0 ? Brushes.Firebrick : warningCount > 0 ? Brushes.DarkGoldenrod : Brushes.DarkGreen,
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Audited binding sidecar (read-only): {bindingSidecarPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Engineer-approved drawing evidence (read-only): {drawingEvidenceSidecarPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Symbol acceptance registry (read-only): {symbolAcceptanceRegistryPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            RowHeaderWidth = 0
        };
        grid.Columns.Add(TextColumn("Severity", nameof(AutocadReviewIssue.Severity), 90));
        grid.Columns.Add(TextColumn("Code", nameof(AutocadReviewIssue.Code), 210));
        grid.Columns.Add(TextColumn("Message", nameof(AutocadReviewIssue.Message), new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(TextColumn("Source IDs", nameof(AutocadReviewIssue.SourceIds), 240, new SourceIdsConverter()));
        root.Children.Add(grid);
        Content = root;
    }

    private static DataGridTextColumn TextColumn(string header, string propertyName, double width, IValueConverter? converter = null) =>
        TextColumn(header, propertyName, new DataGridLength(width), converter);

    private static DataGridTextColumn TextColumn(string header, string propertyName, DataGridLength width, IValueConverter? converter = null) => new()
    {
        Header = header,
        Width = width,
        Binding = new Binding(propertyName) { Converter = converter }
    };

    private sealed class SourceIdsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is IEnumerable<string> ids ? string.Join(", ", ids) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
