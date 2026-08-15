using System.Text.Json;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage;

public sealed class T003Tests
{
    [Fact]
    public void Contracts_StoreIdentityValuesDeterministically()
    {
        var query = new ComponentIdentityQuery
        {
            RawManufacturer = " ACME, Inc. ",
            RawModel = " ax - 100 ",
            NormalizedManufacturer = "acme inc",
            NormalizedModel = "ax-100",
            SearchKey = "acme-inc|ax-100"
        };
        var identity = new ComponentIdentity
        {
            OfficialManufacturer = "ACME Incorporated",
            OfficialModel = "AX-100",
            Mpn = "AX100-REV2",
            OfficialProductUrl = new Uri("https://example.test/products/AX-100")
        };

        Assert.Equal(" ACME, Inc. ", query.RawManufacturer);
        Assert.Equal(" ax - 100 ", query.RawModel);
        Assert.Equal("acme inc", query.NormalizedManufacturer);
        Assert.Equal("ax-100", query.NormalizedModel);
        Assert.Equal("acme-inc|ax-100", query.SearchKey);
        Assert.Equal("ACME Incorporated", identity.OfficialManufacturer);
        Assert.Equal("ACME Incorporated", identity.Manufacturer);
        Assert.Equal("AX-100", identity.OfficialModel);
        Assert.Equal("AX100-REV2", identity.Mpn);
        Assert.Equal(new Uri("https://example.test/products/AX-100"), identity.OfficialProductUrl);
    }

    [Fact]
    public void Contracts_SerializeAllIdentityFields()
    {
        var query = new ComponentIdentityQuery
        {
            RawManufacturer = "ACME",
            RawModel = "AX 100",
            NormalizedManufacturer = "acme",
            NormalizedModel = "ax-100",
            SearchKey = "acme|ax-100"
        };
        var identity = new ComponentIdentity
        {
            OfficialManufacturer = "ACME Incorporated",
            OfficialModel = "AX-100",
            Mpn = "AX100",
            OfficialProductUrl = new Uri("https://example.test/products/AX-100")
        };

        using var queryDocument = JsonDocument.Parse(JsonSerializer.Serialize(query));
        using var identityDocument = JsonDocument.Parse(JsonSerializer.Serialize(identity));

        Assert.Equal("ACME", queryDocument.RootElement.GetProperty("RawManufacturer").GetString());
        Assert.Equal("AX 100", queryDocument.RootElement.GetProperty("RawModel").GetString());
        Assert.Equal("acme", queryDocument.RootElement.GetProperty("NormalizedManufacturer").GetString());
        Assert.Equal("ax-100", queryDocument.RootElement.GetProperty("NormalizedModel").GetString());
        Assert.Equal("acme|ax-100", queryDocument.RootElement.GetProperty("SearchKey").GetString());
        Assert.Equal("ACME Incorporated", identityDocument.RootElement.GetProperty("OfficialManufacturer").GetString());
        Assert.Equal("AX-100", identityDocument.RootElement.GetProperty("OfficialModel").GetString());
        Assert.Equal("AX100", identityDocument.RootElement.GetProperty("Mpn").GetString());
        Assert.Equal("https://example.test/products/AX-100", identityDocument.RootElement.GetProperty("OfficialProductUrl").GetString());
    }
}
