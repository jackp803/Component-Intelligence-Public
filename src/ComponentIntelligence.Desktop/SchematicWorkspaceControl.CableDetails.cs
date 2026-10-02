using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private string? _dragDetailId;
    private SchematicCableDetailPart _dragDetailPart;
    private bool _dragDetailResize;
    private SchematicCableDetailPart? SelectedDetailPart() =>
        _selectionId?.StartsWith("detail:Table:", StringComparison.Ordinal) == true ? SchematicCableDetailPart.Table :
        _selectionId?.StartsWith("detail:Sketch:", StringComparison.Ordinal) == true ? SchematicCableDetailPart.Sketch : null;
    private static string DetailSelectionId(string id, SchematicCableDetailPart part) => "detail:" + part + ":" + id;

    private SchematicCableDetailBinding? SelectedCableDetail()
    {
        var page = _getProject().Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId);
        return page?.InlineCableDetails.Concat(page.CableDetail is null ? [] : new[] { page.CableDetail })
            .SingleOrDefault(b => _selectionId == "detail:" + b.DetailId ||
                _selectionId == DetailSelectionId(b.DetailId, SchematicCableDetailPart.Table) ||
                _selectionId == DetailSelectionId(b.DetailId, SchematicCableDetailPart.Sketch));
    }

    private void EditArchivedDetailLayout(string cableId, SchematicCableDetailBinding? existing)
    {
        if (_pageId is null) return;
        var pageId = _pageId;
        var page = _getProject().Schematic!.Pages.Single(p => p.PageId == pageId);
        existing ??= page.CableDetail?.CableInstanceId == cableId ? page.CableDetail : null;
        var basis = existing ?? new SchematicCableDetailBinding(cableId, null);
        var dialog = new Window { Title = "線材製作明細", Width = 480, Height = 580, MinWidth = 400, MinHeight = 510,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(16) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(12, 5, 12, 5) };
        var apply = new Button { Content = "套用", IsDefault = true, Padding = new(12, 5, 12, 5), Margin = new(8, 0, 0, 0) };
        footer.Children.Add(cancel); footer.Children.Add(apply); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new(0, 8, 0, 8) };
        DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        var fields = new StackPanel();
        var mode = new ComboBox { ItemsSource = new[] { "新增明細頁", "放在目前頁面" }, SelectedIndex = 0, IsEnabled = existing is null };
        fields.Children.Add(mode);
        TextBox Field(string label, double value)
        {
            fields.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 3) });
            var control = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture) }; fields.Children.Add(control); return control;
        }
        var tx = Field("表格 X (mm)", basis.TablePosition.X); var ty = Field("表格 Y (mm)", basis.TablePosition.Y);
        var tw = Field("表格寬 (mm)", basis.TableWidth);
        var scale = Field("表格文字倍率", basis.TableScale);
        var sx = Field("示意圖 X (mm)", basis.SketchPosition.X); var sy = Field("示意圖 Y (mm)", basis.SketchPosition.Y);
        var sw = Field("示意圖最大寬 (mm)", basis.SketchWidth); var sh = Field("示意圖最大高 (mm)", basis.SketchHeight);
        var tableVisible = new CheckBox { Content = "接法表", IsChecked = basis.TableVisible, Margin = new(0, 8, 0, 0) };
        var sketchVisible = new CheckBox { Content = "製作示意圖", IsChecked = basis.SketchVisible, Margin = new(0, 8, 0, 0) };
        fields.Children.Add(tableVisible); fields.Children.Add(sketchVisible);
        root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        apply.Click += (_, _) =>
        {
            try
            {
                var values = new[] { tx, ty, tw, sx, sy, sw, sh, scale }.Select(f => double.Parse(f.Text, CultureInfo.CurrentCulture)).ToArray();
                var layout = basis with { TablePosition = new(values[0], values[1]), TableWidth = values[2],
                    SketchPosition = new(values[3], values[4]), SketchWidth = values[5], SketchHeight = values[6], TableScale = values[7],
                    TableVisible = tableVisible.IsChecked == true, SketchVisible = sketchVisible.IsChecked == true };
                if (!layout.TableVisible && !layout.SketchVisible) throw new InvalidOperationException("至少選取接法表或製作示意圖。");
                var service = new SchematicCableDetailService(); var current = _getProject();
                var targetPage = pageId;
                ComponentIntelligence.Electrical.Domain.ElectricalProject next;
                if (existing is not null) next = service.SetLayout(current, pageId, existing.DetailId, layout);
                else if (mode.SelectedIndex == 1)
                {
                    next = service.PlaceArchivedDetail(current, cableId, pageId, layout.TablePosition, layout.SketchPosition, layout);
                }
                else
                {
                    next = service.AddArchivedDetail(current, cableId, pageId, layout);
                    var target = next.Schematic!.Pages.Single(p => p.CableDetail?.CableInstanceId == cableId);
                    targetPage = target.PageId;
                    next = service.SetLayout(next, targetPage, target.CableDetail!.DetailId, layout with { DetailId = target.CableDetail.DetailId });
                }
                if (Apply(_ => next, "已更新製作明細；不增加線材數量"))
                { _pageId = targetPage; _selectionId = null; RefreshWorkspace(); dialog.DialogResult = true; }
            }
            catch (Exception exception) { error.Text = exception.Message; }
        };
        dialog.Content = root; dialog.ShowDialog();
    }

    private void RenderDetailHandles(SchematicPage page, SchematicCableDetailBinding binding, SchematicCableDetailPresentation? presentation)
    {
        foreach (var (part, position, label, visible) in new[] {
            (SchematicCableDetailPart.Table, binding.TablePosition, "接法表", binding.TableVisible),
            (SchematicCableDetailPart.Sketch, binding.SketchPosition, "製作示意圖", binding.SketchVisible) })
        {
            if (!visible) continue;
            var selected = _selectionId == DetailSelectionId(binding.DetailId, part);
            var menu = new ContextMenu();
            if (part == SchematicCableDetailPart.Table)
            {
                var content = new MenuItem { Header = "編輯接法／線材資料" };
                content.Click += (_, _) => TryEditSelectedArchivedCable(binding.CableInstanceId); menu.Items.Add(content);
            }
            var layoutItem = new MenuItem { Header = "位置／尺寸" };
            layoutItem.Click += (_, _) => EditArchivedDetailLayout(binding.CableInstanceId, binding); menu.Items.Add(layoutItem);
            var delete = new MenuItem { Header = "刪除" + label };
            delete.Click += (_, _) => { _selectionId = DetailSelectionId(binding.DetailId, part); DeleteSelectedObject(); };
            menu.Items.Add(delete);
            if (presentation?.PartBounds.TryGetValue(part, out var bounds) == true)
            {
                var surface = new Rectangle { Width = bounds.Width * PixelsPerMm, Height = bounds.Height * PixelsPerMm,
                    Fill = Brushes.Transparent, Stroke = selected ? Brushes.DarkCyan : Brushes.Transparent, StrokeThickness = 1,
                    Tag = part, Cursor = Cursors.SizeAll, ContextMenu = menu, IsHitTestVisible = !_wireMode && !_placeMode && !_pairMode };
                Canvas.SetLeft(surface, bounds.X * PixelsPerMm); Canvas.SetTop(surface, bounds.Y * PixelsPerMm);
                surface.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true; _selectionId = DetailSelectionId(binding.DetailId, part);
                    if (e.ClickCount == 2)
                    {
                        if (part == SchematicCableDetailPart.Table) TryEditSelectedArchivedCable(binding.CableInstanceId);
                        else EditArchivedDetailLayout(binding.CableInstanceId, binding);
                        return;
                    }
                    BeginGesture(e); _dragDetailId = binding.DetailId; _dragDetailPart = part; _dragDetailResize = false;
                    Render(_getProject());
                };
                surface.MouseRightButtonDown += (_, _) => { _selectionId = DetailSelectionId(binding.DetailId, part); FocusCanvas(); };
                Sheet.Children.Add(surface);
                if (selected)
                {
                    var resize = new Rectangle { Width = 12, Height = 12, Fill = Brushes.White, Stroke = Brushes.DarkCyan,
                        Cursor = Cursors.SizeNWSE, ToolTip = label + "尺寸", Tag = "detail-resize" };
                    Canvas.SetLeft(resize, (bounds.X + bounds.Width) * PixelsPerMm - 6);
                    Canvas.SetTop(resize, (bounds.Y + bounds.Height) * PixelsPerMm - 6);
                    resize.MouseLeftButtonDown += (_, e) =>
                    { e.Handled = true; BeginGesture(e); _dragDetailId = binding.DetailId; _dragDetailPart = part; _dragDetailResize = true; };
                    Sheet.Children.Add(resize);
                }
            }
            var handle = new Button { Content = "\uE70F", FontFamily = new("Segoe MDL2 Assets"), ToolTip = label + "位置／尺寸",
                Width = 22, Height = 22, Padding = new(1), Background = Brushes.White, Cursor = Cursors.Hand };
            Canvas.SetLeft(handle, position.X * PixelsPerMm - 24); Canvas.SetTop(handle, position.Y * PixelsPerMm);
            handle.Click += (_, _) => { _selectionId = DetailSelectionId(binding.DetailId, part); EditArchivedDetailLayout(binding.CableInstanceId, binding); };
            handle.ContextMenu = menu; Sheet.Children.Add(handle);
        }
    }

    private void PreviewDetailGesture()
    {
        var project = _gestureStart ?? throw new InvalidOperationException("拖拉起點不存在。");
        var pointer = _pointer ?? throw new InvalidOperationException("拖拉位置不存在。");
        var dragStart = _dragStart ?? throw new InvalidOperationException("拖拉起點不存在。");
        var detailId = _dragDetailId ?? throw new InvalidOperationException("請選取線材明細。");
        var page = project.Schematic!.Pages.Single(p => p.PageId == _pageId);
        var binding = page.CableDetail is { } standalone && standalone.DetailId == detailId ? standalone :
            page.InlineCableDetails.Single(b => b.DetailId == detailId);
        var origin = _dragDetailPart == SchematicCableDetailPart.Table ? binding.TablePosition : binding.SketchPosition;
        SchematicCableDetailBinding layout;
        if (!_dragDetailResize)
        {
            var position = new SchematicPoint(origin.X + pointer.X - dragStart.X, origin.Y + pointer.Y - dragStart.Y);
            layout = _dragDetailPart == SchematicCableDetailPart.Table ? binding with { TablePosition = position } : binding with { SketchPosition = position };
        }
        else if (_dragDetailPart == SchematicCableDetailPart.Table)
        {
            var bounds = new SchematicCableDetailService().BuildArchived(project, page, binding).PartBounds[_dragDetailPart];
            var ratio = Math.Max((pointer.X - origin.X) / bounds.Width, (pointer.Y - origin.Y) / bounds.Height);
            layout = binding with { TableWidth = binding.TableWidth * ratio, TableScale = binding.TableScale * ratio };
        }
        else layout = binding with { SketchWidth = pointer.X - origin.X, SketchHeight = pointer.Y - origin.Y };
        _gesturePreview = new SchematicCableDetailService().SetLayout(project, page.PageId, binding.DetailId, layout);
    }
}
