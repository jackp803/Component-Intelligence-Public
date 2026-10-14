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
    private bool _finishingPointerPreview;

    private void QueuePointerPreview()
    {
        if (_pendingPointerPreview is not null || _pointer == _lastPreviewPointer) return;
        // Run below Input priority so queued pointer events update one pending preview.
        _pendingPointerPreview = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _pendingPointerPreview = null;
            _lastPreviewPointer = _pointer;
            ProcessPointerPreview();
        }));
    }

    private void FlushPointerPreview()
    {
        if (_pendingPointerPreview is null && _dragVisualTransforms.Count == 0 &&
            !(_gestureStart is not null && _dragStart is not null && _pointer != _dragStart)) return;
        _pendingPointerPreview?.Abort(); _pendingPointerPreview = null;
        _lastPreviewPointer = _pointer;
        _finishingPointerPreview = true;
        try { ProcessPointerPreview(); } finally { _finishingPointerPreview = false; }
    }

    private void ClearPointerPreview()
    {
        _pendingPointerPreview?.Abort(); _pendingPointerPreview = null; _lastPreviewPointer = null;
    }

    private void Sheet_LostMouseCapture(object sender, RoutedEventArgs e)
    {
        if (_marqueeStart is not null && !Sheet.IsMouseCaptured) { CancelWireMarquee(); Render(_getProject()); return; }
        if (_gestureStart is null || !ReferenceEquals(sender, Sheet) || Sheet.IsMouseCaptured) return;
        CancelGesture(); Render(_getProject());
    }
}
