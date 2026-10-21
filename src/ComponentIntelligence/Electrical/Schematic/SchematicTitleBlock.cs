using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicTitleBlockSettings
{
    public string? ProjectName { get; init; }
    public string? DrawingNumber { get; init; }
    public string? DrawnBy { get; init; }
    public string? CheckedBy { get; init; }
    public string? Revision { get; init; }
}

public sealed record SchematicTitleBlockSlot(string Field, SchematicGridBounds Bounds);

public static class SchematicTitleBlock
{
    private static readonly (string Label, string Field)[] Labels =
    [
        ("TITLE:", "SheetContent"), ("SHEET", "SheetNumber"),
        ("NAME(INTENDED USE)", "ProjectName"), ("DWG NO.:", "DrawingNumber"),
        ("DRI:", "DrawnBy"), ("CHKD:", "CheckedBy"), ("REV.", "Revision")
    ];

    public static IReadOnlyList<SchematicTitleBlockSlot> DetectSlots(SchematicCadAsset asset)
    {
        var lines = asset.Primitives.Where(p => p.Kind == "LINE" && p.End is not null).ToArray();
        var slots = new List<SchematicTitleBlockSlot>();
        foreach (var (label, field) in Labels)
        {
            var text = asset.Primitives.Where(p => (p.Kind is "TEXT" or "MTEXT") &&
                p.Start.Y > asset.Height * .65 &&
                string.Equals(p.Text?.Trim(), label, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Start.X).ThenByDescending(p => p.Start.Y).FirstOrDefault();
            if (text is null) continue;
            var inline = field is "DrawnBy" or "CheckedBy";
            var x = inline ? text.Start.X + label.Length * text.TextHeight * .62 + 1.5 : text.Start.X + 1;
            var y = inline ? text.Start.Y - .2 : text.Start.Y + Math.Max(1.8, text.TextHeight * .9);
            var right = lines.Where(p => p.Start.X > x + 4 && Math.Abs(p.Start.X - p.End!.X) < .1 &&
                Math.Min(p.Start.Y, p.End.Y) <= y && Math.Max(p.Start.Y, p.End.Y) >= y)
                .Select(p => p.Start.X).DefaultIfEmpty(asset.Width).Min();
            var bottom = lines.Where(p => p.Start.Y > y + 2 && Math.Abs(p.Start.Y - p.End!.Y) < .1 &&
                Math.Min(p.Start.X, p.End.X) <= x && Math.Max(p.Start.X, p.End.X) >= x)
                .Select(p => p.Start.Y).DefaultIfEmpty(Math.Min(asset.Height, y + 8)).Min();
            var width = Math.Min(asset.Width - x, right - x - 1);
            var height = Math.Min(asset.Height - y, bottom - y - .5);
            if (width < 4 || height < 2) continue;
            slots.Add(new(field, new(x, y, width, height)));
        }
        return slots;
    }

    public static string SuggestPageContent(ElectricalProject project, SchematicPage page)
    {
        var doc = project.Schematic;
        if (doc is null) return page.Title;
        var main = doc.Symbols.Where(s => s.PageId == page.PageId && s.ComponentInstanceId.Length > 0)
            .GroupBy(s => s.ComponentInstanceId, StringComparer.Ordinal)
            .Select(group =>
            {
                var component = project.Components.FirstOrDefault(c => c.ComponentInstanceId == group.Key);
                return new { Component = component, PinCount = group.Sum(s => s.Anchors.Count),
                    Wires = doc.Wires.Count(w => group.Any(s => w.Start.SymbolId == s.SymbolId || w.End.SymbolId == s.SymbolId)) };
            })
            .Where(item => item.Component is not null)
            .OrderByDescending(item => item.Wires).ThenByDescending(item => item.PinCount)
            .Take(2)
            .Select(item => item.Component!.ReferenceDesignator is { Length: > 0 } reference
                ? reference + " " + (item.Component.DisplayName ?? item.Component.TypeKey)
                : item.Component.DisplayName ?? item.Component.TypeKey)
            .ToArray();
        return main.Length == 0 ? page.Title : string.Join(" / ", main);
    }

    public static string Value(ElectricalProject project, SchematicPage page, string field)
    {
        var settings = project.Schematic?.TitleBlock ?? new();
        return field switch
        {
            "SheetContent" => page.TitleBlockContentOverride ?? SuggestPageContent(project, page),
            "SheetNumber" => project.Schematic is { } doc
                ? $"{doc.Pages.FindIndex(p => p.PageId == page.PageId) + 1}/{doc.Pages.Count}" : "",
            "ProjectName" => settings.ProjectName ?? project.Name ?? "",
            "DrawingNumber" => settings.DrawingNumber ?? "",
            "DrawnBy" => settings.DrawnBy ?? "",
            "CheckedBy" => settings.CheckedBy ?? "",
            "Revision" => settings.Revision ?? "",
            _ => ""
        };
    }
}
