using ComponentIntelligence.Bom;
using Xunit;

namespace ComponentIntelligence.Tests.Bom.TaskCoverage;

public sealed class T012Tests
{
    [Theory]
    [InlineData("Manufacturer", BomHeaderMapper.Manufacturer)]
    [InlineData("Model / Part Number", BomHeaderMapper.ModelOrPartNumber)]
    [InlineData("Used Quantity", BomHeaderMapper.UsedQuantity)]
    [InlineData("Total Quantity", BomHeaderMapper.TotalQuantity)]
    [InlineData("Notes", BomHeaderMapper.Notes)]
    [InlineData("製造商", BomHeaderMapper.Manufacturer)]
    [InlineData("型號 / 料號", BomHeaderMapper.ModelOrPartNumber)]
    [InlineData("使用數量", BomHeaderMapper.UsedQuantity)]
    [InlineData("總數", BomHeaderMapper.TotalQuantity)]
    [InlineData("備註", BomHeaderMapper.Notes)]
    public void TryMap_MapsFormalEnglishHeadersAndSpecifiedChineseSynonyms(string header, string expected)
    {
        var mapped = BomHeaderMapper.TryMap(header, out var canonicalHeader);

        Assert.True(mapped);
        Assert.Equal(expected, canonicalHeader);
    }

    [Fact]
    public void TryMap_TrimsHeadersAndRejectsUnknownOrBlankValues()
    {
        Assert.True(BomHeaderMapper.TryMap("  Manufacturer  ", out var canonicalHeader));
        Assert.Equal(BomHeaderMapper.Manufacturer, canonicalHeader);
        Assert.False(BomHeaderMapper.TryMap("Vendor", out _));
        Assert.False(BomHeaderMapper.TryMap(" ", out _));
        Assert.False(BomHeaderMapper.TryMap(null, out _));
    }
}
