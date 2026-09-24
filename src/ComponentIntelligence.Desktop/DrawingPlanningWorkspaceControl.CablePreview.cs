using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Desktop;

public partial class DrawingPlanningWorkspaceControl
{
    private FrameworkElement? CablePreview(DrawingPlacement placement, ElectricalProject? project, string label)
    {
        if (project is null) return null;
        var cable = project.Cables.SingleOrDefault(c => $"REP:{c.CableInstanceId}:CableDetail" == placement.RepresentationId);
        if (cable is null) return null;
        var multi = project.CableAssemblies.SingleOrDefault(a => a.PhysicalTopology?.CableInstanceId == cable.CableInstanceId);
        var canvas = new Canvas { Width = placement.Width - 12, Height = placement.Height - 12 };
        var width = canvas.Width;
        void Text(double x, double y, double w, string value, int size = 12)
        {
            var text = new TextBlock { Text = value, Width = w, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value };
            Canvas.SetLeft(text, x); Canvas.SetTop(text, y); canvas.Children.Add(text);
        }
        Text(4, 2, width * .64, label.Replace('\n', ' '), 14);
        Text(width * .65, 2, width * .34, $"Custom   長度：{(cable.ProvidedLengthMm is { } mm ? mm.ToString("0.##") + " mm" : "待填")}");
        if (multi?.PhysicalTopology is { } physical)
        {
            Text(4, 28, width * .35, EndpointLabel(project, physical.CommonPortId));
            var branches = physical.Branches.OrderBy(b => b.Index).ToArray();
            var centerY = 52 + Math.Max(0, branches.Length - 1) * 12;
            void Segment(double x1, double y1, double x2, double y2) => canvas.Children.Add(new Line
                { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.DimGray, StrokeThickness = 2 });
            Segment(30, centerY, width * .42, centerY);
            for (var index = 0; index < branches.Length; index++)
            {
                var branch = branches[index]; var by = 52 + index * 24;
                Segment(width * .42, centerY, width * .60, by);
                Segment(width * .60, by, width * .66, by);
                Text(width * .67, by - 8, width * .32, $"{branch.Index}: {EndpointLabel(project, branch.PortId)}");
            }
            // The physical sheath split has no electrical junction dot or endpoint.
            var mappingY = 70 + branches.Length * 24;
            Text(4, mappingY - 18, width - 8, "外皮分支（非電氣接點） / 以下為原有接線映射");
            foreach (var connection in project.Connections.Where(c => physical.ConnectionIds.Contains(c.ConnectionId)).OrderBy(c => c.ConnectionId, StringComparer.Ordinal))
            {
                Text(4, mappingY, width - 8, $"{EndpointLabel(project, connection.FromEndpointId)}  →  {EndpointLabel(project, connection.ToEndpointId)}");
                mappingY += 16;
            }
            return canvas;
        }
        var connections = project.Connections.Where(c => c.CableInstanceId == cable.CableInstanceId).ToArray();
        var first = connections.FirstOrDefault();
        Text(4, 25, width * .35, first is null ? "端 A 待確認" : EndpointLabel(project, first.FromEndpointId));
        Text(width * .65, 25, width * .34, first is null ? "端 B 待確認" : EndpointLabel(project, first.ToEndpointId));
        var sheath = new Rectangle { Width = width * .32, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1 };
        Canvas.SetLeft(sheath, width * .34); Canvas.SetTop(sheath, 44); canvas.Children.Add(sheath);
        canvas.Children.Add(new Line { X1 = 25, Y1 = 49, X2 = width * .34, Y2 = 49, Stroke = Brushes.Black });
        canvas.Children.Add(new Line { X1 = width * .66, Y1 = 49, X2 = width - 25, Y2 = 49, Stroke = Brushes.Black });
        var y = 65;
        foreach (var mapping in cable.CoreAssignments.Where(c => c.FromEndpointId is not null || c.ToEndpointId is not null).OrderBy(c => c.CoreId, StringComparer.Ordinal))
        {
            Text(4, y, width - 8, $"{EndpointLabel(project, mapping.FromEndpointId)}  →  {EndpointLabel(project, mapping.ToEndpointId)}    {mapping.Signal}");
            y += 16;
        }
        if (y == 65) Text(4, y, width - 8, "Pin / Core mapping 待確認；不推定直通或 NC。");
        return canvas;
    }

    private FrameworkElement? HeavyDutyPreview(DrawingPlacement placement, ElectricalProject? project, string label)
    {
        if (project is null || !_previewCatalog.TryGetValue(placement.RepresentationId, out var rep) || rep.HeavyDutyConnectorId is null) return null;
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = label.Replace('\n', ' '), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = "接點清單 / Preview（未推定兩側配對）", FontSize = 11, Height = 28 });
        foreach (var binding in rep.PortBindings)
            panel.Children.Add(new TextBlock { Text = EndpointLabel(project, binding.EngineeringEndpointId), Height = 20, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        return panel;
    }

    private static string EndpointLabel(ElectricalProject project, string? endpoint)
    {
        foreach (var component in project.Components)
        foreach (var port in component.Ports)
        {
            var pin = port.Pins.SingleOrDefault(p => p.PinId == endpoint);
            if (pin is null && port.PortId != endpoint) continue;
            var name = component.ReferenceDesignator ?? component.DisplayName ?? "元件名稱待確認";
            return $"{name} / {port.Name}" + (pin is null ? "" : $" / Pin {pin.PinNumber}");
        }
        return "端點待確認";
    }
}
