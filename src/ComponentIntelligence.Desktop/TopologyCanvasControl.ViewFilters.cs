using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly CheckBox _showWiresCheck = ViewCheck("Show Wires｜顯示線路", true);
    private readonly CheckBox _allLayersCheck = ViewCheck("All｜全部", true);
    private readonly CheckBox _powerLayerCheck = ViewCheck("Power｜電源", true);
    private readonly CheckBox _analogLayerCheck = ViewCheck("Analog｜類比", true);
    private readonly CheckBox _digitalLayerCheck = ViewCheck("Digital｜數位", true);
    private readonly CheckBox _communicationLayerCheck = ViewCheck("Communication｜通訊", true);
    private readonly CheckBox _groundLayerCheck = ViewCheck("Ground / Shield｜接地／屏蔽", true);

    private bool _viewFiltersConfigured;
    private bool _syncingLayerChecks;
    private bool _applyingViewFilter;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += (_, _) => ConfigureViewFilters();
    }

    private void ConfigureViewFilters()
    {
        if (_viewFiltersConfigured || WireModeButton.Parent is not Panel toolbar) return;
        _viewFiltersConfigured = true;

        // Layer checkboxes are a view only. Keep the projection unfiltered so they never create
        // or mutate a second topology dataset.
        SetLayerFilter(null);

        toolbar.Children.Add(new Border
        {
            Width = 1,
            Height = 24,
            Background = System.Windows.Media.Brushes.LightGray,
            Margin = new Thickness(8, 0, 8, 0)
        });
        toolbar.Children.Add(new TextBlock
        {
            Text = "View｜顯示",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        });

        foreach (var check in LayerChecks(includeShowWires: true))
        {
            check.Checked += ViewCheck_Changed;
            check.Unchecked += ViewCheck_Changed;
            toolbar.Children.Add(check);
        }

        Surface.LayoutUpdated += (_, _) => ApplyWireLayerVisibility();
        ApplyWireLayerVisibility();
    }

    private void ViewCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingLayerChecks)
        {
            ApplyWireLayerVisibility();
            return;
        }

        _syncingLayerChecks = true;
        try
        {
            if (ReferenceEquals(sender, _allLayersCheck))
            {
                var enabled = _allLayersCheck.IsChecked == true;
                foreach (var check in LayerChecks(includeShowWires: false).Where(check => !ReferenceEquals(check, _allLayersCheck)))
                    check.IsChecked = enabled;
            }
            else if (!ReferenceEquals(sender, _showWiresCheck))
            {
                _allLayersCheck.IsChecked = SpecificLayerChecks().All(check => check.IsChecked == true);
            }
        }
        finally
        {
            _syncingLayerChecks = false;
        }

        ApplyWireLayerVisibility();
    }

    private void ApplyWireLayerVisibility()
    {
        if (_applyingViewFilter || _project is null || Surface is null) return;
        _applyingViewFilter = true;
        try
        {
            var showWires = _showWiresCheck.IsChecked == true;
            var showAllLayers = _allLayersCheck.IsChecked == true;
            var selectedLayers = SelectedLayers();
            var edgeLayers = _projection.Build(_project, null).Edges
                .ToDictionary(edge => edge.ConnectionId, edge => edge.Layer, StringComparer.OrdinalIgnoreCase);

            Visibility? pendingNetLabelVisibility = null;
            foreach (UIElement child in Surface.Children)
            {
                if (child is Line line && line.Tag is string connectionId)
                {
                    var visible = showWires && edgeLayers.TryGetValue(connectionId, out var layer) &&
                        (showAllLayers || selectedLayers.Contains(layer));
                    var desired = visible ? Visibility.Visible : Visibility.Collapsed;
                    if (line.Visibility != desired) line.Visibility = desired;
                    pendingNetLabelVisibility = desired;
                    continue;
                }

                // Net labels are emitted directly after their edge line. Port labels use a smaller font.
                if (pendingNetLabelVisibility is Visibility labelVisibility &&
                    child is TextBlock text && !text.IsHitTestVisible && Math.Abs(text.FontSize - 11d) < 0.01)
                {
                    if (text.Visibility != labelVisibility) text.Visibility = labelVisibility;
                    pendingNetLabelVisibility = null;
                    continue;
                }

                if (child is not TextBlock)
                    pendingNetLabelVisibility = null;
            }
        }
        finally
        {
            _applyingViewFilter = false;
        }
    }

    private HashSet<ElectricalLayer> SelectedLayers()
    {
        var selected = new HashSet<ElectricalLayer>();
        if (_powerLayerCheck.IsChecked == true) selected.Add(ElectricalLayer.Power);
        if (_analogLayerCheck.IsChecked == true) selected.Add(ElectricalLayer.Analog);
        if (_digitalLayerCheck.IsChecked == true) selected.Add(ElectricalLayer.Digital);
        if (_communicationLayerCheck.IsChecked == true) selected.Add(ElectricalLayer.Communication);
        if (_groundLayerCheck.IsChecked == true) selected.Add(ElectricalLayer.Grounding);
        return selected;
    }

    private IEnumerable<CheckBox> LayerChecks(bool includeShowWires)
    {
        if (includeShowWires) yield return _showWiresCheck;
        yield return _allLayersCheck;
        foreach (var check in SpecificLayerChecks()) yield return check;
    }

    private IEnumerable<CheckBox> SpecificLayerChecks()
    {
        yield return _powerLayerCheck;
        yield return _analogLayerCheck;
        yield return _digitalLayerCheck;
        yield return _communicationLayerCheck;
        yield return _groundLayerCheck;
    }

    private static CheckBox ViewCheck(string content, bool isChecked) => new()
    {
        Content = content,
        IsChecked = isChecked,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0)
    };
}
