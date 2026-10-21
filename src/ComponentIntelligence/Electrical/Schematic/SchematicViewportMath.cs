namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicViewportMath
{
    public static double OffsetAfterZoom(double oldOffset, double pointer, double oldZoom, double newZoom) =>
        (oldOffset + pointer) * newZoom / oldZoom - pointer;

    public static double OffsetAfterPan(double startingOffset, double startingPointer, double pointer) =>
        startingOffset + startingPointer - pointer;
}
