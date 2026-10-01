using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private SchematicCableDetailBinding? SelectedCableDetail()
    {
        var page = _getProject().Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId);
        return page?.InlineCableDetails.Concat(page.CableDetail is null ? [] : new[] { page.CableDetail })
            .SingleOrDefault(b => _selectionId == "detail:" + b.DetailId);
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
        var sx = Field("示意圖 X (mm)", basis.SketchPosition.X); var sy = Field("示意圖 Y (mm)", basis.SketchPosition.Y);
        var sw = Field("示意圖最大寬 (mm)", basis.SketchWidth); var sh = Field("示意圖最大高 (mm)", basis.SketchHeight);
        root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        apply.Click += (_, _) =>
        {
            try
            {
                var values = new[] { tx, ty, tw, sx, sy, sw, sh }.Select(f => double.Parse(f.Text, CultureInfo.CurrentCulture)).ToArray();
                var layout = basis with { TablePosition = new(values[0], values[1]), TableWidth = values[2],
                    SketchPosition = new(values[3], values[4]), SketchWidth = values[5], SketchHeight = values[6] };
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

    private void RenderDetailHandles(SchematicPage page, SchematicCableDetailBinding binding)
    {
        foreach (var (position, label) in new[] { (binding.TablePosition, "接法表"), (binding.SketchPosition, "製作示意圖") })
        {
            var handle = new Button { Content = "\uE70F", FontFamily = new("Segoe MDL2 Assets"), ToolTip = label + "位置／尺寸",
                Width = 22, Height = 22, Padding = new(1), Background = Brushes.White, Cursor = Cursors.Hand };
            Canvas.SetLeft(handle, position.X * PixelsPerMm - 24); Canvas.SetTop(handle, position.Y * PixelsPerMm);
            handle.Click += (_, _) => { _selectionId = "detail:" + binding.DetailId; EditArchivedDetailLayout(binding.CableInstanceId, binding); };
            var menu = new ContextMenu();
            var delete = new MenuItem { Header = "移除製作明細" };
            delete.Click += (_, _) => { _selectionId = "detail:" + binding.DetailId; DeleteSelectedObject(); };
            menu.Items.Add(delete); handle.ContextMenu = menu; Sheet.Children.Add(handle);
        }
    }
}
