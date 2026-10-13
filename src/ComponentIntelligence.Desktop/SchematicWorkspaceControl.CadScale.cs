using System.Globalization;
using System.Windows;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void UpdateCadScaleSelection(SchematicSymbol? symbol)
    {
        CadScalePanel.Visibility = symbol?.Geometry is not null ? Visibility.Visible : Visibility.Collapsed;
        if (symbol?.Geometry is not { } geometry) return;
        CadScaleText.Text = (SchematicSymbolPresentation.CadDisplayScale(symbol) * 100).ToString("R", CultureInfo.CurrentCulture);
        CadSizeText.Text = $"原圖 {geometry.Width:0.###} x {geometry.Height:0.###} mm\n顯示 {symbol.Width:0.###} x {symbol.Height:0.###} mm";
        CadScaleText.IsEnabled = CadScaleApply.IsEnabled = CadScaleReset.IsEnabled = !symbol.Locked;
    }

    private void CadScaleApply_Click(object sender, RoutedEventArgs e)
    {
        ApplyInspector();
    }

    private void CadScaleReset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionId is not null) { CadScaleText.Text = "100"; ApplyInspector(); }
    }

    private void PreviewSymbolResize()
    {
        var symbol = _gestureStart!.Schematic!.Symbols.Single(s => s.SymbolId == _dragResizeSymbol);
        var owner = SchematicSymbolOwner.Resolve(_gestureStart, symbol);
        if (symbol.Geometry is not null)
        {
            var visible = _gestureCadBounds ??= CadContentBounds(_gestureStart.Schematic, symbol, owner);
            var right = visible.Right + _pointer!.X - _dragStart!.X;
            var bottom = visible.Bottom + _pointer.Y - _dragStart.Y;
            var scale = SchematicSymbolPresentation.CadDisplayScale(symbol);
            // Fit the actual corner: stroke widths and marker padding do not scale with CAD geometry.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                Rect At(double value) => CadContentBounds(_gestureStart.Schematic, symbol with {
                    Width = symbol.Geometry.Width * value, Height = symbol.Geometry.Height * value }, owner);
                var frame = At(scale);
                if (Math.Abs(frame.Right - right) < 1e-7 || Math.Abs(frame.Bottom - bottom) < 1e-7)
                    if (frame.Right <= right + 1e-7 && frame.Bottom <= bottom + 1e-7) break;
                var delta = Math.Max(.0001, scale * .01);
                var probe = At(scale + delta);
                double Limit(double current, double target, double next)
                {
                    var slope = (next - current) / delta;
                    return slope > 1e-9 ? scale + (target - current) / slope : target < current ? .05 : double.PositiveInfinity;
                }
                var fit = Math.Min(Limit(frame.Right, right, probe.Right), Limit(frame.Bottom, bottom, probe.Bottom));
                if (!double.IsFinite(fit)) break;
                var next = Math.Clamp(fit, .05, 20);
                if (Math.Abs(next - scale) < 1e-10) break;
                scale = next;
            }
            _gesturePreview = _service.ScaleCadSymbol(_gestureStart, symbol.SymbolId, scale);
            return;
        }
        var bounds = SchematicPortPresentation.GenericBodyBounds(_gestureStart.Schematic, symbol, owner);
        var width = _pointer!.X - bounds.X; var height = _pointer.Y - bounds.Y;
        _gesturePreview = _service.ResizeGenericSymbol(_gestureStart, symbol.SymbolId, width, height);
    }
}
