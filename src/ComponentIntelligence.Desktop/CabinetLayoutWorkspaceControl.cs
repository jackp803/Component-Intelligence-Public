using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;

namespace ComponentIntelligence.Desktop;

/// <summary>
/// Practical 2D editor for the 2.5D cabinet-fit model. It intentionally does not calculate cable
/// length. Mechanical/User/Imported cable length remains a separate engineering input.
/// </summary>
public sealed class CabinetLayoutWorkspaceControl : UserControl
{
    private const string PaletteDataFormat = "ComponentIntelligence.CabinetLayout.PaletteItem";

    private readonly Func<ElectricalProject> _projectAccessor;
    private readonly Action<string> _recordMutation;
    private readonly Action _projectChanged;
    private readonly Action<string> _status;

    private readonly ComboBox _containerCombo = new() { MinWidth = 180, DisplayMemberPath = nameof(ContainerChoice.Label) };
    private readonly ComboBox _surfaceCombo = new() { MinWidth = 130 };
    private readonly TextBox _containerName = Box("CAB-01", 100);
    private readonly TextBox _containerWidth = Box("600", 68);
    private readonly TextBox _containerHeight = Box("800", 68);
    private readonly TextBox _containerDepth = Box("250", 68);
    private readonly CheckBox _overlayOpposite = new() { Content = "疊加門板/底板 / Overlay opposite face", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fitBanner = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 8, 0) };

    private readonly ListBox _palette = new() { MinWidth = 220 };
    private readonly Canvas _canvas = new() { Background = Brushes.White, AllowDrop = true, MinWidth = 300, MinHeight = 300 };
    private readonly Border _canvasFrame = new() { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(0) };
    private readonly ListBox _issues = new() { MinHeight = 150 };

    private readonly TextBlock _selectedLabel = new() { Text = "尚未選取 / Nothing selected", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly ComboBox _selectedSurface = new();
    private readonly ComboBox _mountOrientation = new() { DisplayMemberPath = nameof(OrientationChoice.Label) };
    private readonly TextBlock _rotationSummary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox _x = Box("0");
    private readonly TextBox _y = Box("0");
    private readonly TextBox _width = Box(string.Empty);
    private readonly TextBox _height = Box(string.Empty);
    private readonly TextBox _depth = Box(string.Empty);
    private readonly TextBox _depthOffset = Box("0");

    private LayoutSelection? _selection;
    private Point _paletteMouseDown;
    private DragState? _drag;
    private double _scale = 1;
    private bool _refreshing;

    public CabinetLayoutWorkspaceControl(
        Func<ElectricalProject> projectAccessor,
        Action<string> recordMutation,
        Action projectChanged,
        Action<string> status)
    {
        _projectAccessor = projectAccessor ?? throw new ArgumentNullException(nameof(projectAccessor));
        _recordMutation = recordMutation ?? throw new ArgumentNullException(nameof(recordMutation));
        _projectChanged = projectChanged ?? throw new ArgumentNullException(nameof(projectChanged));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        _surfaceCombo.ItemsSource = EditableSurfaces();
        _surfaceCombo.SelectedItem = MountingSurface.Backplate;
        _selectedSurface.ItemsSource = Enum.GetValues<MountingSurface>();
        _selectedSurface.SelectedItem = MountingSurface.Backplate;
        _mountOrientation.ItemsSource = OrientationChoices;
        _mountOrientation.SelectedIndex = 0;

        Content = BuildUi();

        _containerCombo.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            LoadContainerFields();
            RefreshCanvasAndFit();
        };
        _surfaceCombo.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selection = null;
            LoadSelection();
            RefreshCanvasAndFit();
        };
        _overlayOpposite.Checked += (_, _) => RefreshCanvas();
        _overlayOpposite.Unchecked += (_, _) => RefreshCanvas();
        _palette.PreviewMouseLeftButtonDown += (_, e) => _paletteMouseDown = e.GetPosition(_palette);
        _palette.PreviewMouseMove += Palette_PreviewMouseMove;
        _canvas.DragOver += Canvas_DragOver;
        _canvas.Drop += Canvas_Drop;
        _canvas.MouseLeftButtonDown += (_, _) =>
        {
            _selection = null;
            LoadSelection();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) RefreshWorkspace();
        };
        Loaded += (_, _) => RefreshWorkspace();
        SizeChanged += (_, _) =>
        {
            if (IsVisible) RefreshCanvas();
        };
    }

    public void RefreshWorkspace()
    {
        var project = _projectAccessor();
        var selectedId = (_containerCombo.SelectedItem as ContainerChoice)?.Id;
        _refreshing = true;
        try
        {
            _containerCombo.ItemsSource = project.LayoutContainers
                .Select(container => new ContainerChoice(container.ContainerId, $"{container.Name}  [{container.ContainerId}]")).ToArray();
            if (selectedId is not null)
                _containerCombo.SelectedItem = ((IEnumerable<ContainerChoice>)_containerCombo.ItemsSource)
                    .FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (_containerCombo.SelectedItem is null && project.LayoutContainers.Count > 0)
                _containerCombo.SelectedIndex = 0;
        }
        finally
        {
            _refreshing = false;
        }

        LoadContainerFields();
        RefreshPalette();
        RefreshCanvasAndFit();
    }

    private UIElement BuildUi()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brushes.WhiteSmoke
        };
        var tools = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(Label("Cabinet｜箱體"));
        tools.Children.Add(_containerCombo);
        tools.Children.Add(Label("Name"));
        tools.Children.Add(_containerName);
        tools.Children.Add(Label("W"));
        tools.Children.Add(_containerWidth);
        tools.Children.Add(Label("H"));
        tools.Children.Add(_containerHeight);
        tools.Children.Add(Label("D"));
        tools.Children.Add(_containerDepth);
        tools.Children.Add(Unit("mm"));
        var saveContainer = Button("新增/更新箱體", (_, _) => SaveContainer());
        saveContainer.Margin = new Thickness(8, 0, 10, 0);
        tools.Children.Add(saveContainer);
        tools.Children.Add(Label("Surface｜安裝面"));
        tools.Children.Add(_surfaceCombo);
        _overlayOpposite.Margin = new Thickness(10, 0, 8, 0);
        tools.Children.Add(_overlayOpposite);
        var fit = Button("檢查 FIT", (_, _) => RefreshFit());
        fit.Margin = new Thickness(4, 0, 0, 0);
        tools.Children.Add(fit);
        tools.Children.Add(_fitBanner);
        toolbar.Child = tools;
        root.Children.Add(toolbar);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        Grid.SetRow(main, 1);
        root.Children.Add(main);

        var left = new DockPanel();
        var leftHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        leftHeader.Children.Add(new TextBlock { Text = "元件 / Components", FontSize = 16, FontWeight = FontWeights.SemiBold });
        leftHeader.Children.Add(new TextBlock
        {
            Text = "拖曳到中央安裝面。缺 W/H 的元件不會用假尺寸放入。箱外設備可在右側標記 External。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(leftHeader, Dock.Top);
        left.Children.Add(leftHeader);
        _palette.DisplayMemberPath = nameof(PaletteItem.Display);
        left.Children.Add(_palette);
        main.Children.Add(left);

        var center = new Grid();
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var centerHint = new TextBlock
        {
            Text = "2D 操作 + 2.5D 驗證：不同安裝面的 XY 重疊不等於碰撞；深度/關門空間由驗證器判斷。Layout 不計算線長。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        center.Children.Add(centerHint);
        _canvasFrame.Child = _canvas;
        var scroller = new ScrollViewer
        {
            Content = _canvasFrame,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroller, 1);
        center.Children.Add(scroller);
        Grid.SetColumn(center, 2);
        main.Children.Add(center);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var properties = new StackPanel();
        properties.Children.Add(new TextBlock { Text = "選取物件 / Selected", FontSize = 16, FontWeight = FontWeights.SemiBold });
        properties.Children.Add(_selectedLabel);
        properties.Children.Add(Field("Mounting Surface｜安裝面", _selectedSurface));
        properties.Children.Add(Field("安裝方向 / Mounted face", _mountOrientation));
        properties.Children.Add(TwoFields("X mm", _x, "Y mm", _y));
        properties.Children.Add(TwoFields("Width mm", _width, "Height mm", _height));
        properties.Children.Add(TwoFields("Depth mm", _depth, "Depth Offset mm", _depthOffset));
        var propertyButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
        propertyButtons.Children.Add(Button("套用 / Apply", (_, _) => ApplySelection()));
        var rotate = Button("旋轉 90°", (_, _) => RotateSelection());
        rotate.Margin = new Thickness(6, 0, 0, 0);
        propertyButtons.Children.Add(rotate);
        propertyButtons.Children.Add(_rotationSummary);
        var external = Button("標記箱外 External", (_, _) => MarkExternal());
        external.Margin = new Thickness(6, 0, 0, 0);
        propertyButtons.Children.Add(external);
        var clear = Button("清除位置", (_, _) => ClearPlacement());
        clear.Margin = new Thickness(6, 0, 0, 0);
        propertyButtons.Children.Add(clear);
        properties.Children.Add(propertyButtons);
        properties.Children.Add(new TextBlock
        {
            Text = "尺寸欄位是 Project Layout Override（專案佈局覆寫）。若元件本身資料缺尺寸，建議先回拓樸雙擊元件補 Datasheet / 圖片 / 資料來源。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        });
        right.Children.Add(properties);

        var issueHeader = new TextBlock { Text = "Cabinet Fit Issues｜箱體檢查", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
        Grid.SetRow(issueHeader, 1);
        right.Children.Add(issueHeader);
        _issues.DisplayMemberPath = nameof(IssueRow.Display);
        Grid.SetRow(_issues, 2);
        right.Children.Add(_issues);
        Grid.SetColumn(right, 4);
        main.Children.Add(right);

        return root;
    }

    private void SaveContainer()
    {
        if (!TryPositive(_containerWidth.Text, out var width) || !TryPositive(_containerHeight.Text, out var height))
        {
            MessageBox.Show("Cabinet Width / Height 必須是正數。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        double? depth = null;
        if (!string.IsNullOrWhiteSpace(_containerDepth.Text))
        {
            if (!TryPositive(_containerDepth.Text, out var parsedDepth))
            {
                MessageBox.Show("Cabinet Depth 必須是正數，或留空表示未知。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            depth = parsedDepth;
        }

        var project = _projectAccessor();
        var selected = SelectedContainer(project);
        _recordMutation(selected is null ? "Add cabinet layout container" : "Update cabinet dimensions");
        if (selected is null)
        {
            selected = new LayoutContainer
            {
                ContainerId = $"cab-{Guid.NewGuid():N}",
                Name = string.IsNullOrWhiteSpace(_containerName.Text) ? "CAB" : _containerName.Text.Trim(),
                WidthMm = width,
                HeightMm = height,
                DepthMm = depth
            };
            project.LayoutContainers.Add(selected);
        }
        else
        {
            selected.Name = string.IsNullOrWhiteSpace(_containerName.Text) ? selected.Name : _containerName.Text.Trim();
            selected.WidthMm = width;
            selected.HeightMm = height;
            selected.DepthMm = depth;
        }

        _status($"Cabinet '{selected.Name}' 已更新：{width:0.###} × {height:0.###} × {(depth?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?")} mm。Depth 未知時不會假裝完成 2.5D 驗證。");
        _projectChanged();
        RefreshWorkspace();
        SelectContainer(selected.ContainerId);
    }

    private void Palette_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _palette.SelectedItem is not PaletteItem item) return;
        var current = e.GetPosition(_palette);
        if (Math.Abs(current.X - _paletteMouseDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _paletteMouseDown.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(_palette, new DataObject(PaletteDataFormat, item), DragDropEffects.Move);
    }

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(PaletteDataFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(PaletteDataFormat) is not PaletteItem item) return;
        var project = _projectAccessor();
        var container = SelectedContainer(project);
        if (container is null)
        {
            MessageBox.Show("請先建立或選擇 Cabinet（箱體）。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_surfaceCombo.SelectedItem is not MountingSurface surface || surface is MountingSurface.Unknown or MountingSurface.External)
        {
            MessageBox.Show("請選擇 Backplate / Door / Wall 等箱體安裝面。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = ResolveObject(project, item.ObjectId, item.Kind);
        if (target is null) return;
        if (target.Footprint is null || target.Footprint.WidthMm <= 0 || target.Footprint.HeightMm <= 0)
        {
            MessageBox.Show(
                $"{item.Label} 缺少 Width / Height（寬／高），不能用假尺寸放進 Cabinet。\n\n請先在拓樸雙擊元件補資料，或選取後在右側填入 Project Layout Override。",
                "需要尺寸資料",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _selection = new LayoutSelection(item.Kind, item.ObjectId);
            LoadSelection();
            return;
        }

        var point = e.GetPosition(_canvas);
        _recordMutation($"Place {item.Label} on {surface}");
        target.Placement = new PhysicalPlacement
        {
            ParentContainerId = container.ContainerId,
            XMm = Math.Max(0, point.X / _scale),
            YMm = Math.Max(0, point.Y / _scale),
            Surface = surface,
            DepthOffsetMm = 0
        };
        _selection = new LayoutSelection(item.Kind, item.ObjectId);
        _status($"已將 {item.Label} 放到 {container.Name} / {surface}。2D 重疊不會單獨被判成碰撞。 ");
        _projectChanged();
        RefreshWorkspace();
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not NodeTag tag) return;
        e.Handled = true;
        _selection = new LayoutSelection(tag.Kind, tag.ObjectId);
        LoadSelection();
        var project = _projectAccessor();
        var target = ResolveObject(project, tag.ObjectId, tag.Kind);
        if (target?.Placement is null) return;
        _drag = new DragState(tag.Kind, tag.ObjectId, e.GetPosition(_canvas), target.Placement.XMm, target.Placement.YMm, false);
        element.CaptureMouse();
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is null || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement element) return;
        var project = _projectAccessor();
        var target = ResolveObject(project, _drag.ObjectId, _drag.Kind);
        if (target?.Placement is null) return;
        var current = e.GetPosition(_canvas);
        var dxPx = current.X - _drag.MouseStart.X;
        var dyPx = current.Y - _drag.MouseStart.Y;
        if (!_drag.Recorded && Math.Abs(dxPx) + Math.Abs(dyPx) > 2)
        {
            _recordMutation($"Move {target.Label} in cabinet layout");
            _drag = _drag with { Recorded = true };
        }
        if (!_drag.Recorded) return;
        target.Placement.XMm = _drag.XStart + dxPx / _scale;
        target.Placement.YMm = _drag.YStart + dyPx / _scale;
        Canvas.SetLeft(element, target.Placement.XMm * _scale);
        Canvas.SetTop(element, target.Placement.YMm * _scale);
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element) element.ReleaseMouseCapture();
        if (_drag?.Recorded == true)
        {
            _projectChanged();
            RefreshCanvasAndFit();
            LoadSelection();
        }
        _drag = null;
    }

    private void ApplySelection()
    {
        if (_selection is null) return;
        var project = _projectAccessor();
        var target = ResolveObject(project, _selection.ObjectId, _selection.Kind);
        var container = SelectedContainer(project);
        if (target is null || container is null) return;
        if (!TryNumber(_x.Text, out var x) || !TryNumber(_y.Text, out var y) ||
            !TryPositive(_width.Text, out var width) || !TryPositive(_height.Text, out var height))
        {
            MessageBox.Show("X/Y 必須是數字；Width/Height 必須是正數。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        double? depth = null;
        if (!string.IsNullOrWhiteSpace(_depth.Text))
        {
            if (!TryPositive(_depth.Text, out var parsedDepth))
            {
                MessageBox.Show("Depth 必須是正數或留空。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            depth = parsedDepth;
        }
        if (!TryNumber(_depthOffset.Text, out var depthOffset) || depthOffset < 0)
        {
            MessageBox.Show("Depth Offset 必須是 0 或正數。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var surface = _selectedSurface.SelectedItem is MountingSurface selectedSurface ? selectedSurface : MountingSurface.Unknown;
        var mountOrientation = _mountOrientation.SelectedItem is OrientationChoice orientation
            ? orientation.Value
            : ComponentMountOrientation.Front;
        if (mountOrientation is not ComponentMountOrientation.Front && depth is null)
        {
            MessageBox.Show("側面或平放投影需要 Depth 尺寸。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _recordMutation($"Edit cabinet layout properties for {target.Label}");
        target.Footprint ??= new PhysicalFootprint();
        target.Footprint.WidthMm = width;
        target.Footprint.HeightMm = height;
        target.Footprint.DepthMm = depth;
        target.Footprint.MountingType = MapMountingType(surface, target.Footprint.MountingType);
        if (target.Kind == LayoutObjectKind.Component)
        {
            var component = project.Components.First(item =>
                string.Equals(item.ComponentInstanceId, target.ObjectId, StringComparison.OrdinalIgnoreCase));
            component.FootprintOverride = true;
        }
        target.Placement = new PhysicalPlacement
        {
            ParentContainerId = container.ContainerId,
            XMm = x,
            YMm = y,
            RotationDegrees = target.Placement?.RotationDegrees ?? 0,
            MountOrientation = mountOrientation,
            MountTargetId = target.Placement?.MountTargetId,
            Surface = surface,
            DepthOffsetMm = depthOffset
        };
        _status($"已更新 {target.Label} 的 Physical Layout。未知 Depth 仍保持未知，不會假裝 FIT。");
        _projectChanged();
        RefreshWorkspace();
    }

    private void RotateSelection()
    {
        if (_selection is null) return;
        var target = ResolveObject(_projectAccessor(), _selection.ObjectId, _selection.Kind);
        if (target?.Placement is null) return;

        _recordMutation($"Rotate {target.Label} in cabinet layout");
        target.Placement.RotationDegrees = PhysicalFootprintProjection.NormalizeRotation(target.Placement.RotationDegrees + 90);
        _status($"已將 {target.Label} 旋轉至 {target.Placement.RotationDegrees}°。");
        _projectChanged();
        RefreshCanvasAndFit();
        LoadSelection();
    }

    private void MarkExternal()
    {
        if (_selection is null) return;
        var project = _projectAccessor();
        var target = ResolveObject(project, _selection.ObjectId, _selection.Kind);
        var container = SelectedContainer(project) ?? project.LayoutContainers.FirstOrDefault();
        if (target is null || container is null)
        {
            MessageBox.Show("請先建立 Cabinet，External 仍需保留專案的物理分類 context。", "Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _recordMutation($"Mark {target.Label} external to cabinet");
        target.Placement = new PhysicalPlacement
        {
            ParentContainerId = container.ContainerId,
            Surface = MountingSurface.External,
            XMm = 0,
            YMm = 0
        };
        _status($"{target.Label} 已標記為 External（箱外），不占用 Cabinet 內部空間。 ");
        _projectChanged();
        RefreshWorkspace();
    }

    private void ClearPlacement()
    {
        if (_selection is null) return;
        var project = _projectAccessor();
        var target = ResolveObject(project, _selection.ObjectId, _selection.Kind);
        if (target is null) return;
        _recordMutation($"Clear physical placement for {target.Label}");
        target.Placement = null;
        _status($"已清除 {target.Label} 的 Physical Placement；Cabinet Fit 將保持 REVIEW，直到重新分類。 ");
        _projectChanged();
        RefreshWorkspace();
    }

    private void RefreshPalette()
    {
        var project = _projectAccessor();
        var items = new List<PaletteItem>();
        items.AddRange(project.Components
            .Where(component => component.ResponsibilityScope is not (ResponsibilityScope.OutOfScope or ResponsibilityScope.NotRequired))
            .Select(component => new PaletteItem(
                LayoutObjectKind.Component,
                component.ComponentInstanceId,
                component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId,
                DescribeSize(component.Footprint),
                component.Placement?.Surface ?? MountingSurface.Unknown)));
        items.AddRange(project.TerminalBlocks.Select(block => new PaletteItem(
            LayoutObjectKind.TerminalBlock,
            block.TerminalBlockId,
            block.ReferenceDesignator,
            DescribeSize(block.Footprint),
            block.Placement?.Surface ?? MountingSurface.Unknown)));
        _palette.ItemsSource = items.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void RefreshCanvasAndFit()
    {
        RefreshCanvas();
        RefreshFit();
    }

    private void RefreshCanvas()
    {
        _canvas.Children.Clear();
        var project = _projectAccessor();
        var container = SelectedContainer(project);
        var surface = _surfaceCombo.SelectedItem is MountingSurface selectedSurface ? selectedSurface : MountingSurface.Backplate;
        if (container is null)
        {
            _canvas.Width = 700;
            _canvas.Height = 500;
            var empty = new TextBlock { Text = "先建立 Cabinet（箱體）", FontSize = 22, Foreground = Brushes.Gray };
            Canvas.SetLeft(empty, 30);
            Canvas.SetTop(empty, 30);
            _canvas.Children.Add(empty);
            return;
        }

        var face = FaceSize(container, surface);
        var targetWidth = Math.Max(520, ActualWidth - 620);
        var targetHeight = Math.Max(420, ActualHeight - 180);
        _scale = face is null ? 1 : Math.Min(Math.Min(targetWidth / face.Value.Width, targetHeight / face.Value.Height), 1.6);
        if (!double.IsFinite(_scale) || _scale <= 0) _scale = 1;
        var faceWidth = (face?.Width ?? container.WidthMm) * _scale;
        var faceHeight = (face?.Height ?? container.HeightMm) * _scale;
        _canvas.Width = Math.Max(300, faceWidth);
        _canvas.Height = Math.Max(300, faceHeight);

        var faceRect = new Rectangle
        {
            Width = faceWidth,
            Height = faceHeight,
            Stroke = Brushes.SlateGray,
            StrokeThickness = 2,
            Fill = Brushes.White
        };
        _canvas.Children.Add(faceRect);

        foreach (var zone in container.Zones.Where(zone => zone.Surface is MountingSurface.Unknown || zone.Surface == surface))
        {
            var rect = new Rectangle
            {
                Width = zone.Bounds.Width * _scale,
                Height = zone.Bounds.Height * _scale,
                Stroke = zone.IsForbidden ? Brushes.Firebrick : Brushes.DarkOrange,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = Brushes.Transparent,
                ToolTip = $"Zone: {zone.Name} | {(zone.IsForbidden ? "Forbidden" : "Keep-out")}"
            };
            Canvas.SetLeft(rect, zone.Bounds.X * _scale);
            Canvas.SetTop(rect, zone.Bounds.Y * _scale);
            _canvas.Children.Add(rect);
        }

        foreach (var duct in project.CableDucts.Where(duct => string.Equals(duct.ParentContainerId, container.ContainerId, StringComparison.OrdinalIgnoreCase) &&
                                                               duct.Surface is MountingSurface.Unknown || duct.Surface == surface))
        {
            var rect = new Rectangle
            {
                Width = duct.Bounds.Width * _scale,
                Height = duct.Bounds.Height * _scale,
                Stroke = Brushes.SteelBlue,
                Fill = Brushes.AliceBlue,
                Opacity = 0.55,
                ToolTip = $"Cable Duct {duct.CableDuctId}"
            };
            Canvas.SetLeft(rect, duct.Bounds.X * _scale);
            Canvas.SetTop(rect, duct.Bounds.Y * _scale);
            _canvas.Children.Add(rect);
        }

        foreach (var rail in project.DinRails.Where(rail => string.Equals(rail.ParentContainerId, container.ContainerId, StringComparison.OrdinalIgnoreCase) &&
                                                            rail.Surface is MountingSurface.Unknown || rail.Surface == surface))
        {
            var line = new Line
            {
                X1 = rail.XMm * _scale,
                Y1 = rail.YMm * _scale,
                X2 = (rail.Horizontal ? rail.XMm + rail.LengthMm : rail.XMm) * _scale,
                Y2 = (rail.Horizontal ? rail.YMm : rail.YMm + rail.LengthMm) * _scale,
                Stroke = Brushes.Gray,
                StrokeThickness = 5,
                ToolTip = $"DIN Rail {rail.DinRailId} | {rail.LengthMm:0.###} mm"
            };
            _canvas.Children.Add(line);
        }

        if (_overlayOpposite.IsChecked == true && surface is MountingSurface.Backplate or MountingSurface.Door)
        {
            var opposite = surface == MountingSurface.Backplate ? MountingSurface.Door : MountingSurface.Backplate;
            foreach (var target in AllObjects(project).Where(target => target.Placement?.ParentContainerId == container.ContainerId && target.Placement.Surface == opposite && target.Footprint is not null))
                DrawObject(target, overlay: true);
        }

        foreach (var target in AllObjects(project).Where(target =>
                     target.Placement is not null &&
                     string.Equals(target.Placement.ParentContainerId, container.ContainerId, StringComparison.OrdinalIgnoreCase) &&
                     target.Placement.Surface == surface &&
                     target.Footprint is not null))
            DrawObject(target, overlay: false);
    }

    private void DrawObject(LayoutTarget target, bool overlay)
    {
        var placement = target.Placement!;
        var footprint = target.Footprint!;
        var projection = PhysicalFootprintProjection.Project(footprint, placement);
        var width = projection.WidthMm * _scale;
        var height = projection.HeightMm * _scale;
        var isSelected = _selection is not null && _selection.Kind == target.Kind && string.Equals(_selection.ObjectId, target.ObjectId, StringComparison.OrdinalIgnoreCase);

        var border = new Border
        {
            Width = Math.Max(12, width),
            Height = Math.Max(12, height),
            BorderBrush = overlay ? Brushes.MediumPurple : isSelected ? Brushes.DarkBlue : Brushes.DimGray,
            BorderThickness = new Thickness(isSelected ? 3 : 1.5),
            Background = overlay ? Brushes.Lavender : Brushes.WhiteSmoke,
            Opacity = overlay ? 0.35 : 0.92,
            CornerRadius = new CornerRadius(3),
            Tag = new NodeTag(target.Kind, target.ObjectId),
            ToolTip = $"{target.Label}\nSurface: {placement.Surface}\nMounted face: {placement.MountOrientation}, {PhysicalFootprintProjection.NormalizeRotation(placement.RotationDegrees)}°\nSource W/H/D: {footprint.WidthMm:0.###}/{footprint.HeightMm:0.###}/{(footprint.DepthMm?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?")} mm\nShown W/H: {projection.WidthMm:0.###}/{projection.HeightMm:0.###} mm\nProtrusion: {(projection.ProtrusionMm?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?")} mm\nDepth offset: {placement.DepthOffsetMm:0.###} mm"
        };
        border.Child = new TextBlock
        {
            Text = $"{target.Label}\nD:{(footprint.DepthMm?.ToString("0.#", CultureInfo.InvariantCulture) ?? "?")} mm",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(4)
        };
        Canvas.SetLeft(border, placement.XMm * _scale);
        Canvas.SetTop(border, placement.YMm * _scale);
        if (!overlay)
        {
            border.MouseLeftButtonDown += Node_MouseLeftButtonDown;
            border.MouseMove += Node_MouseMove;
            border.MouseLeftButtonUp += Node_MouseLeftButtonUp;
        }
        _canvas.Children.Add(border);
    }

    private void RefreshFit()
    {
        var report = new CabinetFitEvaluator().Evaluate(_projectAccessor());
        _fitBanner.Text = report.Status switch
        {
            CabinetFitStatus.Fit => $"FIT ✓  Classified {report.ClassifiedObjectCount}",
            CabinetFitStatus.NotFit => $"NOT FIT ✕  Issues {report.Issues.Count}",
            _ => $"REVIEW △  Unclassified {report.UnclassifiedObjectCount} | Issues {report.Issues.Count}"
        };
        _fitBanner.Foreground = report.Status switch
        {
            CabinetFitStatus.Fit => Brushes.DarkGreen,
            CabinetFitStatus.NotFit => Brushes.Firebrick,
            _ => Brushes.DarkOrange
        };
        _issues.ItemsSource = report.Issues
            .Select(issue => new IssueRow(issue.RuleId, issue.Severity, issue.ObjectId, issue.Message))
            .ToArray();
    }

    private void LoadContainerFields()
    {
        var container = SelectedContainer(_projectAccessor());
        if (container is null) return;
        _containerName.Text = container.Name;
        _containerWidth.Text = Format(container.WidthMm);
        _containerHeight.Text = Format(container.HeightMm);
        _containerDepth.Text = container.DepthMm is double depth ? Format(depth) : string.Empty;
    }

    private void LoadSelection()
    {
        if (_selection is null)
        {
            _selectedLabel.Text = "尚未選取 / Nothing selected";
            return;
        }
        var target = ResolveObject(_projectAccessor(), _selection.ObjectId, _selection.Kind);
        if (target is null) return;
        _selectedLabel.Text = $"{target.Label}\n{target.Kind} | {target.ObjectId}";
        _selectedSurface.SelectedItem = target.Placement?.Surface ?? MountingSurface.Unknown;
        var mountOrientation = target.Placement?.MountOrientation ?? ComponentMountOrientation.Front;
        _mountOrientation.SelectedItem = OrientationChoices.First(choice => choice.Value == mountOrientation);
        _rotationSummary.Text = $"目前 {PhysicalFootprintProjection.NormalizeRotation(target.Placement?.RotationDegrees ?? 0)}°";
        _x.Text = Format(target.Placement?.XMm ?? 0);
        _y.Text = Format(target.Placement?.YMm ?? 0);
        _width.Text = target.Footprint is null || target.Footprint.WidthMm <= 0 ? string.Empty : Format(target.Footprint.WidthMm);
        _height.Text = target.Footprint is null || target.Footprint.HeightMm <= 0 ? string.Empty : Format(target.Footprint.HeightMm);
        _depth.Text = target.Footprint?.DepthMm is double depth ? Format(depth) : string.Empty;
        _depthOffset.Text = Format(target.Placement?.DepthOffsetMm ?? 0);
    }

    private LayoutContainer? SelectedContainer(ElectricalProject project)
    {
        var id = (_containerCombo.SelectedItem as ContainerChoice)?.Id;
        return id is null ? null : project.LayoutContainers.FirstOrDefault(item => string.Equals(item.ContainerId, id, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectContainer(string id)
    {
        var choices = _containerCombo.ItemsSource as IEnumerable<ContainerChoice>;
        if (choices is null) return;
        _containerCombo.SelectedItem = choices.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<MountingSurface> EditableSurfaces() =>
        Enum.GetValues<MountingSurface>().Where(surface => surface is not (MountingSurface.Unknown or MountingSurface.External));

    private static readonly OrientationChoice[] OrientationChoices =
    [
        new(ComponentMountOrientation.Front, "正面 W×H"),
        new(ComponentMountOrientation.Side, "側面 D×H"),
        new(ComponentMountOrientation.Top, "平放 W×D")
    ];

    private static (double Width, double Height)? FaceSize(LayoutContainer container, MountingSurface surface) => surface switch
    {
        MountingSurface.LeftWall or MountingSurface.RightWall when container.DepthMm is double depth => (depth, container.HeightMm),
        MountingSurface.Top or MountingSurface.Bottom when container.DepthMm is double depth => (container.WidthMm, depth),
        MountingSurface.LeftWall or MountingSurface.RightWall or MountingSurface.Top or MountingSurface.Bottom => null,
        _ => (container.WidthMm, container.HeightMm)
    };

    private static MountingType MapMountingType(MountingSurface surface, MountingType current) => surface switch
    {
        MountingSurface.Door => MountingType.Door,
        MountingSurface.Backplate when current != MountingType.DinRail => MountingType.Backplate,
        MountingSurface.External => MountingType.MachineFrame,
        _ => current == MountingType.Unknown ? MountingType.Surface : current
    };

    private static string DescribeSize(PhysicalFootprint? footprint)
    {
        if (footprint is null || footprint.WidthMm <= 0 || footprint.HeightMm <= 0) return "尺寸缺失 / SIZE MISSING";
        return $"{footprint.WidthMm:0.#}×{footprint.HeightMm:0.#}×{(footprint.DepthMm?.ToString("0.#", CultureInfo.InvariantCulture) ?? "?")} mm";
    }

    private static IEnumerable<LayoutTarget> AllObjects(ElectricalProject project)
    {
        foreach (var component in project.Components)
            yield return new LayoutTarget(LayoutObjectKind.Component, component.ComponentInstanceId,
                component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId,
                component.Footprint, component.Placement,
                footprint => component.Footprint = footprint,
                placement => component.Placement = placement);
        foreach (var block in project.TerminalBlocks)
            yield return new LayoutTarget(LayoutObjectKind.TerminalBlock, block.TerminalBlockId, block.ReferenceDesignator,
                block.Footprint, block.Placement,
                footprint => block.Footprint = footprint,
                placement => block.Placement = placement);
    }

    private static LayoutTarget? ResolveObject(ElectricalProject project, string objectId, LayoutObjectKind kind) =>
        AllObjects(project).FirstOrDefault(item => item.Kind == kind && string.Equals(item.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));

    private static TextBox Box(string text, double width = 86) => new() { Text = text, Width = width, Height = 28, Margin = new Thickness(3, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
    private static TextBlock Unit(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 4, 0) };
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5) };
        button.Click += handler;
        return button;
    }

    private static FrameworkElement Field(string label, Control control)
    {
        control.Margin = new Thickness(0, 3, 0, 8);
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(control);
        return panel;
    }

    private static FrameworkElement TwoFields(string labelA, Control a, string labelB, Control b)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var pa = new StackPanel();
        pa.Children.Add(new TextBlock { Text = labelA, FontWeight = FontWeights.SemiBold });
        pa.Children.Add(a);
        var pb = new StackPanel();
        pb.Children.Add(new TextBlock { Text = labelB, FontWeight = FontWeights.SemiBold });
        pb.Children.Add(b);
        Grid.SetColumn(pb, 2);
        grid.Children.Add(pa);
        grid.Children.Add(pb);
        return grid;
    }

    private static bool TryNumber(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool TryPositive(string? text, out double value) => TryNumber(text, out value) && value > 0;
    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private enum LayoutObjectKind { Component, TerminalBlock }
    private sealed record ContainerChoice(string Id, string Label);
    private sealed record LayoutSelection(LayoutObjectKind Kind, string ObjectId);
    private sealed record NodeTag(LayoutObjectKind Kind, string ObjectId);
    private sealed record DragState(LayoutObjectKind Kind, string ObjectId, Point MouseStart, double XStart, double YStart, bool Recorded);
    private sealed record IssueRow(string RuleId, ValidationSeverity Severity, string ObjectId, string Message)
    {
        public string Display => $"[{Severity}] {RuleId}\n{Message}";
    }
    private sealed record PaletteItem(LayoutObjectKind Kind, string ObjectId, string Label, string Size, MountingSurface Surface)
    {
        public string Display => $"{Label}\n{Size}\n{(Surface == MountingSurface.Unknown ? "未分類" : Surface.ToString())}";
    }

    private sealed record OrientationChoice(ComponentMountOrientation Value, string Label);

    private sealed class LayoutTarget
    {
        private readonly Action<PhysicalFootprint?> _setFootprint;
        private readonly Action<PhysicalPlacement?> _setPlacement;
        private PhysicalFootprint? _footprint;
        private PhysicalPlacement? _placement;

        public LayoutTarget(
            LayoutObjectKind kind,
            string objectId,
            string label,
            PhysicalFootprint? footprint,
            PhysicalPlacement? placement,
            Action<PhysicalFootprint?> setFootprint,
            Action<PhysicalPlacement?> setPlacement)
        {
            Kind = kind;
            ObjectId = objectId;
            Label = label;
            _setFootprint = setFootprint;
            _setPlacement = setPlacement;
            Footprint = footprint;
            Placement = placement;
        }

        public LayoutObjectKind Kind { get; }
        public string ObjectId { get; }
        public string Label { get; }
        public PhysicalFootprint? Footprint
        {
            get => _footprint;
            set { _footprint = value; _setFootprint(value); }
        }
        public PhysicalPlacement? Placement
        {
            get => _placement;
            set { _placement = value; _setPlacement(value); }
        }
    }
}
