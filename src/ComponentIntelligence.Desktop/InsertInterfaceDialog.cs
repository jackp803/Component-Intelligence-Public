using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public sealed class InsertInterfaceDialog : Window
{
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "接頭 / Connector", "端子 / Terminal" } };
    private readonly ComboBox _catalog = new() { ItemsSource = CommonConnectorCatalog.Options, DisplayMemberPath = "Display" };
    private readonly ComboBox _from = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _to = new() { DisplayMemberPath = "Label" };
    private readonly TextBox _reference = new();
    private readonly TextBox _function = new();
    public CommonConnectorInsertionDraft? ConnectorDraft { get; private set; }
    public InlineTerminalOptions? TerminalOptions { get; private set; }

    public InsertInterfaceDialog(ElectricalProject project, string connectionId, TopologyConnectionEditor editor)
    {
        Title = "插入介面";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(20) };
        void Field(string label, UIElement control) { body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); body.Children.Add(control); }
        Field("介面類型", _kind);
        Field("共用接頭", _catalog);
        Field("原 A 端接入接點", _from);
        Field("接往原 B 端的接點", _to);
        Field("代號 / Reference", _reference);
        Field("端子功能（可留空）", _function);
        _catalog.IsEnabled = _from.IsEnabled = _to.IsEnabled = _function.IsEnabled = false;
        _kind.SelectionChanged += (_, _) => {
            _catalog.IsEnabled = _from.IsEnabled = _to.IsEnabled = _kind.SelectedIndex == 0;
            _function.IsEnabled = _kind.SelectedIndex == 1;
        };
        _catalog.SelectionChanged += (_, _) => {
            if (_catalog.SelectedItem is not CommonConnectorOption option) return;
            try {
                ConnectorDraft = editor.PrepareCommonConnector(project, connectionId, option.DefinitionId);
                _from.ItemsSource = _to.ItemsSource = ConnectorDraft.Contacts;
                _from.SelectedIndex = _to.SelectedIndex = -1;
                _reference.Text = ConnectorDraft.Reference;
            } catch (InvalidOperationException exception) { ConnectorDraft = null; MessageBox.Show(this, exception.Message, Title); }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, MinWidth = 90 });
        var apply = new Button { Content = "插入", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        apply.Click += (_, _) => {
            if (_kind.SelectedIndex == 0)
            {
                if (ConnectorDraft is null || _from.SelectedItem is not CommonConnectorContact from || _to.SelectedItem is not CommonConnectorContact to || from.PortId == to.PortId)
                { MessageBox.Show(this, "請選擇共用接頭及兩側明確接點。", Title); return; }
                ConnectorDraft.FromContactId = from.EndpointId;
                ConnectorDraft.ToContactId = to.EndpointId;
                ConnectorDraft.Reference = _reference.Text;
            }
            else if (_kind.SelectedIndex == 1)
            {
                ConnectorDraft = null;
                TerminalOptions = new InlineTerminalOptions(_reference.Text, _function.Text);
            }
            else { MessageBox.Show(this, "請選擇接頭或端子。", Title); return; }
            DialogResult = true;
        };
        buttons.Children.Add(apply);
        body.Children.Add(buttons);
        Content = body;
    }
}
