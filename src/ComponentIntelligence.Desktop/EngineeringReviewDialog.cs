using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public sealed class EngineeringReviewDialog : Window
{
    private readonly EngineeringReviewService _service;
    private readonly Func<ElectricalProject> _project;
    private readonly Func<ElectricalProject, string, Task> _save;
    private readonly TextBlock _status = Text("");
    private readonly ListBox _components = new() { DisplayMemberPath = "Label" };
    private readonly ListBox _cables = new() { DisplayMemberPath = "Label" };
    private readonly StackPanel _componentDetail = new();
    private readonly StackPanel _cableDetail = new();
    private bool _saving;

    public EngineeringReviewDialog(EngineeringReviewService service, Func<ElectricalProject> project,
        Func<ElectricalProject, string, Task> save)
    {
        _service = service; _project = project; _save = save;
        Title = "工程確認 / Engineering Review";
        Width = 1180; Height = 820; MinWidth = 850; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(Button("重新檢查", async () => await RefreshAsync()));
        footer.Children.Add(Button("關閉 / 取消未確認項目", () => { if (!_saving) Close(); return Task.CompletedTask; }));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "元件身分與端點", Content = Split(_components, _componentDetail) });
        tabs.Items.Add(new TabItem { Header = "線材製作方式", Content = Split(_cables, _cableDetail) });
        root.Children.Add(tabs); Content = root;
        _components.SelectionChanged += (_, _) => ShowComponent();
        _cables.SelectionChanged += (_, _) => ShowCable();
        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }

    private async Task RefreshAsync()
    {
        try
        {
            var coverage = await _service.InspectAsync(_project());
            _components.ItemsSource = coverage.Components.Where(c => c.Classification == "UNSAFE_UNRESOLVED").ToArray();
            _cables.ItemsSource = coverage.Cables.Where(c => c.Classification == "UNSAFE_UNRESOLVED").ToArray();
            _status.Text = $"待確認元件型別：{coverage.UnsafeComponentRoles}　待確認線材：{coverage.UnsafeCableRoles}　" +
                (coverage.IsSafe ? "Coverage 通過；仍需正式出圖前檢查。" : "Coverage 尚未通過");
            _componentDetail.Children.Clear(); _cableDetail.Children.Clear();
        }
        catch (Exception ex) { _status.Text = "檢查失敗，未核准出圖：" + ex.Message; }
    }

    private void ShowCable()
    {
        _cableDetail.Children.Clear();
        if (_cables.SelectedItem is not CableEngineeringReview row) return;
        _cableDetail.Children.Add(Text(row.Label, 18));
        _cableDetail.Children.Add(Text($"既有 cable/model（僅上下文）：{row.ModelContext}\n目前 ConstructionType：{row.ConstructionType}"));
        _cableDetail.Children.Add(Text("From / 來源元件與介面\n" + (row.From.Length == 0 ? "尚無明確端點" : row.From)));
        _cableDetail.Children.Add(Text("To / 目的元件與介面\n" + (row.To.Length == 0 ? "尚無明確端點" : row.To)));
        _cableDetail.Children.Add(Text("製作方式"));
        var choice = new ComboBox { MinHeight = 32, SelectedIndex = -1, DisplayMemberPath = "Label",
            ItemsSource = new[] { new CableChoice("外購成品線 / Purchased", CableConstructionType.Purchased), new CableChoice("自製／加工線 / Custom", CableConstructionType.Custom) } };
        _cableDetail.Children.Add(choice);
        var consequence = Text(""); _cableDetail.Children.Add(consequence);
        choice.SelectionChanged += (_, _) => consequence.Text = (choice.SelectedItem as CableChoice)?.Value == CableConstructionType.Custom
            ? "Custom 可能需要 Cable Detail 與 Pin/Core Mapping evidence；選擇製作方式不會自動建立這些工程資料。" : "";
        _cableDetail.Children.Add(Button("確認並儲存此線材", async () =>
        {
            var selected = choice.SelectedItem as CableChoice;
            var draft = _service.ReviewCable(_project(), row.CableId, selected?.Value, selected is not null);
            await SaveAsync(draft, $"Human cable confirmation: {row.CableId} = {selected!.Value}");
        }, mutation: true));
        _cableDetail.Children.Add(Button("略過，不變更", () => { _cables.SelectedIndex = -1; return Task.CompletedTask; }));
        _cableDetail.Children.Add(Trace(row.CableId));
    }

    private void ShowComponent()
    {
        _componentDetail.Children.Clear();
        if (_components.SelectedItem is not ComponentEngineeringReview row) return;
        _componentDetail.Children.Add(Text(row.Label, 18));
        _componentDetail.Children.Add(Text($"既有型錄／exact cache Manufacturer / Model（未等於身分核准）：{row.Manufacturer} / {row.Model}\n{row.Reason}"));
        _componentDetail.Children.Add(Text("實際連接上下文\n" + (row.Context.Length == 0 ? "目前沒有 connection" : row.Context)));
        if (InlineInterfaceRepresentation.IsRecognized(row.DefinitionId))
        {
            _componentDetail.Children.Add(Text("此為專案中性介面，請補足明確的介面／接點證據；不可用型錄身分替代。"));
            _componentDetail.Children.Add(Button("略過，不變更", () => { _components.SelectedIndex = -1; return Task.CompletedTask; }));
            return;
        }
        _componentDetail.Children.Add(Text("權威型錄候選（未自動配對）"));
        var candidates = new ComboBox { ItemsSource = _service.Candidates, DisplayMemberPath = "Label", SelectedIndex = -1, MinHeight = 32 };
        _componentDetail.Children.Add(candidates);
        var candidateEvidence = Text(""); _componentDetail.Children.Add(candidateEvidence);
        var mappingPanel = new StackPanel(); _componentDetail.Children.Add(mappingPanel);
        var mappings = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        candidates.SelectionChanged += (_, _) =>
        {
            mappings.Clear(); mappingPanel.Children.Clear();
            if (candidates.SelectedItem is not EngineeringCandidate selected) return;
            candidateEvidence.Text = "候選證據／原因：" + selected.Evidence;
            var catalogEndpoints = _service.CatalogEndpoints(selected.ComponentId);
            foreach (var endpoint in row.Endpoints)
            {
                mappingPanel.Children.Add(Text((endpoint.Required ? "實際使用 · " : "既有端點 · ") + endpoint.Kind + " / " + endpoint.Label));
                var combo = new ComboBox { ItemsSource = catalogEndpoints.Where(e => e.Kind == endpoint.Kind).ToArray(),
                    DisplayMemberPath = "Label", SelectedIndex = -1, MinHeight = 30, ToolTip = endpoint.Id };
                mappings.Add(endpoint.Id, combo); mappingPanel.Children.Add(combo);
            }
        };
        _componentDetail.Children.Add(Text("身分及端點對照證據（文件／頁碼／工程確認依據）"));
        var evidence = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        _componentDetail.Children.Add(evidence);
        var confirmed = new CheckBox { Content = "我已確認 Component identity 與全部 Port / Pin 對照有明確證據", Margin = new Thickness(0, 12, 0, 8) };
        _componentDetail.Children.Add(confirmed);
        _componentDetail.Children.Add(Button("確認並儲存此元件", async () =>
        {
            var selected = candidates.SelectedItem as EngineeringCandidate;
            var selectedMappings = mappings.Where(m => m.Value.SelectedItem is EngineeringEndpoint)
                .ToDictionary(m => m.Key, m => ((EngineeringEndpoint)m.Value.SelectedItem).Id, StringComparer.Ordinal);
            var draft = await _service.ReviewComponentAsync(_project(), row.InstanceId, selected?.ComponentId,
                selectedMappings, evidence.Text, confirmed.IsChecked == true);
            await SaveAsync(draft, $"Human component confirmation: {row.InstanceId} -> {selected!.ComponentId}; evidence={evidence.Text}; mappings={JsonSerializer.Serialize(selectedMappings)}");
        }, mutation: true));
        _componentDetail.Children.Add(Button("保持 unresolved／略過", () => { _components.SelectedIndex = -1; return Task.CompletedTask; }));
        _componentDetail.Children.Add(Trace(row.DefinitionId + "\n" + row.InstanceId));
    }

    private async Task SaveAsync(ElectricalProject draft, string evidence)
    {
        await _save(draft, evidence);
        await RefreshAsync();
    }
    private Button Button(string label, Func<Task> action, bool mutation = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 8, 8, 0) };
        button.Click += async (_, _) =>
        {
            if (_saving) return;
            if (mutation) { _saving = true; IsEnabled = false; }
            try { await action(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "工程確認", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { if (mutation) { _saving = false; IsEnabled = true; } }
        };
        return button;
    }
    private static Grid Split(ListBox list, StackPanel detail)
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Label"));
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(FrameworkElement.MaxWidthProperty, 255.0);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 5, 3, 5));
        list.DisplayMemberPath = "";
        list.ItemTemplate = new DataTemplate { VisualTree = label };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.Margin = new Thickness(0, 0, 16, 0); grid.Children.Add(list);
        var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); return grid;
    }
    private static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
    private static Expander Trace(string id) => new() { Header = "追溯 ID", Content = Text(id), Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 0) };
    private sealed record CableChoice(string Label, CableConstructionType Value);
}
