using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private Func<string, Task<Uri?>>? _componentImageResolver;
    private Func<string, Task<Uri?>>? _componentProductPageResolver;
    private readonly TopologyImageCache _topologyImageCache = new();
    private readonly HashSet<string> _expandedVisualPortIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _componentVisualHooked;
    private bool _decoratingVisuals;
    private bool _notionSettingsButtonAdded;

    public Func<string, Task<Uri?>>? ComponentImageResolver
    {
        get => _componentImageResolver;
        set
        {
            _componentImageResolver = value;
            ConfigureComponentVisualHooks();
        }
    }

    public Func<string, Task<Uri?>>? ComponentProductPageResolver
    {
        get => _componentProductPageResolver;
        set
        {
            _componentProductPageResolver = value;
            ConfigureComponentVisualHooks();
        }
    }

    private void ConfigureComponentVisualHooks()
    {
        ConfigureNotionSettingsButton();
        if (_componentVisualHooked || Surface is null) return;
        _componentVisualHooked = true;
        Surface.PreviewMouseLeftButtonDown += Surface_PreviewPinExpansion;
    }

    private void ConfigureNotionSettingsButton()
    {
        if (_notionSettingsButtonAdded || WireModeButton.Parent is not Panel toolbar) return;
        _notionSettingsButtonAdded = true;
        var button = new Button
        {
            Content = "Notion 中央庫",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "設定／測試 Component Intelligence 的 Notion 中央電料知識庫連線"
        };
        button.Click += (_, _) =>
        {
            var dialog = new NotionConnectionDialog { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        };
        toolbar.Children.Add(button);
    }

    private void Surface_PreviewPinExpansion(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || _interactionMode != InteractionMode.Select || e.ClickCount < 2) return;
        var border = FindAncestor<Border>(e.OriginalSource as DependencyObject);
        if (border?.Tag is not string portId || Math.Abs(border.Width - 14d) > 0.01 || Math.Abs(border.Height - 14d) > 0.01) return;
        var port = _project.Components.SelectMany(component => component.Ports)
            .FirstOrDefault(candidate => string.Equals(candidate.PortId, portId, StringComparison.OrdinalIgnoreCase));
        if (port is null) return;

        // Individually wired terminals/flying leads are permanently pin-level. Only a whole-mated
        // connector (M12/RJ45/etc.) can be collapsed/expanded for inspection or special pin wiring.
        if (TopologyEndpointPolicy.DetermineDisplayMode(port) != TopologyEndpointDisplayMode.Connector)
        {
            SelectionText.Text = "此接口為 Pin-level（腳位層）";
            HintText.Text = "散線／端子會固定顯示每一個可接線 Pin，不提供收合，避免遺失實際接線端點。";
            e.Handled = true;
            return;
        }

        if (!_expandedVisualPortIds.Add(portId)) _expandedVisualPortIds.Remove(portId);
        SelectionText.Text = _expandedVisualPortIds.Contains(portId) ? "Connector Pins 已展開" : "Connector Pins 已收合";
        HintText.Text = _expandedVisualPortIds.Contains(portId)
            ? "標準 Connector 已展開；現在每個 Pin 都是可拉線的真實 Endpoint。再次雙擊 Connector 可收合。"
            : "標準 Connector 已收合；一般拓樸可直接把整個 Connector 當一個 Endpoint 使用。";
        Render();
        e.Handled = true;
    }

    private void DecorateComponentVisuals()
    {
        if (_decoratingVisuals || _project is null) return;
        _decoratingVisuals = true;
        try
        {
            DecorateComponentImages();
            EnsureEndpointModeVisuals();
        }
        finally
        {
            _decoratingVisuals = false;
        }
    }

    private void DecorateComponentImages()
    {
        if (_project is null || _componentImageResolver is null) return;
        foreach (var border in Surface.Children.OfType<Border>().ToArray())
        {
            if (border.Tag is not string objectId) continue;
            var component = _project.Components.FirstOrDefault(item => string.Equals(item.ComponentInstanceId, objectId, StringComparison.OrdinalIgnoreCase));
            if (component is null || border.Child is not StackPanel panel) continue;
            if (panel.Children.OfType<Image>().Any(image => Equals(image.Tag, "CI-COMPONENT-IMAGE"))) continue;

            var image = new Image
            {
                Tag = "CI-COMPONENT-IMAGE",
                Width = 54,
                Height = 42,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 2, 3, 0),
                ToolTip = "Product Image（產品圖片）— 僅作視覺表示，不作工程真值"
            };
            panel.Children.Insert(0, image);
            _ = LoadComponentImageAsync(component.ComponentDefinitionId, image);
        }
    }

    private async Task LoadComponentImageAsync(string componentDefinitionId, Image target)
    {
        try
        {
            var source = _componentImageResolver is null ? null : await _componentImageResolver(componentDefinitionId);
            var productPage = _componentProductPageResolver is null ? null : await _componentProductPageResolver(componentDefinitionId);
            var localPath = await _topologyImageCache.GetLocalPathAsync(source, productPage);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    target.ToolTip = $"產品圖片目前無法載入。\nImage URL: {source?.AbsoluteUri ?? "<none>"}\nProduct Page fallback: {productPage?.AbsoluteUri ?? "<none>"}";
                });
                return;
            }
            var imagePath = localPath!;

            await Dispatcher.InvokeAsync(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                target.Source = bitmap;
                target.ToolTip = $"Product Image（產品圖片）— 僅作視覺表示，不作工程真值\nCache: {imagePath}";
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                target.ToolTip = $"產品圖片載入失敗：{exception.GetType().Name}: {exception.Message}";
            });
            // Product images are optional display aids. A failed image must never block topology.
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
