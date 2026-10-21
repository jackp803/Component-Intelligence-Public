using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed class SchematicWireSettingsDialog : Window
{
    private readonly SchematicAuthoringService _service = new();
    private readonly Func<Func<ElectricalProject, ElectricalProject>, bool> _apply;
    private readonly SchematicWire _wire;
    private readonly TextBox _name;
    private readonly ComboBox _mode, _awg, _area;
    private readonly TextBlock _status, _basis;
    private readonly Button _accept;
    private SchematicWireSizingProposal? _previewProposal;

    public SchematicWireSettingsDialog(ElectricalProject project, string wireId,
        Func<Func<ElectricalProject, ElectricalProject>, bool> apply, bool showBasis = false, bool manual = false)
    {
        _apply = apply; _wire = project.Schematic!.Wires.Single(w => w.WireId == wireId);
        Title = "導線線號／規格"; Width = 540; Height = 650; MinWidth = 440; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(16) };
        _name = new() { Text = _wire.Designation ?? "", MaxLength = 100, ToolTip = "線號" };
        _mode = new() { ItemsSource = new[] { "保留目前規格", "重新自動建議（待核對）", "手動 AWG", "手動截面積 mm²" }, SelectedIndex = manual ? 2 : 0 };
        var specification = SchematicWirePresentation.Resolve(project, _wire);
        _awg = new() { ItemsSource = Enumerable.Range(0, 41).ToArray(), SelectedItem = specification.Awg, ToolTip = "AWG" };
        _area = new() { ItemsSource = new[] { .14, .25, .34, .5, .75, 1, 1.5, 2.5, 4, 6, 10, 16, 25 }, IsEditable = true,
            Text = specification.AreaMm2?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", ToolTip = "導體截面積 mm²" };
        foreach (var (label, field) in new[] { ("線號", (FrameworkElement)_name), ("設定方式", _mode), ("AWG", _awg), ("截面積 mm²", _area) })
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 5, 0, 3) }); panel.Children.Add(field);
        }
        _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
        panel.Children.Add(_status);
        _accept = new() { Content = "採用建議", HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 5, 12, 5) };
        _accept.Click += (_, _) => { if (TryAcceptSuggestion()) DialogResult = true; }; panel.Children.Add(_accept);
        _basis = new() { Text = SchematicWireSizingPresentation.Basis(project, _wire), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        panel.Children.Add(new Expander { Header = "查看依據", IsExpanded = showBasis, Content = _basis, Margin = new(0, 10, 0, 8) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Padding = new(12, 5, 12, 5) });
        var save = new Button { Content = "套用", IsDefault = true, Padding = new(12, 5, 12, 5), Margin = new(8, 0, 0, 0) };
        save.Click += (_, _) => { if (TryApply()) DialogResult = true; }; buttons.Children.Add(save);
        var dock = new DockPanel(); buttons.Margin = new(16, 8, 16, 16); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = panel, Background = SystemColors.WindowBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = dock;
        _mode.SelectionChanged += (_, _) => UpdateStatus(project);
        _awg.SelectionChanged += (_, _) => UpdateStatus(project);
        _area.LostKeyboardFocus += (_, _) => UpdateStatus(project);
        UpdateStatus(project);
    }

    private ElectricalProject ApplySize(ElectricalProject project) => _mode.SelectedIndex switch
    {
        1 => _service.UseAutomaticWireSize(project, _wire.WireId),
        2 => _service.SetWireAwg(project, _wire.WireId, _awg.SelectedItem is int gauge ? gauge : throw new InvalidOperationException("請先選擇 AWG，未自動套用預設值。")),
        3 => _service.SetWireArea(project, _wire.WireId, double.Parse(_area.Text, CultureInfo.InvariantCulture)),
        _ => project
    };

    public bool TryApply()
    {
        try
        {
            var applied = _apply(project =>
            {
                var next = ApplySize(project);
                return _name.Text.Trim() == (_wire.Designation ?? "") ? next : _service.SetWireDesignation(next, _wire.WireId, _name.Text);
            });
            if (!applied)
            {
                _status.Text = "未套用設定；資料可能已變更，請重新查看導線後再試。";
                _status.Foreground = Brushes.Firebrick;
            }
            return applied;
        }
        catch (Exception error) { _status.Text = error.Message; _status.Foreground = Brushes.Firebrick; return false; }
    }

    public bool TryAcceptSuggestion()
    {
        try
        {
            var applied = _apply(project =>
            {
                var next = _mode.SelectedIndex == 1 ? _service.UseAutomaticWireSize(project, _wire.WireId) : project;
                var current = next.Schematic!.Wires.Single(w => w.WireId == _wire.WireId);
                if (_mode.SelectedIndex > 1 || _previewProposal is null || current.SizingProposal != _previewProposal)
                    throw new InvalidOperationException("建議已變更或沒有可採用的建議；請重新查看後再採用。");
                next = _service.AcceptWireSizeSuggestion(next, _wire.WireId);
                return _name.Text.Trim() == (_wire.Designation ?? "") ? next : _service.SetWireDesignation(next, _wire.WireId, _name.Text);
            });
            if (!applied)
            {
                _status.Text = "未採用建議；資料可能已變更，請重新查看依據後再試。";
                _status.Foreground = Brushes.Firebrick;
            }
            return applied;
        }
        catch (Exception error) { _status.Text = error.Message; _status.Foreground = Brushes.Firebrick; return false; }
    }

    private void UpdateStatus(ElectricalProject project)
    {
        _awg.IsEnabled = _mode.SelectedIndex == 2; _area.IsEnabled = _mode.SelectedIndex == 3;
        try
        {
            var preview = ApplySize(project); var wire = preview.Schematic!.Wires.Single(w => w.WireId == _wire.WireId);
            _status.Foreground = Brushes.DimGray;
            _status.Text = SchematicWireSizingPresentation.Summary(preview, wire);
            _previewProposal = wire.SizingProposal;
            _accept.IsEnabled = _mode.SelectedIndex <= 1 && SchematicWireSizingPresentation.CanAccept(preview, wire);
            _basis.Text = SchematicWireSizingPresentation.Basis(preview, wire);
        }
        catch (Exception error) { _status.Text = error.Message; _status.Foreground = Brushes.Firebrick; _accept.IsEnabled = false; _previewProposal = null; }
    }
}
