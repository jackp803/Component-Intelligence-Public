using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void ReviewDraft_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        var project = _getProject();
        var issues = SchematicDraftReview.Inspect(project);
        var rows = issues.Select(issue => new
        {
            Issue = issue,
            Page = project.Schematic!.Pages.Select((p, i) => (p, i)).Where(x => x.p.PageId == issue.PageId)
                .Select(x => $"{x.i + 1:00} {x.p.Title}").Single(),
            issue.Description
        }).ToArray();
        var dialog = new Window { Title = "草稿缺口", Width = 780, Height = 500, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new(12) };
        var state = new TextBlock { Text = $"已辨識 {issues.Count} 項草稿缺口；工程核准：未完成。", Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(state, Dock.Top); panel.Children.Add(state);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = "頁面", Binding = new Binding("Page"), Width = 160 });
        grid.Columns.Add(new DataGridTextColumn { Header = "待確認事項", Binding = new Binding("Description"), Width = new(1, DataGridLengthUnitType.Star),
            ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } } });
        var go = new Button { Content = "前往選取項目", IsEnabled = false, Padding = new(12, 6, 12, 6), Margin = new(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(go, Dock.Bottom); panel.Children.Add(go);
        grid.SelectionChanged += (_, _) => go.IsEnabled = grid.SelectedIndex >= 0;
        go.Click += (_, _) =>
        {
            var selected = rows.FirstOrDefault(row => ReferenceEquals(row, grid.SelectedItem));
            if (selected is null) return;
            var issue = selected.Issue;
            CancelCommand(); _pageId = issue.PageId; _selectionId = issue.ObjectId;
            RefreshWorkspace(); Sheet.BringIntoView(); dialog.Close();
            Status.Text = issue.Description;
        };
        panel.Children.Add(grid); dialog.Content = panel; dialog.ShowDialog();
    }
}
