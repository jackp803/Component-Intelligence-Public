using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Layout;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public sealed partial class CabinetLayoutWorkspaceControl
{
    private readonly TextBox _terminalSectionName = Box(string.Empty);
    private readonly TextBox _terminalSectionFunction = Box(string.Empty);
    private readonly ComboBox _terminalSectionColor = new()
    {
        DisplayMemberPath = nameof(SectionColorChoice.Label),
        MinWidth = 150
    };

    private static readonly SectionColorChoice[] TerminalSectionColors =
    [
        new("#B22222", "紅色｜Power +"),
        new("#4169E1", "藍色｜0V / Return"),
        new("#FF8C00", "橙色｜AC / Analog"),
        new("#228B22", "綠色｜Digital"),
        new("#6A5ACD", "紫色｜Communication"),
        new("#2F4F4F", "灰色｜General")
    ];

    private UIElement BuildTerminalSectionEditor()
    {
        _terminalSectionColor.ItemsSource = TerminalSectionColors;
        _terminalSectionColor.SelectedItem = TerminalSectionColors[^1];
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "端子排分區 / Terminal Section",
            FontSize = 15d,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0d, 10d, 0d, 4d)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "框選端子後可建立區塊名稱；只改 Layout 顯示，不合併 BOM 或接線。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(Field("區塊名稱，例如 54V 電源", _terminalSectionName));
        panel.Children.Add(Field("功能 / Function", _terminalSectionFunction));
        panel.Children.Add(Field("顏色 / Color", _terminalSectionColor));
        var buttons = new WrapPanel { Margin = new Thickness(0d, 5d, 0d, 5d) };
        buttons.Children.Add(Button("建立/更新區塊", (_, _) => SaveTerminalSection()));
        var remove = Button("移除區塊", (_, _) => RemoveTerminalSection());
        remove.Margin = new Thickness(6d, 0d, 0d, 0d);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        return panel;
    }

    private void SaveTerminalSection()
    {
        var project = _projectAccessor();
        var targets = SelectedTerminalTargets(project);
        if (targets.Length == 0)
        {
            MessageBox.Show("請先在 Layout 框選至少一個端子台。", "端子排分區", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = string.IsNullOrWhiteSpace(_terminalSectionName.Text)
            ? "Terminal section"
            : _terminalSectionName.Text.Trim();
        var function = string.IsNullOrWhiteSpace(_terminalSectionFunction.Text)
            ? null
            : _terminalSectionFunction.Text.Trim();
        var color = (_terminalSectionColor.SelectedItem as SectionColorChoice)?.Hex ?? "#2F4F4F";
        var selectedIds = targets.Select(target => target.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = project.TerminalStripSections.FirstOrDefault(section =>
            section.MemberObjectIds.Any(selectedIds.Contains));

        _recordMutation(existing is null ? "Create terminal strip section" : "Update terminal strip section");
        if (targets.Length > 1)
        {
            foreach (var section in project.TerminalStripSections)
                section.MemberObjectIds.RemoveAll(selectedIds.Contains);
        }

        existing ??= new TerminalStripSection
        {
            SectionId = $"terminal-section-{Guid.NewGuid():N}"
        };
        if (!project.TerminalStripSections.Contains(existing))
            project.TerminalStripSections.Add(existing);
        existing.Name = name;
        existing.Function = function;
        existing.ColorHex = color;
        existing.ParentContainerId = targets[0].Placement!.ParentContainerId;
        existing.Surface = targets[0].Placement.Surface;
        if (targets.Length > 1 || existing.MemberObjectIds.Count == 0)
        {
            existing.MemberObjectIds.Clear();
            existing.MemberObjectIds.AddRange(targets.Select(target => target.ObjectId));
        }
        project.TerminalStripSections.RemoveAll(section => section.MemberObjectIds.Count == 0);
        _projectChanged();
        _status($"端子排區塊「{name}」已保存，共 {existing.MemberObjectIds.Count} 顆端子；BOM 與接線身份保持獨立。");
        RefreshCanvasAndFit();
        LoadSelection();
    }

    private void RemoveTerminalSection()
    {
        var project = _projectAccessor();
        var selectedIds = _selectedLayoutItems.Select(item => item.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sections = project.TerminalStripSections
            .Where(section => section.MemberObjectIds.Any(selectedIds.Contains))
            .ToArray();
        if (sections.Length == 0) return;
        _recordMutation($"Remove {sections.Length} terminal strip section(s)");
        foreach (var section in sections)
            project.TerminalStripSections.Remove(section);
        _projectChanged();
        _status($"已移除 {sections.Length} 個端子排顯示區塊；端子本身、BOM 與接線未刪除。");
        RefreshCanvasAndFit();
        LoadSelection();
    }

    private void LoadTerminalSectionFields()
    {
        var selectedIds = _selectedLayoutItems.Select(item => item.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var section = _projectAccessor().TerminalStripSections.FirstOrDefault(candidate =>
            candidate.MemberObjectIds.Any(selectedIds.Contains));
        _terminalSectionName.Text = section?.Name ?? string.Empty;
        _terminalSectionFunction.Text = section?.Function ?? string.Empty;
        _terminalSectionColor.SelectedItem = TerminalSectionColors.FirstOrDefault(choice =>
            string.Equals(choice.Hex, section?.ColorHex, StringComparison.OrdinalIgnoreCase)) ?? TerminalSectionColors[^1];
    }

    private LayoutTarget[] SelectedTerminalTargets(ElectricalProject project) => _selectedLayoutItems
        .Select(selection => ResolveObject(project, selection.ObjectId, selection.Kind))
        .Where(target => target is not null && target.Placement is not null && IsTerminalTarget(target))
        .Cast<LayoutTarget>()
        .ToArray();

    private bool IsTerminalTarget(LayoutTarget target)
    {
        if (target.Kind == LayoutObjectKind.TerminalBlock) return true;
        return _projectAccessor().Components.FirstOrDefault(component => string.Equals(
                   component.ComponentInstanceId,
                   target.ObjectId,
                   StringComparison.OrdinalIgnoreCase)) is { } component &&
               TopologyPaletteMaterialPolicy.Classify(component.TypeKey) == TopologyPaletteMaterialKind.TerminalBlock;
    }

    private void DrawTerminalSectionFrames(ElectricalProject project, string containerId, MountingSurface surface)
    {
        foreach (var section in project.TerminalStripSections.Where(section =>
                     string.Equals(section.ParentContainerId, containerId, StringComparison.OrdinalIgnoreCase) &&
                     section.Surface == surface))
        {
            var members = section.MemberObjectIds
                .Select(id => ResolveAnyObject(project, id))
                .Where(target => target?.Placement is not null && target.Footprint is not null)
                .Cast<LayoutTarget>()
                .ToArray();
            if (members.Length == 0) continue;
            var bounds = members.Select(target =>
            {
                var projection = PhysicalFootprintProjection.Project(target.Footprint!, target.Placement!);
                return new Rect(
                    target.Placement!.XMm * _scale,
                    target.Placement.YMm * _scale,
                    projection.WidthMm * _scale,
                    projection.HeightMm * _scale);
            }).Aggregate((first, second) => Rect.Union(first, second));
            var color = TryColor(section.ColorHex, Colors.DarkSlateGray);
            var frame = new Border
            {
                Width = bounds.Width + 14d,
                Height = bounds.Height + 14d,
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(2.5d),
                CornerRadius = new CornerRadius(5d),
                Background = new SolidColorBrush(Color.FromArgb(12, color.R, color.G, color.B)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(frame, -20);
            Canvas.SetLeft(frame, Math.Max(0d, bounds.Left - 7d));
            Canvas.SetTop(frame, Math.Max(0d, bounds.Top - 7d));
            _canvas.Children.Add(frame);

            var caption = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(section.Function)
                    ? section.Name
                    : $"{section.Name}｜{section.Function}",
                FontSize = 11d,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                Background = Brushes.White,
                Padding = new Thickness(5d, 2d, 5d, 2d),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(caption, 25);
            Canvas.SetLeft(caption, Math.Max(0d, bounds.Left));
            Canvas.SetTop(caption, Math.Max(0d, bounds.Top - 25d));
            _canvas.Children.Add(caption);
        }
    }

    private LayoutTarget? ResolveAnyObject(ElectricalProject project, string objectId) =>
        ResolveObject(project, objectId, LayoutObjectKind.Component) ??
        ResolveObject(project, objectId, LayoutObjectKind.TerminalBlock);

    private static Color TryColor(string? text, Color fallback)
    {
        try
        {
            return ColorConverter.ConvertFromString(text) is Color color ? color : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record SectionColorChoice(string Hex, string Label);
}
