using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl
{
    public DrawingRuntimeSettingsStore RuntimeSettingsStore { get; set; } = new();
    public DrawingExecutorRuntimeSettingsStore ExecutorRuntimeSettingsStore { get; set; } = new();
    public DrawingProjectMetadataStore ProjectMetadataStore { get; set; } = new();
    public Func<DrawingRuntimeSettings, DrawingGenerationCoordinator>? GenerationCoordinatorFactory { get; set; }
    private DrawingExecutorResult? _lastExecutorResult;

    private async void GeneratePreviewGate_Click(object sender, RoutedEventArgs e) => await RunGenerationAsync(fullGeneration: false);
    private async void GenerateAutoCadGate_Click(object sender, RoutedEventArgs e) => await RunGenerationAsync(fullGeneration: true);

    private async Task RunGenerationAsync(bool fullGeneration)
    {
        ResetOutputActions();
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
            ShowIssues(result.Preflight.Issues.Concat(result.DrawingIr?.Issues ?? []).Concat(result.ExecutorResult?.Issues ?? []).ToArray());
            if (result.Status == DrawingGenerationStatus.Applied && result.ExecutorResult is not null)
            {
                _lastExecutorResult = result.ExecutorResult;
                OpenOutputFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ExecutorResult.StagingRoot) || !string.IsNullOrWhiteSpace(result.ExecutorResult.ProjectFile);
                OpenOutputProjectButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ExecutorResult.ProjectFile);
            }
            StatusText.Text = result.Status switch
            {
                DrawingGenerationStatus.PreviewReady => $"Progressive Preview ready. Eligible pages: {string.Join(", ", result.Preflight.EligiblePageIds)}",
                DrawingGenerationStatus.ReadyForCp3C => "electrical-drawing-ir.v2 READY → READY_FOR_CP3C. Executor is not configured; no DWG/WDP has been generated.",
                DrawingGenerationStatus.Applied => $"APPLIED to ISOLATED_STAGING: {result.ExecutorResult?.ProjectFile}. APPLIED != VERIFIED; CP3-D Trusted Readback is not part of this operation.",
                DrawingGenerationStatus.ExecutionFailed => "AutoCAD Electrical staging execution failed or was blocked. No APPLIED claim is made; review the actionable issues.",
                _ => "Generation blocked. Double-click an issue to navigate to its target page when available."
            };
        }
        catch (Exception ex) { StatusText.Text = $"Drawing generation failed closed: {ex.Message}"; }
    }

    private void ConfigureRuntime_Click(object sender, RoutedEventArgs e)
    {
        var existing = RuntimeSettingsStore.Load();
        var existingExecutor = ExecutorRuntimeSettingsStore.Load();
        var dialog = new Window { Owner = Window.GetWindow(this), Title = "Drawing / CP3-C Runtime Settings", Width = 780, Height = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var grid = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 9; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var python = AddRow("pythonExecutable", existingExecutor?.PythonExecutable ?? existing?.PythonExecutable ?? string.Empty, 0);
        var root = AddRow("automationRoot", existingExecutor?.AutomationRoot ?? existing?.AutomationRoot ?? string.Empty, 1);
        var accore = AddRow("accoreConsolePath", existingExecutor?.AccoreConsolePath ?? string.Empty, 2);
        var staging = AddRow("stagingRoot (parent)", existingExecutor?.StagingRoot ?? string.Empty, 3);
        var baseline = AddRow("projectBaselineWdp", existingExecutor?.ProjectBaselineWdp ?? string.Empty, 4);
        var template = AddRow("drawingTemplatePath", existingExecutor?.DrawingTemplatePath ?? string.Empty, 5);
        var wdt = AddRow("projectTitleBlockMappingPath", existingExecutor?.ProjectTitleBlockMappingPath ?? string.Empty, 6);
        var wdl = AddRow("projectDescriptionLabelsPath", existingExecutor?.ProjectDescriptionLabelsPath ?? string.Empty, 7);
        var save = new Button { Content = "Validate and Save", Padding = new Thickness(12,6,12,6), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(save, 8); Grid.SetColumn(save, 1); grid.Children.Add(save); dialog.Content = grid;

        TextBox AddRow(string label, string value, int row)
        {
            var text = new TextBox { Text = value, Margin = new Thickness(6) };
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(caption, row); Grid.SetRow(text, row); Grid.SetColumn(text, 1); grid.Children.Add(caption); grid.Children.Add(text); return text;
        }

        save.Click += (_, _) =>
        {
            try
            {
                RuntimeSettingsStore.Save(new DrawingRuntimeSettings { PythonExecutable = python.Text.Trim(), AutomationRoot = root.Text.Trim() });
                var executorFields = new[] { accore.Text, staging.Text, baseline.Text, template.Text, wdt.Text, wdl.Text };
                if (executorFields.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    ExecutorRuntimeSettingsStore.Save(new DrawingExecutorRuntimeSettings
                    {
                        PythonExecutable = python.Text.Trim(), AutomationRoot = root.Text.Trim(), AccoreConsolePath = accore.Text.Trim(),
                        StagingRoot = staging.Text.Trim(), ProjectBaselineWdp = baseline.Text.Trim(), DrawingTemplatePath = template.Text.Trim(),
                        ProjectTitleBlockMappingPath = wdt.Text.Trim(), ProjectDescriptionLabelsPath = wdl.Text.Trim()
                    });
                }
                dialog.DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Invalid drawing runtime", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        if (dialog.ShowDialog() == true) StatusText.Text = "Drawing runtime settings validated and saved to the user-local profile. CP3-C settings are stored separately from engineering project truth.";
    }

    private void ConfigureProjectMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectProvider is null) { StatusText.Text = "Project metadata is unavailable until a project is loaded."; return; }
        var project = ProjectProvider();
        var existing = ProjectMetadataStore.Load(project.ProjectId);
        var dialog = new Window { Owner = Window.GetWindow(this), Title = "Company Project Metadata", Width = 720, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        TextBox AddRow(string label, string? value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var caption = new TextBlock { Text = label, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBox { Text = value ?? string.Empty, Margin = new Thickness(4) };
            Grid.SetRow(caption, row); Grid.SetRow(text, row); Grid.SetColumn(text, 1); grid.Children.Add(caption); grid.Children.Add(text); row++;
            return text;
        }
        var projectName = AddRow("Project name", existing.ProjectName);
        var partNumber = AddRow("Equipment part number", existing.EquipmentPartNumber);
        var checker = AddRow("Checker", existing.Checker);
        var drafter = AddRow("Drafter", existing.Drafter);
        var revision = AddRow("Revision", existing.Revision);
        var pageRows = new List<(string PageId, TextBox Title, TextBox DocumentNumber)>();
        var existingPages = existing.Pages.ToDictionary(x => x.PageId, StringComparer.Ordinal);
        foreach (var page in (_controller.CurrentPlan?.Pages ?? []).OrderBy(x => x.Order).ThenBy(x => x.PageId, StringComparer.Ordinal))
        {
            existingPages.TryGetValue(page.PageId, out var pageMetadata);
            var title = AddRow($"{page.PageId} title", pageMetadata?.DrawingTitle);
            var documentNumber = AddRow($"{page.PageId} document no.", pageMetadata?.DrawingDocumentNumber);
            pageRows.Add((page.PageId, title, documentNumber));
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var save = new Button { Content = "Save metadata", Padding = new Thickness(12,6,12,6), Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(save, row); Grid.SetColumn(save, 1); grid.Children.Add(save);
        dialog.Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        save.Click += (_, _) =>
        {
            ProjectMetadataStore.Save(project.ProjectId, new DrawingProjectMetadata
            {
                ProjectName = NullIfBlank(projectName.Text), EquipmentPartNumber = NullIfBlank(partNumber.Text),
                Checker = NullIfBlank(checker.Text), Drafter = NullIfBlank(drafter.Text), Revision = NullIfBlank(revision.Text),
                Pages = pageRows.Select(x => new DrawingPageMetadata { PageId = x.PageId, DrawingTitle = NullIfBlank(x.Title.Text), DrawingDocumentNumber = NullIfBlank(x.DocumentNumber.Text) }).ToArray()
            });
            dialog.DialogResult = true;
        };
        if (dialog.ShowDialog() == true) StatusText.Text = "Company project metadata saved to the user-local profile. Customer remains unmapped because company evidence does not define it.";
        static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _lastExecutorResult?.StagingRoot ?? (_lastExecutorResult?.ProjectFile is null ? null : Path.GetDirectoryName(_lastExecutorResult.ProjectFile));
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenOutputProject_Click(object sender, RoutedEventArgs e)
    {
        var path = _lastExecutorResult?.ProjectFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ResetOutputActions()
    {
        _lastExecutorResult = null;
        OpenOutputFolderButton.IsEnabled = false;
        OpenOutputProjectButton.IsEnabled = false;
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
