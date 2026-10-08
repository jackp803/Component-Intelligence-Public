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
        CadScaleText.Text = (SchematicSymbolPresentation.CadDisplayScale(symbol) * 100).ToString("0.###", CultureInfo.CurrentCulture);
        CadSizeText.Text = $"原圖 {geometry.Width:0.###} x {geometry.Height:0.###} mm\n顯示 {symbol.Width:0.###} x {symbol.Height:0.###} mm";
        CadScaleText.IsEnabled = CadScaleApply.IsEnabled = CadScaleReset.IsEnabled = !symbol.Locked;
    }

    private void CadScaleApply_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionId is null) return;
        if (!double.TryParse(CadScaleText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent))
        { Status.Text = "請輸入有效的 CAD 縮放百分比。"; return; }
        Apply(p => _service.ScaleCadSymbol(p, _selectionId, percent / 100), "已調整 CAD 顯示縮放");
    }

    private void CadScaleReset_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionId is not null)
            Apply(p => _service.ScaleCadSymbol(p, _selectionId, 1), "已還原 CAD 顯示為 100%");
    }

    private void PreviewSymbolResize()
    {
        var symbol = _gestureStart!.Schematic!.Symbols.Single(s => s.SymbolId == _dragResizeSymbol);
        var owner = SchematicSymbolOwner.Resolve(_gestureStart, symbol);
        var bounds = SchematicPortPresentation.GenericBodyBounds(_gestureStart.Schematic, symbol, owner);
        var width = _pointer!.X - bounds.X; var height = _pointer.Y - bounds.Y;
        _gesturePreview = symbol.Geometry is not null
            ? _service.ResizeCadSymbol(_gestureStart, symbol.SymbolId, width, height)
            : _service.ResizeGenericSymbol(_gestureStart, symbol.SymbolId, width, height);
    }
}
