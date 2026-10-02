using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Desktop;

public sealed class CableArchiveEditorDialog : Window
{
    private sealed class ContactRow
    {
        public required string PinId { get; init; }
        public required string Label { get; set; }
        public string? Contact { get; set; }
    }
    private sealed class TextRow
    {
        public string AttributeTag { get; set; } = "";
        public CableTextField Field { get; set; }
    }
    private readonly SymbolArchiveRepository _repository;
    private readonly string? _expectedHash;
    private readonly ArchivedCableTemplate _source;
    private readonly bool _updateCandidate;
    private readonly CableCadRoleEditor _wiring = new();
    private readonly CableCadRoleEditor _sketch = new();
    private readonly CableManufacturingTableEditor _table;
    private readonly TextBox _name = new();
    private readonly TextBox _revision = new();
    private readonly TextBox _mappingRevision = new();
    private readonly TextBox _evidence = new() { AcceptsReturn = true, MinHeight = 50, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _confirmed = new() { Content = "已核對所有接法與來源" };
    private readonly ComboBox _construction = new() { ItemsSource = Enum.GetValues<CableConstructionType>() };
    private readonly TextBox _constructionEvidence = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<ContactRow> _contacts = [];
    private readonly ObservableCollection<TextRow> _textBindings;
    private readonly DataGrid _contactGrid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, MinColumnWidth = 180 };
    private readonly DataGrid _textGrid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, MinColumnWidth = 180 };
    private readonly CheckBox _sketchOnly = new() { Content = "製作 CAD 已只保留示意圖，不含舊接法表" };
    private readonly DataGridComboBoxColumn _contactColumn;
    private readonly DataGridComboBoxColumn _tagColumn;
    private bool _saving;
    public CableArchiveEntry? SavedEntry { get; private set; }

    public CableArchiveEditorDialog(SymbolArchiveRepository repository, IReadOnlyList<ComponentIR> catalog,
        CableArchiveEntry? existing = null)
    {
        _repository = repository; _expectedHash = repository.GetContentHash();
        if (existing is not null)
        {
            existing = repository.Load().CableTemplates.SingleOrDefault(e => e.Template.TemplateId == existing.Template.TemplateId &&
                e.Template.TemplateRevision == existing.Template.TemplateRevision) ??
                throw new InvalidOperationException("線材模板已移除，請重新開啟線材庫。");
            if (repository.GetContentHash() != _expectedHash)
                throw new InvalidOperationException("線材庫讀取時已變更，請重新開啟編輯。");
        }
        _updateCandidate = existing?.Status == SymbolRevisionStatus.Candidate;
        _source = existing is null ? new()
        {
            TemplateId = "cable-" + Guid.NewGuid().ToString("N"), TemplateRevision = "draft",
            AssetSha256 = new('0', 64), Ports = Enumerable.Range(1, 2).Select(i => new ComponentIntelligence.Electrical.Domain.ComponentPort
            {
                PortId = "source-port-" + Guid.NewGuid().ToString("N"), Name = "P" + i,
                Pins = [new() { PinId = "source-pin-" + Guid.NewGuid().ToString("N"), PinNumber = "1" }]
            }).ToList()
        } : JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(existing.Template))!;
        _textBindings = new(_source.TextBindings.Select(b => new TextRow { AttributeTag = b.AttributeTag, Field = b.Field }));
        _table = new(new CableManufacturingEditorService().Prepare(_source), CableConnectorCatalog.Choices(catalog),
            boundPins: () => {
                if (!Commit(_contactGrid)) throw new InvalidOperationException("請先完成接點綁定欄位編輯。");
                return _contacts.Where(c => !string.IsNullOrWhiteSpace(c.Contact)).Select(c => c.PinId).ToHashSet(StringComparer.Ordinal);
            });
        _table.PinsRemoved += pins =>
        {
            foreach (var contact in _contacts.Where(c => pins.Contains(c.PinId)).ToArray()) _contacts.Remove(contact);
            _contactGrid.Items.Refresh(); _confirmed.IsChecked = false;
        };
        Title = existing is null ? "新增自製線材模板" : _updateCandidate ? "編輯線材候選草稿" : "線材模板：建立新版本";
        Width = 1000; Height = 800; MinWidth = 750; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _name.Text = _source.DisplayName ?? "";
        _revision.Text = _updateCandidate ? _source.TemplateRevision : DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        _revision.IsReadOnly = _updateCandidate;
        _mappingRevision.Text = _source.MappingRevision ?? "";
        _evidence.Text = _source.MappingEvidence ?? "";
        _construction.SelectedItem = existing?.ConstructionType ?? CableConstructionType.Unknown;
        _constructionEvidence.Text = existing?.ConstructionEvidence ?? "";
        // Confirmation is a new explicit decision, not inherited by a changed candidate.
        _table.Draft.ConfirmMapping = false;
        var root = new DockPanel { Margin = new(16) };
        var heading = new Grid { Margin = new(0, 0, 0, 12) };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = new(220) });
        heading.Children.Add(Field("線材名稱", _name));
        var revisionField = Field(_updateCandidate ? "草稿版本" : "新版本", _revision); Grid.SetColumn(revisionField, 1); heading.Children.Add(revisionField);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(14, 6, 14, 6) };
        var save = new Button { Content = "儲存候選草稿", Padding = new(14, 6, 14, 6), Margin = new(8, 0, 0, 0) };
        save.Click += async (_, _) => await SaveAsync(); footer.Children.Add(cancel); footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "接線 CAD", Content = _wiring });
        tabs.Items.Add(new TabItem { Header = "製作示意 CAD", Content = _sketch });
        tabs.Items.Add(new TabItem { Header = "接頭／接法表", Content = _table });
        _contactGrid.ItemsSource = _contacts;
        _contactGrid.Columns.Add(new DataGridTextColumn { Header = "來源 Pin", Binding = new Binding("Label"), IsReadOnly = true, Width = new(1, DataGridLengthUnitType.Star) });
        _contactColumn = new() { Header = "CAD 接點", SelectedValuePath = "Tag", DisplayMemberPath = "Tag",
            SelectedValueBinding = new Binding("Contact") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 300 };
        _contactGrid.Columns.Add(_contactColumn);
        var contactPanel = new DockPanel();
        var clearContact = IconButton("\uE74D", "清除所選接點綁定", (_, _) => {
            if (!Commit(_contactGrid)) return;
            if (_contactGrid.SelectedItem is ContactRow row) { row.Contact = null; _contactGrid.Items.Refresh(); }
        });
        DockPanel.SetDock(clearContact, Dock.Top); contactPanel.Children.Add(clearContact); contactPanel.Children.Add(_contactGrid);
        tabs.Items.Add(new TabItem { Header = "CAD 接點綁定", Content = contactPanel });
        _textGrid.ItemsSource = _textBindings;
        _tagColumn = new() { Header = "CAD 屬性", SelectedValueBinding = new Binding("AttributeTag"), Width = new(1, DataGridLengthUnitType.Star) };
        _textGrid.Columns.Add(_tagColumn);
        _textGrid.Columns.Add(new DataGridComboBoxColumn { Header = "實例欄位", ItemsSource = Enum.GetValues<CableTextField>(),
            SelectedValueBinding = new Binding("Field"), Width = 240 });
        var textPanel = new DockPanel();
        var textToolbar = new StackPanel { Orientation = Orientation.Horizontal };
        textToolbar.Children.Add(IconButton("\uE710", "新增文字綁定", (_, _) => { if (Commit(_textGrid)) _textBindings.Add(new() { AttributeTag = "" }); }));
        textToolbar.Children.Add(IconButton("\uE74D", "移除所選文字綁定", (_, _) => { if (Commit(_textGrid) && _textGrid.SelectedItem is TextRow row) _textBindings.Remove(row); }));
        DockPanel.SetDock(textToolbar, Dock.Top); textPanel.Children.Add(textToolbar); textPanel.Children.Add(_textGrid);
        tabs.Items.Add(new TabItem { Header = "Reference／長度／規格", Content = textPanel });
        var confirmation = new StackPanel();
        confirmation.Children.Add(Field("接法版本", _mappingRevision));
        confirmation.Children.Add(Field("接法來源與核對依據", _evidence));
        confirmation.Children.Add(_confirmed);
        confirmation.Children.Add(_sketchOnly);
        confirmation.Children.Add(Field("製作方式", _construction));
        confirmation.Children.Add(Field("製作方式來源", _constructionEvidence));
        tabs.Items.Add(new TabItem { Header = "來源確認", Content = confirmation });
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) RefreshBindings(); };
        _wiring.Changed += (_, _) => RefreshBindings(); _sketch.Changed += (_, _) => RefreshBindings();
        root.Children.Add(tabs); Content = root;
        if (existing is not null)
        {
            foreach (var binding in existing.ContactBindings)
                _contacts.Add(new() { PinId = binding.EngineeringEndpointId, Label = binding.EngineeringEndpointId, Contact = binding.ConnectionPointId });
            if (!existing.WiringAssetPending)
                _wiring.Restore(repository.ResolveArchivePath(existing.AssetPath), existing.WiringGeometry, existing.WiringSelection, existing.MillimetresPerUnit);
            if (existing.ManufacturingAsset is { } sketch)
                _sketch.Restore(repository.ResolveArchivePath(sketch.AssetPath), sketch.Geometry, sketch.Selection, sketch.MillimetresPerUnit);
        }
        RefreshBindings();
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await SaveAsync(); } };
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }

    private void RefreshBindings()
    {
        if (!_table.Commit() || !Commit(_contactGrid) || !Commit(_textGrid)) return;
        var pins = _table.Draft.Ends.SelectMany(p => p.Pins.Select(pin => (Id: pin.PinId, Label: $"{p.Name} / {pin.PinNumber} {pin.Function}"))).ToArray();
        foreach (var row in _contacts.Where(r => pins.All(p => p.Id != r.PinId) && r.Contact is null).ToArray()) _contacts.Remove(row);
        foreach (var pin in pins)
        {
            if (_contacts.SingleOrDefault(r => r.PinId == pin.Id) is { } row) row.Label = pin.Label;
            else _contacts.Add(new() { PinId = pin.Id, Label = pin.Label });
        }
        _contactGrid.Items.Refresh();
        _contactColumn.ItemsSource = _wiring.Geometry?.ConnectionPoints;
        _tagColumn.ItemsSource = new[] { _wiring.Geometry, _sketch.Geometry }.Where(a => a is not null)
            .SelectMany(a => a!.Primitives).Select(p => p.AttributeTag).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        try
        {
            if (!_table.Commit() || !Commit(_contactGrid) || !Commit(_textGrid)) return;
            _wiring.ValidateCommitted(); _sketch.ValidateCommitted();
            if (_sketch.Geometry is not null && _sketchOnly.IsChecked != true)
                throw new InvalidOperationException("請確認製作示意 CAD 不含舊接法表；若原圖有表格，請先選取示意圖區域或圖塊。");
            if (string.IsNullOrWhiteSpace(_revision.Text)) throw new InvalidOperationException("請填寫新版本。");
            _table.Draft.MappingRevision = _mappingRevision.Text; _table.Draft.MappingEvidence = _evidence.Text;
            _table.Draft.ConfirmMapping = _confirmed.IsChecked == true;
            var wiring = _wiring.Geometry;
            if (_wiring.SourcePath is not null && wiring is null) throw new InvalidOperationException("請先載入接線 CAD。");
            if (_sketch.SourcePath is not null && _sketch.Geometry is null) throw new InvalidOperationException("請先載入製作示意 CAD。");
            var template = new CableManufacturingEditorService().Apply(_source, _table.Draft) with
            {
                TemplateRevision = _revision.Text.Trim(), DisplayName = _name.Text.Trim(),
                AssetSha256 = wiring?.SourceSha256 ?? new('0', 64), WiringSelectionSha256 = _wiring.Selection?.GeometrySha256,
                TextBindings = _textBindings.Select(b => new CableTextBinding(b.AttributeTag, b.Field)).ToList()
            };
            var contacts = _contacts.Where(c => !string.IsNullOrWhiteSpace(c.Contact)).Select(c => new SymbolPortBinding {
                EngineeringEndpointId = c.PinId, ConnectionPointId = c.Contact! }).ToArray();
            if (contacts.Any(c => wiring?.ConnectionPoints.All(p => p.Tag != c.ConnectionPointId) != false))
                throw new InvalidOperationException("有綁定的 CAD 接點已不存在；請明確清除或重新綁定。");
            var entry = new CableArchiveEntry
            {
                Template = template, AssetPath = wiring is null ? "" : "pending-wiring.dxf", MillimetresPerUnit = wiring?.MillimetresPerUnit ?? 1,
                ConstructionType = (CableConstructionType)_construction.SelectedItem,
                ConstructionEvidence = string.IsNullOrWhiteSpace(_constructionEvidence.Text) ? null : _constructionEvidence.Text.Trim(),
                WiringAssetPending = wiring is null, WiringSelection = _wiring.Selection, ContactBindings = contacts,
                ManufacturingAsset = _sketch.Geometry is { } sketch ? new() { AssetPath = "pending-sketch.dxf",
                    SourceSha256 = sketch.SourceSha256, MillimetresPerUnit = sketch.MillimetresPerUnit, Selection = _sketch.Selection } : null
            };
            _saving = true; IsEnabled = false;
            SavedEntry = await new CableArchiveCreationService(_repository).CreateAsync(new() { Entry = entry,
                WiringSourcePath = _wiring.SourcePath, WiringGeometry = wiring, ManufacturingSourcePath = _sketch.SourcePath,
                ManufacturingGeometry = _sketch.Geometry, ExpectedArchiveSha256 = _expectedHash, UpdateExistingCandidate = _updateCandidate });
            _saving = false; DialogResult = true;
        }
        catch (Exception error) { _error.Text = error.Message; }
        finally { _saving = false; IsEnabled = true; }
    }

    private static bool Commit(DataGrid grid) => grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true);
    private static StackPanel Field(string label, Control control) { var panel = new StackPanel { Margin = new(0, 0, 12, 8) }; panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 4) }); panel.Children.Add(control); return panel; }
    private static Button IconButton(string glyph, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button { Content = glyph, FontFamily = new("Segoe MDL2 Assets"), ToolTip = tooltip, Padding = new(9, 5, 9, 5), Margin = new(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += handler; return button;
    }
}
