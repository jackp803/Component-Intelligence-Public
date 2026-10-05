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
    private readonly TextBlock _status;

    public SchematicWireSettingsDialog(ElectricalProject project, string wireId,
        Func<Func<ElectricalProject, ElectricalProject>, bool> apply)
    {
        _apply = apply; _wire = project.Schematic!.Wires.Single(w => w.WireId == wireId);
        Title = "導線線號／規格"; Width = 490; Height = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(16) };
        _name = new() { Text = _wire.Designation ?? "", MaxLength = 100, ToolTip = "線號" };
        _mode = new() { ItemsSource = new[] { "保留目前規格", "重新自動建議（待核對）", "手動 AWG", "手動截面積 mm²" }, SelectedIndex = 0 };
        _awg = new() { ItemsSource = Enumerable.Range(0, 41).ToArray(), SelectedItem = _wire.Awg ?? 20, ToolTip = "AWG" };
        var specification = SchematicWirePresentation.Resolve(project, _wire);
        _area = new() { ItemsSource = new[] { .14, .25, .34, .5, .75, 1, 1.5, 2.5, 4, 6, 10, 16, 25 }, IsEditable = true,
            Text = (specification.AreaMm2 ?? .5).ToString("0.###", CultureInfo.InvariantCulture), ToolTip = "導體截面積 mm²" };
        foreach (var (label, field) in new[] { ("線號", (FrameworkElement)_name), ("設定方式", _mode), ("AWG", _awg), ("截面積 mm²", _area) })
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 5, 0, 3) }); panel.Children.Add(field);
        }
        _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
        panel.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Padding = new(12, 5, 12, 5) });
        var save = new Button { Content = "套用", IsDefault = true, Padding = new(12, 5, 12, 5), Margin = new(8, 0, 0, 0) };
        save.Click += (_, _) => { if (TryApply()) DialogResult = true; }; buttons.Children.Add(save); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, Background = SystemColors.WindowBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _mode.SelectionChanged += (_, _) => UpdateStatus(project);
        _awg.SelectionChanged += (_, _) => UpdateStatus(project);
        _area.LostKeyboardFocus += (_, _) => UpdateStatus(project);
        UpdateStatus(project);
    }

    private ElectricalProject ApplySize(ElectricalProject project) => _mode.SelectedIndex switch
    {
        1 => _service.UseAutomaticWireSize(project, _wire.WireId),
        2 => _service.SetWireAwg(project, _wire.WireId, (int)_awg.SelectedItem),
        3 => _service.SetWireArea(project, _wire.WireId, double.Parse(_area.Text, CultureInfo.InvariantCulture)),
        _ => project
    };

    public bool TryApply()
    {
        try
        {
            return _apply(project =>
            {
                var next = ApplySize(project);
                return _name.Text.Trim() == (_wire.Designation ?? "") ? next : _service.SetWireDesignation(next, _wire.WireId, _name.Text);
            });
        }
        catch (Exception error) { _status.Text = error.Message; _status.Foreground = Brushes.Firebrick; return false; }
    }

    private void UpdateStatus(ElectricalProject project)
    {
        _awg.IsEnabled = _mode.SelectedIndex == 2; _area.IsEnabled = _mode.SelectedIndex == 3;
        try
        {
            var preview = ApplySize(project); var wire = preview.Schematic!.Wires.Single(w => w.WireId == _wire.WireId);
            var specification = SchematicWirePresentation.Resolve(preview, wire);
            _status.Foreground = Brushes.DimGray;
            _status.Text = (specification.AreaMm2 is double area ? $"{area:0.###} mm²" : "規格待設定") +
                (specification.Awg is int awg ? $" / {(specification.AwgIsApproximate ? "約 " : "")}AWG {awg}" : "");
            if (wire.SizingProposal is { } proposal)
                _status.Text += (proposal.LoadCurrentAmp is double load ? $"\n已知負載小計：{load:0.###} A" : "\n負載未知：Sensor 草稿預設") +
                    "\n待核對：" + proposal.Review;
        }
        catch (Exception error) { _status.Text = error.Message; _status.Foreground = Brushes.Firebrick; }
    }
}
