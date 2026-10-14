using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private SchematicAttachment? _matingFirst;

    private void Mating_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        CancelCommand(); _pairMode = true;
        Status.Text = "接頭對接：選擇第一個 Port，再選擇對端 Port。";
        Render(_getProject()); Focus();
    }

    private void PickMatingPort(SchematicAttachment attachment)
    {
        if (_pairFirst is not null) { Status.Text = "已選擇導線草稿，請選另一頁的導線草稿，或按 Esc 重新選 Port。"; return; }
        if (_matingFirst is null)
        {
            _matingFirst = attachment; _selectionId = attachment.SymbolId;
            Status.Text = "接頭對接：選擇對端 Port，可切換到另一頁。"; Render(_getProject()); return;
        }
        var first = _matingFirst;
        try
        {
            if (first.EndpointId == attachment.EndpointId) { Status.Text = "請選擇另一個接頭。"; return; }
            var pairs = ConfirmMating(first.EndpointId!, attachment.EndpointId!, out var evidence);
            if (pairs is null) return;
            if (Apply(p => _service.MatePorts(p, first, attachment, pairs, evidence, NextMatingSignal(p)), "已建立接頭對接並更新 Pin 功能"))
                CancelCommand();
        }
        catch (InvalidOperationException error) { Status.Text = error.Message; }
    }

    private static string NextMatingSignal(ElectricalProject project) =>
        "J" + (project.Connections.Count(c => c.MatingMap is not null) + 1) + " 接頭對接";

    private IReadOnlyList<ConnectorMatingPinPair>? ConfirmMating(string fromId, string toId, out string evidence)
    {
        evidence = "";
        var project = _getProject();
        var from = ConnectorMatingService.RequirePort(project, fromId); var to = ConnectorMatingService.RequirePort(project, toId);
        var proposed = ConnectorMatingService.Prepare(project, fromId, toId);
        var dialog = new Window { Title = "接頭對接 / 逐 Pin 核對", Width = 610, Height = 550,
            MinWidth = 500, MinHeight = 350, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new Grid { Margin = new(16) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = new TextBlock { Text = $"{from.Name} {from.Connector!.Family} {from.Connector.Coding} {from.Connector.Gender}  →  " +
            $"{to.Name} {to.Connector!.Family} {to.Connector.Coding} {to.Connector.Gender}", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        grid.Children.Add(label);
        var rows = new StackPanel(); var choices = new List<(string From, ComboBox Choice)>();
        foreach (var pin in from.Pins.OrderBy(p => p.PinNumber, ComponentIntelligence.Electrical.Drawing.DrawingContactDisplayOrder.NumberComparer))
        {
            var row = new Grid { Margin = new(0, 3, 0, 3) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = $"{from.Name} / {pin.PinNumber}  {pin.DisplayFunction}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var choice = new ComboBox { ItemsSource = to.Pins.OrderBy(p => p.PinNumber, ComponentIntelligence.Electrical.Drawing.DrawingContactDisplayOrder.NumberComparer).ToArray(),
                DisplayMemberPath = nameof(ComponentPin.PinNumber), SelectedValuePath = nameof(ComponentPin.PinId), MinHeight = 28,
                SelectedValue = proposed.SingleOrDefault(p => p.FromPinId == pin.PinId)?.ToPinId };
            Grid.SetColumn(choice, 1); row.Children.Add(choice); rows.Children.Add(row); choices.Add((pin.PinId, choice));
        }
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var footer = new StackPanel { Margin = new(0, 12, 0, 8) };
        footer.Children.Add(new TextBlock { Text = "核對依據" }); var source = new TextBox { MinHeight = 28, Text = "使用者逐 Pin 核對", MaxLength = 512 };
        footer.Children.Add(source); var confirmed = new CheckBox { Content = "已核對全部 Pin 對應", Margin = new(0, 8, 0, 0) }; footer.Children.Add(confirmed);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; footer.Children.Add(error);
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var ok = new Button { Content = "建立對接", IsDefault = true, MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Right, Padding = new(12, 4, 12, 4) };
        ok.Click += (_, _) =>
        {
            if (confirmed.IsChecked != true || string.IsNullOrWhiteSpace(source.Text) || choices.Any(c => c.Choice.SelectedItem is not ComponentPin) ||
                choices.Select(c => c.Choice.SelectedValue).Distinct().Count() != choices.Count)
            { error.Text = "請核對每個 Pin 一對一對應，填寫依據並勾選確認。"; return; }
            dialog.DialogResult = true;
        };
        Grid.SetRow(ok, 3); grid.Children.Add(ok); dialog.Content = grid;
        if (dialog.ShowDialog() != true) return null;
        evidence = source.Text.Trim();
        return choices.Select(c => new ConnectorMatingPinPair(c.From, ((ComponentPin)c.Choice.SelectedItem).PinId)).ToArray();
    }

    private void FunctionReview_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        var project = _getProject();
        var names = ConnectorMatingService.Ports(project).SelectMany(port => port.Pins.Select(pin => (pin.PinId, Caption: port.Name + " / " + pin.PinNumber)))
            .ToDictionary(p => p.PinId, p => p.Caption, StringComparer.Ordinal);
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        var pins = symbol is null ? ProjectPinFunctionService.Pins(project) : SchematicSymbolOwner.Resolve(project, symbol).Ports.SelectMany(p => p.Pins).ToArray();
        var dialog = new Window { Title = "Pin 功能 / 自動映射", Width = 820, Height = 620, MinWidth = 650, MinHeight = 360,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var dock = new DockPanel { Margin = new(16) }; var ok = new Button { Content = "套用", IsDefault = true, MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Right, Padding = new(16, 4, 16, 4) };
        DockPanel.SetDock(ok, Dock.Bottom); dock.Children.Add(ok);
        var rows = new StackPanel(); var edits = new List<(string Id, ComboBox Mode, TextBox Value)>();
        foreach (var pin in pins)
        {
            var row = new Grid { Margin = new(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new() { Width = new(170) }); row.ColumnDefinitions.Add(new() { Width = new(130) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = names[pin.PinId], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, ToolTip = pin.PinId });
            var mode = new ComboBox { ItemsSource = new[] { "保留目前", "手動", "自動" }, SelectedIndex = 0, MinHeight = 28, Margin = new(6, 0, 6, 0) };
            var value = new TextBox { Text = pin.DisplayFunction ?? "", IsReadOnly = true, MinHeight = 28, MaxLength = 512,
                ToolTip = pin.InferredFunction is { } link ? "來源：" + names.GetValueOrDefault(link.SourcePinId, link.SourcePinId) + " / " + link.SourcePinId : "已填寫功能" };
            mode.SelectionChanged += (_, _) => value.IsReadOnly = mode.SelectedIndex != 1;
            Grid.SetColumn(mode, 1); Grid.SetColumn(value, 2); row.Children.Add(mode); row.Children.Add(value); rows.Children.Add(row); edits.Add((pin.PinId, mode, value));
        }
        foreach (var issue in ProjectPinFunctionService.Inspect(project))
            rows.Children.Add(new TextBlock { Text = issue.Description, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        dock.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); dialog.Content = dock;
        ok.Click += (_, _) =>
        {
            if (Apply(p => { var next = ComponentIntelligence.Electrical.Editing.ProjectSaveSession.Clone(p);
                foreach (var edit in edits.Where(v => v.Mode.SelectedIndex != 0))
                    ProjectPinFunctionService.SetOverride(next, edit.Id, edit.Value.Text, edit.Mode.SelectedIndex == 1);
                return next; }, "已更新 Pin 功能與映射")) dialog.DialogResult = true;
        };
        dialog.ShowDialog();
    }
}
