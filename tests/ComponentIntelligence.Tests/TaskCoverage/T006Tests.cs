using System.Reflection;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage;

public sealed class T006Tests
{
    [Fact]
    public void RawSpecification_PreservesRawValueSeparatelyFromProposedKey()
    {
        var evidence = new Evidence
        {
            SourceType = default,
            ExtractionMethod = default,
            RetrievedAt = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
            VerificationStatus = default,
            RawValue = " 10 kOhm ±5% "
        };
        var specification = new RawSpecification
        {
            RawName = "Resistance",
            RawValue = " 10 kOhm ±5% ",
            ProposedKey = "resistance",
            Evidence = [evidence]
        };

        Assert.Equal("Resistance", specification.RawName);
        Assert.Equal(" 10 kOhm ±5% ", specification.RawValue);
        Assert.Equal("resistance", specification.ProposedKey);
        Assert.Single(specification.Evidence);
        Assert.Same(evidence, specification.Evidence[0]);
        Assert.Null(typeof(RawSpecification).GetProperty("NormalizedValue", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void RawSpecification_DefaultsEvidenceToAnEmptyList()
    {
        var specification = new RawSpecification { RawName = "Operating temperature" };
        Assert.Null(specification.RawValue);
        Assert.Null(specification.ProposedKey);
        Assert.Empty(specification.Evidence);
    }
}
