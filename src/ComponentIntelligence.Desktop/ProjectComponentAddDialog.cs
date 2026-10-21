using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed class ProjectComponentAddDialog : Window
{
    private readonly IReadOnlyList<ComponentIR> _catalog;
    private readonly TextBox _search = new() { MinHeight = 30 };
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(Choice.Label) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly TextBox _manufacturer = new() { Text = "自製", MinHeight = 30 };
    private readonly TextBox _model = new() { MinHeight = 30 };
    private readonly TextBox _category = new() { Text = "自製元件", MinHeight = 30 };
    private readonly TextBlock _contacts = new() { Text = "Port 0 / Pin 0", Margin = new(0, 8, 0, 8) };
    private readonly TextBox _quantity = new() { Text = "1", Width = 90, MinHeight = 30, ToolTip = "新增數量：1 到 1000" };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 6) };
    private readonly TabControl _tabs = new();
    private ComponentIR _custom = new() { Identity = new() { ComponentId = "project-local:" + Guid.NewGuid().ToString("N"), Manufacturer = "自製", Model = "" } };
    private sealed record Choice(ComponentIR Definition)
    {
        public string Label => $"{Definition.Identity.Manufacturer}  {Definition.Identity.Model}  /  {Definition.Classification.Category ?? "未分類"}";
    }

    public ComponentIR? SelectedComponent { get; private set; }
    public int Quantity { get; private set; }

    public ProjectComponentAddDialog(IReadOnlyList<ComponentIR> catalog, string? warning = null)
    {
        _catalog = catalog.Where(c => ComponentMaterialRolePolicy.Classify(c) != BomTopologyDisposition.DeferredConnectionMaterial).ToArray();
        Title = "新增專案元件"; Width = 760; Height = 620; MinWidth = 560; MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(14) };
        var footer = new StackPanel();
        if (warning is not null) footer.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick });
        footer.Children.Add(_error);
        var commands = new DockPanel { LastChildFill = false };
        commands.Children.Add(new TextBlock { Text = "新增數量", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) });
        commands.Children.Add(_quantity);
        var add = new Button { Content = "加入專案", IsDefault = true, Padding = new(16, 6, 16, 6), Margin = new(8, 0, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(16, 6, 16, 6) };
        DockPanel.SetDock(add, Dock.Right); commands.Children.Add(add);
        DockPanel.SetDock(cancel, Dock.Right); commands.Children.Add(cancel);
        add.Click += (_, _) => Accept(); footer.Children.Add(commands);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        var library = new DockPanel { Margin = new(10) };
        _search.ToolTip = "搜尋廠牌、型號、分類";
        DockPanel.SetDock(_search, Dock.Top); library.Children.Add(_search);
        DockPanel.SetDock(_details, Dock.Bottom); library.Children.Add(_details);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemContainerStyle = new Style(typeof(ListBoxItem));
        _list.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 8, 5, 8)));
        _list.ItemContainerStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 660d));
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Choice.Label)));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        _list.DisplayMemberPath = ""; _list.ItemTemplate = new DataTemplate { VisualTree = text };
        library.Children.Add(_list);
        _search.TextChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => { var c = (_list.SelectedItem as Choice)?.Definition;
            _details.Text = c is null ? "" : $"{c.Identity.Manufacturer} {c.Identity.Model}\nPort {c.Ports.Count} / Pin {c.Pins.Count}"; };
        _tabs.Items.Add(new TabItem { Header = "元件庫", Content = library });

        var custom = new StackPanel { Margin = new(12) };
        Field(custom, "廠牌", _manufacturer); Field(custom, "型號／元件名稱", _model); Field(custom, "分類", _category);
        custom.Children.Add(_contacts);
        var edit = new Button { Content = "Port／Pin 與規格", Padding = new(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left };
        edit.Click += (_, _) => EditContacts(); custom.Children.Add(edit);
        _tabs.Items.Add(new TabItem { Header = "自製元件", Content = new ScrollViewer { Content = custom, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        root.Children.Add(_tabs); Content = root;
        Filter();
    }

    private static void Field(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 4) }); panel.Children.Add(box);
    }

    private void Filter()
    {
        var selected = (_list.SelectedItem as Choice)?.Definition.Identity.ComponentId;
        var query = _search.Text.Trim();
        var choices = _catalog.Select(c => new Choice(c)).Where(c => c.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _list.ItemsSource = choices;
        _list.SelectedItem = choices.FirstOrDefault(c => c.Definition.Identity.ComponentId == selected);
        _details.Text = choices.Length == 0 ? "找不到元件" : "";
    }

    private ComponentIR CustomDefinition() => _custom with { Identity = _custom.Identity with {
        Manufacturer = _manufacturer.Text.Trim(), Model = _model.Text.Trim() },
        Classification = _custom.Classification with { Category = _category.Text.Trim() } };

    private void EditContacts()
    {
        if (string.IsNullOrWhiteSpace(_model.Text)) { _error.Text = "請先填寫元件名稱。"; _model.Focus(); return; }
        var editor = new ComponentManualEditorDialog(CustomDefinition(), projectLocal: true) { Owner = this };
        if (editor.ShowDialog() != true || editor.EditedComponent is not { } edited) return;
        _custom = edited; _category.Text = edited.Classification.Category ?? "";
        _contacts.Text = $"Port {edited.Ports.Count} / Pin {edited.Pins.Count}"; _error.Text = "";
    }

    private void Accept()
    {
        try
        {
            if (!int.TryParse(_quantity.Text, out var quantity) || quantity is < 1 or > 1000)
                throw new InvalidOperationException("新增數量必須為 1 到 1000 的整數。");
            var selected = _tabs.SelectedIndex == 1 ? CustomDefinition() : (_list.SelectedItem as Choice)?.Definition
                ?? throw new InvalidOperationException("請選擇元件。");
            _ = new SchematicAuthoringService().AddBomComponent(new ElectricalProject { ProjectId = "validate" }, selected, 1);
            SelectedComponent = selected; Quantity = quantity; DialogResult = true;
        }
        catch (Exception error) { _error.Text = error.Message; }
    }
}
