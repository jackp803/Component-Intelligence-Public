using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private bool _filteringBom;
    private string? _bomProjectId;
    private SchematicBomPaletteEntry? _pendingBom;
    private readonly Dictionary<string, CatalogItem> _bomItems = new(StringComparer.Ordinal);

    private sealed class CatalogItem(SchematicBomPaletteEntry entry) : INotifyPropertyChanged
    {
        public SchematicBomPaletteEntry Entry { get; set; } = entry;
        public string Label => Entry.Label;
        public string Category => Entry.Category;
        public BitmapSource? PreviewImage { get; private set; }
        public bool PhotoLoading { get; set; }
        public string PhotoStatus { get; private set; } = "無照片";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetPhoto(BitmapSource? image)
        {
            PreviewImage = image; PhotoStatus = image is null ? "無照片" : "";
            PropertyChanged?.Invoke(this, new(nameof(PreviewImage)));
            PropertyChanged?.Invoke(this, new(nameof(PhotoStatus)));
        }
        public void Refresh(SchematicBomPaletteEntry next)
        {
            if (Entry.DefinitionId != next.DefinitionId) SetPhoto(null);
            Entry = next;
            PropertyChanged?.Invoke(this, new(nameof(Entry)));
            PropertyChanged?.Invoke(this, new(nameof(Label)));
            PropertyChanged?.Invoke(this, new(nameof(Category)));
        }
    }

    private void BomCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_filteringBom) FilterCatalog();
    }

    private void FilterCatalog()
    {
        if (CatalogList is null || BomCategory is null || Search is null || _filteringBom) return;
        _filteringBom = true;
        try
        {
            var project = _getProject();
            if (_bomProjectId != project.ProjectId)
            {
                _bomItems.Clear(); _bomProjectId = project.ProjectId;
                _placeMode = false; _placementPreview = null; _pendingBom = null;
                CatalogList.SelectedItem = null;
                BomCategory.SelectedItem = null;
            }
            var entries = SchematicBomPalette.Build(project, _catalog);
            var selected = (CatalogList.SelectedItem as CatalogItem)?.Entry.Key;
            var category = BomCategory.SelectedItem as string ?? "全部分類";
            var categories = new[] { "全部分類" }.Concat(entries.Select(e => e.Category).Distinct()).ToArray();
            BomCategory.ItemsSource = categories;
            if (!categories.Contains(category)) category = "全部分類";
            BomCategory.SelectedItem = category;
            var search = Search.Text.Trim();
            var rows = new List<CatalogItem>();
            foreach (var entry in entries.Where(e => (category == "全部分類" || e.Category == category) &&
                $"{e.Label} {e.Category} {e.Subcategory}".Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                if (!_bomItems.TryGetValue(entry.Key, out var row))
                {
                    row = new(entry); _bomItems[entry.Key] = row;
                    _ = LoadBomPhoto(row);
                }
                else
                {
                    row.Refresh(entry);
                    if (row.PreviewImage is null) _ = LoadBomPhoto(row);
                }
                rows.Add(row);
            }
            var view = new ListCollectionView(rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CatalogItem.Category)));
            CatalogList.ItemsSource = view;
            CatalogList.SelectedItem = rows.FirstOrDefault(r => r.Entry.Key == selected);
            BomEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BomEmpty.Text = entries.Count == 0 ? "尚無專案 BOM" : "找不到符合的 BOM 項目";
            BomContext.Text = project.BomItems.Count == 0 && entries.Count > 0
                ? "既有專案實體數量" : $"{entries.Count} 種項目";
        }
        finally { _filteringBom = false; }
    }

    private async Task LoadBomPhoto(CatalogItem item)
    {
        if (item.Entry.DefinitionId is null || item.PhotoLoading) return;
        var definitionId = item.Entry.DefinitionId;
        item.PhotoLoading = true;
        try
        {
            var image = await GetImage(definitionId);
            if (item.Entry.DefinitionId == definitionId) item.SetPhoto(image);
        }
        catch { if (item.Entry.DefinitionId == definitionId) item.SetPhoto(null); }
        finally
        {
            item.PhotoLoading = false;
            if (item.Entry.DefinitionId != definitionId) _ = LoadBomPhoto(item);
        }
    }
}
