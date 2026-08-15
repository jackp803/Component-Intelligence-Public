using System.Text.Json;
using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage;

public sealed class T004Tests
{
    [Fact]
    public void IdentityMatchResult_RepresentsExactStrongAmbiguousNotFoundAndConflict()
    {
        var candidate = new ComponentCandidate { Manufacturer = "ACME Incorporated", OfficialModel = "AX-100", SourceType = default };
        var identity = new ComponentIdentity { OfficialManufacturer = "ACME Incorporated", OfficialModel = "AX-100" };
        var exact = new IdentityMatchResult { Status = ResolutionStatus.Resolved, MatchLevel = MatchLevel.Exact, Identity = identity, Candidate = candidate };
        var strong = new IdentityMatchResult { Status = ResolutionStatus.Resolved, MatchLevel = MatchLevel.Strong, Identity = identity, Candidate = candidate };
        var ambiguous = new IdentityMatchResult { Status = ResolutionStatus.Ambiguous, MatchLevel = MatchLevel.Ambiguous };
        var notFound = new IdentityMatchResult { Status = ResolutionStatus.NotFound, MatchLevel = MatchLevel.None };
        var conflict = new IdentityMatchResult { Status = ResolutionStatus.Conflict, MatchLevel = MatchLevel.None };

        Assert.Equal(MatchLevel.Exact, exact.MatchLevel);
        Assert.Equal(MatchLevel.Strong, strong.MatchLevel);
        Assert.Equal(ResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(ResolutionStatus.NotFound, notFound.Status);
        Assert.Equal(ResolutionStatus.Conflict, conflict.Status);
    }

    [Fact]
    public void ResolutionContracts_SerializeCandidateAndResolutionResult()
    {
        var resolution = new ResolutionResult
        {
            Status = ResolutionStatus.Resolved,
            MatchLevel = MatchLevel.Exact,
            Input = new ComponentIdentityQuery { RawManufacturer = "ACME", RawModel = "AX 100" },
            ResolvedIdentity = new ComponentIdentity { OfficialManufacturer = "ACME Incorporated", OfficialModel = "AX-100" },
            Candidates = [new ComponentCandidate { Manufacturer = "ACME Incorporated", OfficialModel = "AX-100", SourceType = default }]
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(resolution));
        Assert.Equal("ACME", document.RootElement.GetProperty("Input").GetProperty("RawManufacturer").GetString());
        Assert.Equal("AX-100", document.RootElement.GetProperty("ResolvedIdentity").GetProperty("OfficialModel").GetString());
        Assert.Equal("ACME Incorporated", document.RootElement.GetProperty("Candidates")[0].GetProperty("Manufacturer").GetString());
    }
}
