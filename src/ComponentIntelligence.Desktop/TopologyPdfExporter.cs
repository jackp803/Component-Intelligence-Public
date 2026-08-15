using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ComponentIntelligence.Desktop;

public sealed class TopologyPdfExporter
{
    private readonly TopologyProjection _projection = new();

    public void Export(ElectricalProject project, string filePath, ElectricalLayer? layerFilter = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _projection.EnsurePlacements(project);
        var graph = _projection.Build(project, layerFilter);
        if (graph.Nodes.Count == 0)
            throw new InvalidOperationException("Topology canvas has no visible nodes to export.");

        using var document = new PdfDocument();
        document.Info.Title = string.IsNullOrWhiteSpace(project.Name) ? "Electrical Topology" : project.Name;
        document.Info.Subject = "Component Intelligence Electrical Topology";
        document.Info.Creator = "Component Intelligence";

        var page = document.AddPage();
        page.Size = PageSize.A3;
        page.Orientation = PageOrientation.Landscape;
        using var gfx = XGraphics.FromPdfPage(page);

        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;
        const double margin = 34;
        const double headerHeight = 42;

        var minX = graph.Nodes.Min(node => node.Placement.X);
        var minY = graph.Nodes.Min(node => node.Placement.Y);
        var maxX = graph.Nodes.Max(node => node.Placement.X + node.Placement.Width);
        var maxY = graph.Nodes.Max(node => node.Placement.Y + node.Placement.Height);
        var contentWidth = Math.Max(1, maxX - minX);
        var contentHeight = Math.Max(1, maxY - minY);
        var availableWidth = pageWidth - margin * 2;
        var availableHeight = pageHeight - margin * 2 - headerHeight;
        var scale = Math.Min(availableWidth / contentWidth, availableHeight / contentHeight);
        scale = Math.Min(scale, 2.2);

        var font = CreateFont(9, XFontStyleEx.Regular);
        var bold = CreateFont(10, XFontStyleEx.Bold);
        var titleFont = CreateFont(15, XFontStyleEx.Bold);
        var small = CreateFont(7.5, XFontStyleEx.Regular);

        gfx.DrawString(
            string.IsNullOrWhiteSpace(project.Name) ? "Electrical Topology" : project.Name,
            titleFont,
            XBrushes.Black,
            new XRect(margin, margin - 8, availableWidth, 24),
            XStringFormats.TopLeft);
        gfx.DrawString(
            $"Project: {project.ProjectId}    Layer: {(layerFilter?.ToString() ?? "All")}    Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
            small,
            XBrushes.DimGray,
            new XRect(margin, margin + 16, availableWidth, 18),
            XStringFormats.TopLeft);

        double Tx(double x) => margin + (x - minX) * scale;
        double Ty(double y) => margin + headerHeight + (y - minY) * scale;

        var nodeMap = graph.Nodes.ToDictionary(node => node.ObjectId, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in graph.Edges)
        {
            if (!nodeMap.TryGetValue(edge.FromObjectId, out var from) || !nodeMap.TryGetValue(edge.ToObjectId, out var to)) continue;
            var x1 = Tx(from.Placement.X + from.Placement.Width / 2);
            var y1 = Ty(from.Placement.Y + from.Placement.Height / 2);
            var x2 = Tx(to.Placement.X + to.Placement.Width / 2);
            var y2 = Ty(to.Placement.Y + to.Placement.Height / 2);
            gfx.DrawLine(LayerPen(edge.Layer), x1, y1, x2, y2);
            var label = edge.NetLabel ?? edge.NetId;
            if (!string.IsNullOrWhiteSpace(label))
                gfx.DrawString(label, small, XBrushes.Black, (x1 + x2) / 2 + 3, (y1 + y2) / 2 - 3);
        }

        foreach (var node in graph.Nodes)
        {
            var x = Tx(node.Placement.X);
            var y = Ty(node.Placement.Y);
            var width = Math.Max(46, node.Placement.Width * scale);
            var height = Math.Max(28, node.Placement.Height * scale);
            var rect = new XRect(x, y, width, height);
            var pen = node.ObjectKind == "TERMINAL_BLOCK" ? new XPen(XColors.DarkSlateGray, 1.5) : new XPen(XColors.DimGray, 1.1);
            gfx.DrawRoundedRectangle(pen, XBrushes.WhiteSmoke, rect, new XSize(5, 5));
            gfx.DrawString(node.Label.Replace('\n', ' '), bold, XBrushes.Black, new XRect(x + 4, y + 5, width - 8, 14), XStringFormats.TopCenter);

            var component = project.Components.FirstOrDefault(item => string.Equals(item.ComponentInstanceId, node.ObjectId, StringComparison.OrdinalIgnoreCase));
            if (component is not null)
            {
                var ports = component.Ports.Take(8).ToArray();
                if (ports.Length > 0)
                {
                    var portText = string.Join("  ", ports.Select(port => $"{port.Name}:{port.Connector?.Family ?? port.Protocol ?? "?"}"));
                    gfx.DrawString(portText, small, XBrushes.DimGray, new XRect(x + 4, y + height - 13, width - 8, 10), XStringFormats.TopCenter);
                }
            }
        }

        document.Save(filePath);
    }

    private static XFont CreateFont(double size, XFontStyleEx style)
    {
        try { return new XFont("Segoe UI", size, style); }
        catch { return new XFont("Arial", size, style); }
    }

    private static XPen LayerPen(ElectricalLayer layer) => layer switch
    {
        ElectricalLayer.Power => new XPen(XColors.Firebrick, 1.6),
        ElectricalLayer.Analog => new XPen(XColors.DarkOrange, 1.6),
        ElectricalLayer.Digital => new XPen(XColors.ForestGreen, 1.6),
        ElectricalLayer.Communication => new XPen(XColors.RoyalBlue, 1.6),
        ElectricalLayer.Grounding => new XPen(XColors.SaddleBrown, 1.6),
        ElectricalLayer.Safety => new XPen(XColors.DarkViolet, 1.6),
        _ => new XPen(XColors.Gray, 1.3)
    };
}
