using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private sealed record PendingRepresentation(string InstanceId, string ComponentId,
        DrawingAssetResolution Asset, SchematicCadAsset Geometry, Cp3aDrawingAssetResolver Resolver);
    private PendingRepresentation? _pendingRepresentation;
    private sealed record RepresentationChoice(string Id, string Label);

    private async void AnotherRepresentation_Click(object sender, RoutedEventArgs e)
    {
        var project = _getProject();
        var selected = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == _selectionId);
        if (selected is null || _pageId is null) { Status.Text = "請先選取既有元件。"; return; }
        if (string.IsNullOrWhiteSpace(_archiveRoot)) { Status.Text = "尚未設定圖塊歸檔來源。"; return; }
        if (!FinishPendingDraft()) return;
        try
        {
            var component = project.Components.Single(c => c.ComponentInstanceId == selected.ComponentInstanceId);
            var repository = new SymbolArchiveRepository(_archiveRoot);
            var choices = repository.Load().Bindings
                .Where(b => b.ComponentId == component.ComponentDefinitionId && b.Role == SymbolRole.Schematic)
                .SelectMany(b => b.Revisions.Where(r => r.Status == SymbolRevisionStatus.Approved)
                    .Select(r => new RepresentationChoice(b.RepresentationId,
                        $"{(b.RepresentationId == "default" ? "預設表示" : b.RepresentationId)} / {r.Revision} / {r.PortBindings.Count} 個接點")))
                .ToArray();
            if (choices.Length == 0) { Status.Text = "此元件尚無已核准的接線表示，請先完成圖塊歸檔。"; return; }
            var dialog = new Window { Title = "放置既有元件的另一個表示", Width = 480, Height = 320,
                Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new(16) };
            panel.Children.Add(new TextBlock { Text = $"{component.ReferenceDesignator ?? "未指定 Reference"}\n{component.ComponentDefinitionId}", TextWrapping = TextWrapping.Wrap });
            var list = new ListBox { ItemsSource = choices, DisplayMemberPath = "Label", Height = 140, Margin = new(0, 12, 0, 12) };
            panel.Children.Add(list);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(12, 5, 12, 5) };
            var place = new Button { Content = "選取表示", IsEnabled = false, Padding = new(12, 5, 12, 5), Margin = new(8, 0, 0, 0) };
            list.SelectionChanged += (_, _) => place.IsEnabled = list.SelectedItem is RepresentationChoice;
            place.Click += (_, _) => dialog.DialogResult = true;
            row.Children.Add(cancel); row.Children.Add(place); panel.Children.Add(row); dialog.Content = panel;
            if (dialog.ShowDialog() != true || list.SelectedItem is not RepresentationChoice choice) return;
            var resolver = new Cp3aDrawingAssetResolver(new SymbolResolver(repository, _catalog), repository);
            var asset = resolver.ResolveRepresentation(component.ComponentDefinitionId!, DrawingRepresentationRole.Schematic, choice.Id)
                ?? throw new InvalidOperationException("核准表示已不存在，請重新選取。");
            var loaded = await PickCadAsset(asset.AssetPath); if (loaded is null) return;
            CancelCommand();
            _placementPreview = _service.PlaceApprovedRepresentation(_getProject(), component.ComponentInstanceId,
                _pageId!, new(20, 20), asset, loaded.Value.Asset);
            _pendingRepresentation = new(component.ComponentInstanceId, component.ComponentDefinitionId!, asset, loaded.Value.Asset, resolver);
            _placeMode = true; Status.Text = "既有元件表示待放置；實體數量不變。"; Focus(); Render(_getProject());
        }
        catch (Exception error) { Status.Text = "無法放置表示：" + error.Message; }
    }

    private void PlacePendingRepresentation(SchematicPoint point)
    {
        var pending = _pendingRepresentation!;
        try
        {
            var current = pending.Resolver.ResolveRepresentation(pending.ComponentId, DrawingRepresentationRole.Schematic, pending.Asset.ArchiveRepresentationId);
            if (current is null || current.Revision != pending.Asset.Revision || current.AssetPath != pending.Asset.AssetPath ||
                current.AssetHashSha256 != pending.Asset.AssetHashSha256 || !current.PortBindings.SequenceEqual(pending.Asset.PortBindings))
                throw new InvalidOperationException("核准表示已變更，請重新選取。");
            if (!Apply(p => _service.PlaceApprovedRepresentation(p, pending.InstanceId, _pageId!, point, current, pending.Geometry),
                    "已放置另一個表示；元件數量與接線不變。")) return;
            _pendingRepresentation = null; _placeMode = false; _placementPreview = null; Render(_getProject());
        }
        catch (Exception error) { Status.Text = "未放置表示：" + error.Message; }
    }
}
