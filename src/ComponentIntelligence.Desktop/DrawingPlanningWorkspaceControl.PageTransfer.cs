using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl
{
    private void SelectGroup_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlan is null || _controller.SelectedRepresentationIds.Count == 0) return;
        var groups = CurrentPlan.Placements.Where(p => _controller.SelectedRepresentationIds.Contains(p.RepresentationId))
            .Select(p => p.GroupId).ToHashSet(StringComparer.Ordinal);
        _selectedRouteId = null;
        _controller.SelectRepresentations(CurrentPlan.Placements.Where(p => p.PageId == _controller.SelectedPageId && groups.Contains(p.GroupId)).Select(p => p.RepresentationId));
        RefreshSelection(); RefreshCanvas();
    }

    private async void TransferPage_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPlan is null || PlanningInputProvider is null || _controller.SelectedRepresentationIds.Count == 0)
        { StatusText.Text = "請先選取要搬頁的元件或群組。"; return; }
        var dialog = new Window { Owner=Window.GetWindow(this), Title="搬移選取物件（不是切換查看頁或重排頁序）",
            Width=540, Height=230, WindowStartupLocation=WindowStartupLocation.CenterOwner, ResizeMode=ResizeMode.NoResize };
        var panel = new StackPanel { Margin=new Thickness(16) };
        var choices = CurrentPlan.Pages.OrderBy(p => p.Order).Select(p => new PageDestination(p.PageId, $"{p.Order+1}: {p.PageId} / {p.Archetype}")).ToArray();
        var target = new ComboBox { ItemsSource=choices, DisplayMemberPath=nameof(PageDestination.Label), Margin=new Thickness(0,8,0,12) };
        panel.Children.Add(new TextBlock { Text="目標頁面" }); panel.Children.Add(target);
        var buttons = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        var cancel = new Button { Content="取消", IsCancel=true, Padding=new Thickness(16,6,16,6), Margin=new Thickness(6) };
        var apply = new Button { Content="搬移", Padding=new Thickness(16,6,16,6), Margin=new Thickness(6) };
        apply.Click += (_, _) => { if (target.SelectedItem is PageDestination) dialog.DialogResult=true; };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); panel.Children.Add(buttons); dialog.Content=panel;
        if (dialog.ShowDialog() != true || target.SelectedItem is not PageDestination destination) return;
        IsEnabled=false;
        try
        {
            var settings = RuntimeSettingsStore.Load();
            var validation = DrawingRuntimeSettingsValidator.Validate(settings);
            if (settings is null || !validation.IsValid) throw new InvalidOperationException("請先確認 Drawing Planning runtime 設定。");
            var client = PlannerClient ?? new PythonDrawingPlannerClient(settings);
            var input = PlanningInputProvider();
            await _controller.TransferSelectedAsync(destination.PageId, proposal => client.GenerateAsync(input, proposal, CancellationToken.None));
            _selectedRouteId=null;
            PersistPlan(); Refresh();
            StatusText.Text="已搬頁並重建同頁／跨頁路線。一次 Undo 可回復整筆操作；尚未儲存專案。";
        }
        catch (Exception ex) { StatusText.Text="搬頁未套用：" + ex.Message; }
        finally { IsEnabled=true; }
    }

    private sealed record PageDestination(string PageId, string Label);
}
