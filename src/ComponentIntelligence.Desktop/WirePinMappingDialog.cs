using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

public sealed class WirePinMappingDialog : Window
{
    private sealed record PinChoice(string Id, string Label);
    private readonly ComboBox _from = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _to = new() { DisplayMemberPath = "Label" };
    public string FromPinId => ((PinChoice)_from.SelectedItem).Id;
    public string ToPinId => ((PinChoice)_to.SelectedItem).Id;

    public WirePinMappingDialog(ComponentPort from, ComponentPort to, ElectricalConnection connection)
    {
        Title = "普通配線腳位對應";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(20) };
        void Side(ComboBox combo, ComponentPort port, string endpointId, string label)
        {
            var choices = port.Pins.Select(p => new PinChoice(p.PinId, $"Pin {p.PinNumber} / {p.Function ?? "未定義功能"}")).ToArray();
            combo.ItemsSource = choices;
            combo.SelectedItem = choices.SingleOrDefault(p => p.Id == endpointId);
            body.Children.Add(new TextBlock { Text = $"{label} / {port.Name}", Margin = new Thickness(0, 10, 0, 4) });
            body.Children.Add(combo);
        }
        Side(_from, from, connection.FromEndpointId, "A 端");
        Side(_to, to, connection.ToEndpointId, "B 端");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, MinWidth = 90 });
        var apply = new Button { Content = "套用", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        apply.Click += (_, _) => {
            if (_from.SelectedItem is not PinChoice || _to.SelectedItem is not PinChoice)
            { MessageBox.Show(this, "請明確選擇兩端 Pin。", Title); return; }
            DialogResult = true;
        };
        buttons.Children.Add(apply);
        body.Children.Add(buttons);
        Content = body;
    }
}
