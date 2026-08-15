namespace ComponentIntelligence.Bom;

public static class BomQuantityCalculator
{
    public static int? CalculateSpareQuantity(int? used, int? total)
    {
        if (used is null || total is null || used < 0 || total < 0 || total < used)
            return null;
        return total - used;
    }
}
