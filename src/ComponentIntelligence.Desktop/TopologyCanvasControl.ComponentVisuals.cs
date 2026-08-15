using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        Surface.LayoutUpdated += (_, _) => DecorateComponentVisuals();
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
        if (!_project.Components.SelectMany(component => component.Ports).Any(port => string.Equals(port.PortId, portId, StringComparison.OrdinalIgnoreCase))) return;

        if (!_expandedVisualPortIds.Add(portId)) _expandedVisualPortIds.Remove(portId);
        SelectionText.Text = _expandedVisualPortIds.Contains(portId) ? "Pin 清單已展開" : "Pin 清單已收合";
        HintText.Text = "Port 雙擊可展開／收合 Pin；展開只改顯示，不會建立或修改任何接線。";
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
            DecorateExpandedPins();
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

    private void DecorateExpandedPins()
    {
        if (_project is null || _expandedVisualPortIds.Count == 0) return;
        var children = Surface.Children.Cast<UIElement>().ToArray();
        foreach (var marker in children.OfType<Border>())
        {
            if (marker.Tag is not string portId || !_expandedVisualPortIds.Contains(portId)) continue;
            if (Math.Abs(marker.Width - 14d) > 0.01 || Math.Abs(marker.Height - 14d) > 0.01) continue;
            if (Surface.Children.OfType<FrameworkElement>().Any(element => Equals(element.Tag, "CI-PINS:" + portId))) continue;

            var port = _project.Components.SelectMany(component => component.Ports)
                .FirstOrDefault(item => string.Equals(item.PortId, portId, StringComparison.OrdinalIgnoreCase));
            if (port is null) continue;

            var panel = new StackPanel
            {
                Tag = "CI-PINS:" + portId,
                Background = Brushes.White,
                Opacity = 0.96,
                ToolTip = "Expanded Pin list（展開腳位）— 顯示用途"
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"{port.Name} Pins",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray
            });
            foreach (var pin in port.Pins.Take(16))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{pin.PinNumber}: {pin.Function ?? pin.PinName ?? "Unknown"}",
                    FontSize = 9,
                    Foreground = LayerBrush(pin.Layer),
                    Padding = new Thickness(2, 0, 2, 0)
                });
            }
            if (port.Pins.Count > 16)
                panel.Children.Add(new TextBlock { Text = $"… +{port.Pins.Count - 16}", FontSize = 9, Foreground = Brushes.Gray });

            Canvas.SetLeft(panel, Canvas.GetLeft(marker) + 22);
            Canvas.SetTop(panel, Math.Max(0, Canvas.GetTop(marker) - 8));
            Surface.Children.Add(panel);
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
