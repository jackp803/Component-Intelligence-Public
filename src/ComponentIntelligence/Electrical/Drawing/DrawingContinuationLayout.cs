namespace ComponentIntelligence.Electrical.Drawing;

public sealed record DrawingContinuationPlacement(
    DrawingContinuationLabel Label, DrawingBounds Box, int Index, bool FitsWithoutOverlap);

public static class DrawingContinuationLayout
{
    public const long Width = 240;

    public static IReadOnlyList<DrawingContinuationPlacement> Place(
        DrawingPlanDocument plan, IReadOnlyList<DrawingContinuationLabel> labels, string pageId)
    {
        var page = plan.Pages.Single(p => p.PageId == pageId);
        var bounds = page.Bounds;
        var obstacles = plan.Placements.Where(p => p.PageId == pageId)
            .Select(p => new DrawingBounds(p.X - 6, p.Y - 6, p.Width + 12, p.Height + 12)).ToList();
        var result = new List<DrawingContinuationPlacement>();
        foreach (var label in labels.Where(l => l.PageId == pageId)
                     .OrderBy(l => l.Anchor.Y).ThenBy(l => l.Anchor.X).ThenBy(l => l.RelationId, StringComparer.Ordinal))
        {
            var height = EstimatedHeight(label.Text);
            var preferredY = Math.Clamp(label.Anchor.Y - height / 2, bounds.Y + 8, bounds.Y + bounds.Height - height - 60);
            var right = bounds.X + bounds.Width - Width - 8;
            var left = bounds.X + 8;
            var nearRight = Math.Clamp(label.Anchor.X + 12, left, right);
            var nearLeft = Math.Clamp(label.Anchor.X - Width - 12, left, right);
            var xPositions = label.Anchor.X < bounds.X + bounds.Width / 2
                ? new[] { left, right, nearLeft, nearRight }.Distinct()
                : new[] { right, left, nearRight, nearLeft }.Distinct();
            DrawingBounds? chosen = null;
            foreach (var displacement in Enumerable.Range(0, (int)bounds.Height / 12 + 1))
            {
                foreach (var direction in displacement == 0 ? new[] { 0 } : new[] { 1, -1 })
                {
                    var y = preferredY + displacement * 12L * direction;
                    if (y < bounds.Y + 8 || y + height > bounds.Y + bounds.Height - 60) continue;
                    foreach (var x in xPositions)
                    {
                        var box = new DrawingBounds(x, y, Width, height);
                        if (obstacles.All(o => !Overlaps(box, o))) { chosen = box; break; }
                    }
                    if (chosen is not null) break;
                }
                if (chosen is not null) break;
            }
            chosen ??= new DrawingBounds(right, preferredY, Width, height);
            result.Add(new DrawingContinuationPlacement(label, chosen, result.Count + 1,
                obstacles.All(o => !Overlaps(chosen, o))));
            obstacles.Add(new DrawingBounds(chosen.X - 4, chosen.Y - 4, chosen.Width + 8, chosen.Height + 8));
        }
        return result;
    }

    public static long EstimatedHeight(string text)
    {
        var units = text.Sum(c => c >= 0x2E80 ? 2 : 1);
        return Math.Max(30, 10 + (long)Math.Ceiling(units / 37.0) * 12);
    }

    private static bool Overlaps(DrawingBounds a, DrawingBounds b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
