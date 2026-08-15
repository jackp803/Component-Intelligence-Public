using ComponentIntelligence.Bom;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T010Tests
{
    [Theory]
    [InlineData(4, 5, 1)]
    [InlineData(5, 5, 0)]
    public void CalculateSpareQuantity_ReturnsTotalMinusUsedForValidInputs(int used, int total, int expected)
    {
        Assert.Equal(expected, BomQuantityCalculator.CalculateSpareQuantity(used, total));
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(-1, 4)]
    [InlineData(1, -1)]
    public void CalculateSpareQuantity_ReturnsNullForInvalidInputs(int used, int total)
    {
        Assert.Null(BomQuantityCalculator.CalculateSpareQuantity(used, total));
    }

    [Fact]
    public void CalculateSpareQuantity_ReturnsNullWhenEitherInputIsNull()
    {
        Assert.Null(BomQuantityCalculator.CalculateSpareQuantity(null, 5));
        Assert.Null(BomQuantityCalculator.CalculateSpareQuantity(5, null));
        Assert.Null(BomQuantityCalculator.CalculateSpareQuantity(null, null));
    }

    [Fact]
    public void CalculateSpareQuantity_DoesNotAlterCallerValues()
    {
        int? used = 4;
        int? total = 5;
        var spare = BomQuantityCalculator.CalculateSpareQuantity(used, total);
        Assert.Equal(1, spare);
        Assert.Equal(4, used);
        Assert.Equal(5, total);
    }
}
