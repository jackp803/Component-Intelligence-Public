using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl
{
    public DrawingRuntimeSettingsStore RuntimeSettingsStore { get; set; } = new();
    public Func<DrawingRuntimeSettings, DrawingGenerationCoordinator>? GenerationCoordinatorFactory { get; set; }

    private async void GeneratePreviewGate_Click(object sender, RoutedEventArgs e) => await RunGenerationAsync(fullGeneration: false);
    private async void GenerateAutoCadGate_Click(object sender, RoutedEventArgs e) => await RunGenerationAsync(fullGeneration: true);

    private async Task RunGenerationAsync(bool fullGeneration)
    {
        if (ProjectProvider is null || ProjectReplaced is null || GenerationCoordinatorFactory is null)
        {
            StatusText.Text = "Drawing generation coordinator is not configured."; return;
        }
        try
        {
            var settings = RuntimeSettingsStore.Load();
            var runtime = DrawingRuntimeSettingsValidator.Validate(settings);
            ShowIssues(runtime.Issues);
            if (settings is null || !runtime.IsValid)
            {
                StatusText.Text = "Runtime settings are invalid. Open Runtime Settings and choose actual local paths."; return;
            }
            var coordinator = GenerationCoordinatorFactory(settings);
            var project = ProjectProvider();
            var result = fullGeneration
                ? await coordinator.GenerateAutoCadAsync(project, runtime, CancellationToken.None)
                : await coordinator.GeneratePreviewAsync(project, runtime, CancellationToken.None);
            if (result.DrawingPlan is not null) { project.DrawingPlan = result.DrawingPlan; ProjectReplaced(project); LoadPlan(result.DrawingPlan); }
            ShowIssues(result.Preflight.Issues.Concat(result.DrawingIr?.Issues ?? []).ToArray());
            StatusText.Text = result.Status switch
            {
                DrawingGenerationStatus.PreviewReady => $"Progressive Preview ready. Eligible pages: {string.Join(", ", result.Preflight.EligiblePageIds)}",
                DrawingGenerationStatus.ReadyForCp3C => "electrical-drawing-ir.v2 READY → READY_FOR_CP3C. No DWG/WDP has been generated.",
                _ => "Generation blocked. Double-click an issue to navigate to its target page when available."
            };
        }
        catch (Exception ex) { StatusText.Text = $"Drawing generation failed closed: {ex.Message}"; }
    }

    private void ConfigureRuntime_Click(object sender, RoutedEventArgs e)
    {
        var existing = RuntimeSettingsStore.Load();
        var dialog = new Window { Owner = Window.GetWindow(this), Title = "Drawing Runtime Settings", Width = 720, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var grid = new Grid { Margin = new Thickness(12) }; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var python = new TextBox { Text = existing?.PythonExecutable ?? string.Empty, Margin = new Thickness(6) }; var root = new TextBox { Text = existing?.AutomationRoot ?? string.Empty, Margin = new Thickness(6) };
        var save = new Button { Content = "Validate and Save", Padding = new Thickness(12,6,12,6), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right };
        grid.Children.Add(new TextBlock { Text = "pythonExecutable", VerticalAlignment = VerticalAlignment.Center }); Grid.SetRow(python, 0); Grid.SetColumn(python, 1); grid.Children.Add(python);
        var rootLabel = new TextBlock { Text = "automationRoot", VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(rootLabel, 1); grid.Children.Add(rootLabel); Grid.SetRow(root, 1); Grid.SetColumn(root, 1); grid.Children.Add(root);
        Grid.SetRow(save, 2); Grid.SetColumn(save, 1); grid.Children.Add(save); dialog.Content = grid;
        save.Click += (_, _) =>
        {
            try { RuntimeSettingsStore.Save(new DrawingRuntimeSettings { PythonExecutable = python.Text.Trim(), AutomationRoot = root.Text.Trim() }); dialog.DialogResult = true; }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Invalid drawing runtime", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        if (dialog.ShowDialog() == true) StatusText.Text = "Drawing runtime settings validated and saved to the user-local profile.";
    }

    private void ShowIssues(IEnumerable<DrawingActionableIssue> issues)
    {
        IssueList.ItemsSource = issues.OrderBy(x => x.Severity).ThenBy(x => x.IssueId, StringComparer.Ordinal).Select(x => new DrawingIssueRow(x)).ToArray();
    }

    private void IssueList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IssueList.SelectedItem is not DrawingIssueRow row || string.IsNullOrWhiteSpace(row.Issue.PageId) || CurrentPlan is null) return;
        var page = CurrentPlan.Pages.FirstOrDefault(x => x.PageId == row.Issue.PageId); if (page is null) return;
        PageList.SelectedItem = page;
    }

    private sealed record DrawingIssueRow(DrawingActionableIssue Issue)
    {
        public string Display => $"[{Issue.Severity}] {Issue.Code} | {Issue.Message} | {Issue.PageId ?? Issue.ObjectId ?? "global"}";
        public override string ToString() => Display;
    }
}
