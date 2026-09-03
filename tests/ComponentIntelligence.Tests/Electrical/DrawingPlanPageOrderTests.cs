using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPlanPageOrderTests
{
    [Fact]
    public void MovePage_CannotCrossLockedPageOrderBoundary()
    {
        var service = new DrawingPlanEditService();
        var plan = Plan(
            Page("P0", 0),
            Page("P1", 1, DrawingPlanControlState.Locked),
            Page("P2", 2));

        var error = Assert.Throws<InvalidOperationException>(() => service.MovePage(plan, "P0", 2));
        Assert.Contains("locked page order", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MovePage_WithinSameUnlockedSegment_PreservesLockedIndex()
    {
        var service = new DrawingPlanEditService();
        var plan = Plan(
            Page("P0", 0),
            Page("P1", 1),
            Page("P2", 2, DrawingPlanControlState.Locked),
            Page("P3", 3));

        var moved = service.MovePage(plan, "P0", 1);
        var ordered = moved.Pages.OrderBy(x => x.Order).ToArray();
        Assert.Equal(new[] { "P1", "P0", "P2", "P3" }, ordered.Select(x => x.PageId));
        Assert.Equal(2, ordered.Single(x => x.PageId == "P2").Order);
        Assert.Equal(DrawingPlanControlState.Locked, ordered.Single(x => x.PageId == "P2").OrderState);
    }

    private static DrawingPlanDocument Plan(params DrawingPlanPage[] pages) => new()
    {
        ProjectId = "PAGE-ORDER-TEST",
        SourcePlanningInputHash = new string('A', 64),
        SourcePagePlanHash = new string('B', 64),
        Pages = pages
    };

    private static DrawingPlanPage Page(string id, int order, DrawingPlanControlState state = DrawingPlanControlState.Auto) => new()
    {
        PageId = id,
        Archetype = "Generic",
        Order = order,
        OrderState = state,
        Bounds = new DrawingBounds(0, 0, 1000, 700)
    };
}
