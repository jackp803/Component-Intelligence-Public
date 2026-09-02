using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private const string TerminalGroupVisualPrefix = "CI-TERMINAL-GROUP:";
    private readonly TopologyTerminalGroupingPolicy _terminalGroupingPolicy = new();

    private void ApplyTerminalComponentGroupingVisuals()
    {
        if (_project is null) return;

        foreach (var visual in Surface.Children.OfType<FrameworkElement>()
                     .Where(element => element.Tag is string tag &&
                         tag.StartsWith(TerminalGroupVisualPrefix, StringComparison.Ordinal))
                     .ToArray())
            Surface.Children.Remove(visual);

        var terminalComponents = _project.Components
            .Where(IsCompactTerminalComponent)
            .ToDictionary(component => component.ComponentInstanceId, StringComparer.OrdinalIgnoreCase);
        foreach (var component in terminalComponents.Values)
            ResetTerminalMemberVisual(component);

        var groupIndex = 0;
        foreach (var group in _terminalGroupingPolicy.BuildGroups(_project))
        {
            var members = group.ComponentInstanceIds
                .Where(terminalComponents.ContainsKey)
                .Select(id => terminalComponents[id])
                .ToArray();
            if (members.Length < 2) continue;

            foreach (var component in members)
                CompactGroupedTerminalMemberVisual(component);

            var groupId = TerminalGroupVisualPrefix + groupIndex++;
            var frame = new Border
            {
                Tag = groupId,
                Width = group.Bounds.Width + 10d,
                Height = group.Bounds.Height + 10d,
                BorderBrush = Brushes.DarkSlateGray,
                BorderThickness = new Thickness(2d),
                CornerRadius = new CornerRadius(4d),
                Background = new SolidColorBrush(Color.FromArgb(22, 47, 79, 79)),
                IsHitTestVisible = false,
                ToolTip = $"自動合併端子排：{members.Length} 顆端子（資料與接線仍各自獨立）"
            };
            Panel.SetZIndex(frame, -20);
            Canvas.SetLeft(frame, Math.Max(0d, group.Bounds.X - 5d));
            Canvas.SetTop(frame, Math.Max(0d, group.Bounds.Y - 5d));
            Surface.Children.Add(frame);

            var title = new TextBlock
            {
                Tag = groupId + ":TITLE",
                Text = BuildTerminalGroupTitle(members),
                FontSize = 10d,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray,
                Background = Brushes.White,
                Padding = new Thickness(4d, 1d, 4d, 1d),
                IsHitTestVisible = false,
                ToolTip = "相鄰端子已自動顯示為一組；拖開後會自動拆組。"
            };
            Panel.SetZIndex(title, 20);
            Canvas.SetLeft(title, Math.Max(0d, group.Bounds.X + 4d));
            Canvas.SetTop(title, Math.Max(0d, group.Bounds.Y - 20d));
            Surface.Children.Add(title);
        }
    }

    private void ResetTerminalMemberVisual(ComponentInstance component)
    {
        var border = FindTopologyNodeVisual(component.ComponentInstanceId);
        if (border is null) return;

        border.BorderBrush = Brushes.DimGray;
        border.BorderThickness = new Thickness(1.5d);
        border.CornerRadius = new CornerRadius(5d);
        border.Background = Brushes.WhiteSmoke;
        if (border.Child is not StackPanel panel) return;

        var labels = panel.Children.OfType<TextBlock>().ToArray();
        var main = labels.FirstOrDefault(label => label.FontWeight == FontWeights.SemiBold);
        if (main is not null) main.Text = FullTerminalLabel(component);
        var subtitle = labels.LastOrDefault(label =>
            label.Text.Contains("Component", StringComparison.OrdinalIgnoreCase));
        if (subtitle is not null) subtitle.Visibility = Visibility.Visible;
    }

    private void CompactGroupedTerminalMemberVisual(ComponentInstance component)
    {
        var border = FindTopologyNodeVisual(component.ComponentInstanceId);
        if (border is null) return;

        border.BorderBrush = Brushes.DarkSlateGray;
        border.BorderThickness = new Thickness(0.75d);
        border.CornerRadius = new CornerRadius(0d);
        border.Background = new SolidColorBrush(Color.FromRgb(248, 250, 250));
        if (border.Child is not StackPanel panel) return;

        var labels = panel.Children.OfType<TextBlock>().ToArray();
        var main = labels.FirstOrDefault(label => label.FontWeight == FontWeights.SemiBold);
        if (main is not null) main.Text = ShortTerminalMemberLabel(component);
        var subtitle = labels.LastOrDefault(label =>
            label.Text.Contains("Component", StringComparison.OrdinalIgnoreCase));
        if (subtitle is not null) subtitle.Visibility = Visibility.Collapsed;
    }

    private static string BuildTerminalGroupTitle(IReadOnlyList<ComponentInstance> members)
    {
        var labels = members.Select(FullTerminalLabel).ToArray();
        var bases = labels.Select(RemoveTerminalSequenceSuffix).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var family = bases.Length == 1 && !string.IsNullOrWhiteSpace(bases[0])
            ? bases[0]
            : "Terminal strip｜端子排";
        return $"{family} × {members.Count}";
    }

    private static string ShortTerminalMemberLabel(ComponentInstance component)
    {
        var full = FullTerminalLabel(component);
        var match = Regex.Match(full, @"(?:#\s*\d+|:\s*\d+)\s*$", RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : full;
    }

    private static string RemoveTerminalSequenceSuffix(string label) =>
        Regex.Replace(label, @"\s*(?:#\s*\d+|:\s*\d+)\s*$", string.Empty, RegexOptions.CultureInvariant).Trim();

    private static string FullTerminalLabel(ComponentInstance component) =>
        component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId;
}
