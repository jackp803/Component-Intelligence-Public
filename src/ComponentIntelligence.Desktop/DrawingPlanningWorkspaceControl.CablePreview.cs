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
        if (multi is not null) return null; // Preserve multi-end geometry until its dedicated renderer is integrated.
        var canvas = new Canvas { Width = placement.Width - 12, Height = placement.Height - 12 };
        var width = canvas.Width;
        void Text(double x, double y, double w, string value, int size = 12)
        {
            var text = new TextBlock { Text = value, Width = w, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value };
            Canvas.SetLeft(text, x); Canvas.SetTop(text, y); canvas.Children.Add(text);
        }
        Text(4, 2, width * .64, label.Replace('\n', ' '), 14);
        Text(width * .65, 2, width * .34, $"Custom   長度：{(cable.ProvidedLengthMm is { } mm ? mm.ToString("0.##") + " mm" : "待填")}");
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
