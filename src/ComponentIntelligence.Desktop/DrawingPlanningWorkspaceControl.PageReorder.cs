using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl
{
    private const string PageDragFormat = "ComponentIntelligence.DrawingPlanPageId";
    private Point _pageDragStart;
    private string? _pageDragId;

    private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pageDragStart = e.GetPosition(PageList);
        _pageDragId = FindPageFromSource(e.OriginalSource)?.PageId;
    }

    private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrWhiteSpace(_pageDragId)) return;
        var point = e.GetPosition(PageList);
        if (Math.Abs(point.X - _pageDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pageDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var id = _pageDragId;
        _pageDragId = null;
        DragDrop.DoDragDrop(PageList, new DataObject(PageDragFormat, id), DragDropEffects.Move);
    }

    private void PageList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(PageDragFormat) || e.Data.GetData(PageDragFormat) is not string sourcePageId || CurrentPlan is null) return;
        var ordered = CurrentPlan.Pages.OrderBy(page => page.Order).ThenBy(page => page.PageId, StringComparer.Ordinal).ToArray();
        var target = FindPageFromSource(e.OriginalSource);
        var targetIndex = target is null ? ordered.Length - 1 : Array.FindIndex(ordered, page => page.PageId == target.PageId);
        if (targetIndex < 0) return;

        TryEdit(() => _controller.MovePage(sourcePageId, targetIndex));
        e.Handled = true;
    }

    private DrawingPlanPage? FindPageFromSource(object? source)
    {
        if (source is not DependencyObject element) return null;
        var item = ItemsControl.ContainerFromElement(PageList, element) as ListBoxItem;
        return item?.Content as DrawingPlanPage ?? item?.DataContext as DrawingPlanPage;
    }
}
