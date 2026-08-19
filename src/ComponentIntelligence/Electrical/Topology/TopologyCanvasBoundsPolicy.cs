namespace ComponentIntelligence.Electrical.Topology;

public sealed record TopologyCanvasExpansion(
    double ShiftX,
    double ShiftY,
    double Width,
    double Height);

/// <summary>
/// Calculates deterministic canvas growth. Right/bottom growth extends the current coordinate
/// space; left/top growth inserts new space before the origin and therefore returns the coordinate
/// shift that must be applied to every placed object.
/// </summary>
public static class TopologyCanvasBoundsPolicy
{
    public static TopologyCanvasExpansion Calculate(
        double currentWidth,
        double currentHeight,
        double desiredLeft,
        double desiredTop,
        double desiredRight,
        double desiredBottom,
        double edgeTrigger = 48d,
        double expansionChunk = 800d)
    {
        if (currentWidth <= 0d) throw new ArgumentOutOfRangeException(nameof(currentWidth));
        if (currentHeight <= 0d) throw new ArgumentOutOfRangeException(nameof(currentHeight));
        if (edgeTrigger < 0d) throw new ArgumentOutOfRangeException(nameof(edgeTrigger));
        if (expansionChunk <= 0d) throw new ArgumentOutOfRangeException(nameof(expansionChunk));

        var shiftX = RequiredLeadingGrowth(desiredLeft, edgeTrigger, expansionChunk);
        var shiftY = RequiredLeadingGrowth(desiredTop, edgeTrigger, expansionChunk);
        var widthAfterLeadingGrowth = currentWidth + shiftX;
        var heightAfterLeadingGrowth = currentHeight + shiftY;
        var shiftedRight = desiredRight + shiftX;
        var shiftedBottom = desiredBottom + shiftY;
        var trailingGrowthX = RequiredTrailingGrowth(
            shiftedRight,
            widthAfterLeadingGrowth,
            edgeTrigger,
            expansionChunk);
        var trailingGrowthY = RequiredTrailingGrowth(
            shiftedBottom,
            heightAfterLeadingGrowth,
            edgeTrigger,
            expansionChunk);

        return new TopologyCanvasExpansion(
            shiftX,
            shiftY,
            widthAfterLeadingGrowth + trailingGrowthX,
            heightAfterLeadingGrowth + trailingGrowthY);
    }

    private static double RequiredLeadingGrowth(double desiredStart, double trigger, double chunk)
    {
        if (desiredStart >= trigger) return 0d;
        return Math.Ceiling((trigger - desiredStart) / chunk) * chunk;
    }

    private static double RequiredTrailingGrowth(double desiredEnd, double currentExtent, double trigger, double chunk)
    {
        if (desiredEnd <= currentExtent - trigger) return 0d;
        return Math.Ceiling((desiredEnd + trigger - currentExtent) / chunk) * chunk;
    }
}
