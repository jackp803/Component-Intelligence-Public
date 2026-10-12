using System.Windows;
using System.Windows.Threading;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private DispatcherOperation? _pendingPointerPreview;
    private SchematicPoint? _lastPreviewPointer;
    private SchematicGridBounds? _gestureDetailBounds;
    private Rect? _gestureCadBounds;

    private void QueuePointerPreview()
    {
        if (_pendingPointerPreview is not null || _pointer == _lastPreviewPointer) return;
        // Keep only the latest snapped pointer until the dispatcher is ready to render.
        _pendingPointerPreview = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _pendingPointerPreview = null;
            _lastPreviewPointer = _pointer;
            ProcessPointerPreview();
        }));
    }

    private void FlushPointerPreview()
    {
        if (_pendingPointerPreview is null) return;
        _pendingPointerPreview.Abort(); _pendingPointerPreview = null;
        _lastPreviewPointer = _pointer;
        ProcessPointerPreview();
    }

    private void ClearPointerPreview()
    {
        _pendingPointerPreview?.Abort(); _pendingPointerPreview = null; _lastPreviewPointer = null;
    }

    private void Sheet_LostMouseCapture(object sender, RoutedEventArgs e)
    {
        if (_gestureStart is null || !ReferenceEquals(sender, Sheet) || Sheet.IsMouseCaptured) return;
        CancelGesture(); Render(_getProject());
    }
}
