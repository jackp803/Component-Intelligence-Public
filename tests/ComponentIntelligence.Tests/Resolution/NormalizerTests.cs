using ComponentIntelligence.Resolution;
using Xunit;
namespace ComponentIntelligence.Tests.Resolution;
public sealed class NormalizerTests
{
    [Fact]
    public void Manufacturer_NormalizesCaseAndWhitespace()
        => Assert.Equal("IFM", ManufacturerNormalizer.NormalizeKey("  ifm  "));

    [Fact]
    public void Model_PreservesPunctuation()
    {
        var value = ModelNormalizer.Normalize(" O5D-100 ");
        Assert.NotNull(value);
        Assert.Equal("O5D-100", value!.Canonical);
    }
}
