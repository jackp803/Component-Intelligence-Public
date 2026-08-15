using System.Text.Json;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage;

public sealed class T005Tests
{
    [Fact]
    public void Evidence_StoresAllProvenanceFieldsDeterministically()
    {
        var retrievedAt = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var evidence = new Evidence
        {
            SourceType = default,
            SourceUrl = new Uri("https://example.test/product/AX-100"),
            DocumentUrl = new Uri("https://example.test/datasheets/AX-100.pdf"),
            DocumentHashSha256 = "0123456789abcdef",
            PageNumber = 7,
            ExtractionMethod = default,
            RawValue = "AX-100",
            RetrievedAt = retrievedAt,
            VerificationStatus = default
        };

        Assert.Equal(default(ComponentSourceType), evidence.SourceType);
        Assert.Equal(new Uri("https://example.test/product/AX-100"), evidence.SourceUrl);
        Assert.Equal(new Uri("https://example.test/datasheets/AX-100.pdf"), evidence.DocumentUrl);
        Assert.Equal("0123456789abcdef", evidence.DocumentHashSha256);
        Assert.Equal(7, evidence.PageNumber);
        Assert.Equal(default(ExtractionMethod), evidence.ExtractionMethod);
        Assert.Equal("AX-100", evidence.RawValue);
        Assert.Equal(retrievedAt, evidence.RetrievedAt);
        Assert.Equal(default(VerificationStatus), evidence.VerificationStatus);
        Assert.NotEmpty(Enum.GetValues<ComponentSourceType>());
        Assert.NotEmpty(Enum.GetValues<ExtractionMethod>());
    }

    [Fact]
    public void Evidence_SerializesSourceDocumentExtractionAndVerificationFields()
    {
        var evidence = new Evidence
        {
            SourceType = default,
            SourceUrl = new Uri("https://example.test/product/AX-100"),
            DocumentUrl = new Uri("https://example.test/datasheets/AX-100.pdf"),
            DocumentHashSha256 = "abc123",
            PageNumber = 3,
            ExtractionMethod = default,
            RawValue = "10 kOhm",
            RetrievedAt = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            VerificationStatus = default
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(evidence));
        Assert.Equal("https://example.test/product/AX-100", document.RootElement.GetProperty("SourceUrl").GetString());
        Assert.Equal("https://example.test/datasheets/AX-100.pdf", document.RootElement.GetProperty("DocumentUrl").GetString());
        Assert.Equal("abc123", document.RootElement.GetProperty("DocumentHashSha256").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("PageNumber").GetInt32());
        Assert.Equal("10 kOhm", document.RootElement.GetProperty("RawValue").GetString());
        Assert.True(document.RootElement.TryGetProperty("SourceType", out _));
        Assert.True(document.RootElement.TryGetProperty("ExtractionMethod", out _));
        Assert.True(document.RootElement.TryGetProperty("VerificationStatus", out _));
    }
}
