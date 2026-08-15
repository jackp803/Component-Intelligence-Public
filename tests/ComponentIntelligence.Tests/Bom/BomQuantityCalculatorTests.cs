using ComponentIntelligence.Bom;
using Xunit;
namespace ComponentIntelligence.Tests.Bom;
public sealed class BomQuantityCalculatorTests
{
    [Theory]
    [InlineData(4,5,1)]
    [InlineData(5,5,0)]
    public void ValidQuantities_ReturnSpare(int used,int total,int expected)
        => Assert.Equal(expected, BomQuantityCalculator.CalculateSpareQuantity(used,total));

    [Theory]
    [InlineData(5,4)]
    [InlineData(-1,4)]
    public void InvalidQuantities_ReturnNull(int used,int total)
        => Assert.Null(BomQuantityCalculator.CalculateSpareQuantity(used,total));
}
