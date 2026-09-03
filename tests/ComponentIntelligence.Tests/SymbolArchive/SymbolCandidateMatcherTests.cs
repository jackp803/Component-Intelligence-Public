using ComponentIntelligence.Contracts;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class SymbolCandidateMatcherTests
{
    [Fact]
    public void Rank_UsesWeakSignalsOnlyAsDeterministicSuggestions()
    {
        var candidate = Candidate("IFM/AL1342.dwg") with
        {
            DeepMetadata = new BlockDeepInspectionMetadata
            {
                BlockNames = ["IFM_AL1342"], TextLabels = ["AL1342"], Attributes = [new("MANUFACTURER", "IFM")]
            }
        };
        var matches = new SymbolCandidateMatcher().Rank(candidate, [Component("C-IFM-AL1342", "IFM", "AL1342")]);
        var match = Assert.Single(matches);
        Assert.Equal("C-IFM-AL1342", match.ComponentId);
        Assert.True(match.Score > 0);
        Assert.DoesNotContain(typeof(BlockArchiveCandidate).GetProperties(), property => property.Name is "ComponentId" or "Role" or "Status");
    }

    [Fact]
    public void EqualScores_RemainMultipleAndTieBreakByComponentId()
    {
        var candidate = Candidate("folder/MODEL-X.dwg");
        var matches = new SymbolCandidateMatcher().Rank(candidate,
            [Component("B", "M", "MODEL-X"), Component("A", "M", "MODEL-X")]);
        Assert.Equal(2, matches.Count);
        Assert.Equal(matches[0].Score, matches[1].Score);
        Assert.Equal(new[] { "A", "B" }, matches.Select(match => match.ComponentId));
    }

    private static BlockArchiveCandidate Candidate(string relative) => new()
    {
        SourcePath = Path.GetFullPath(relative), RelativePath = relative, FileName = Path.GetFileName(relative),
        Extension = ".dwg", FileSize = 1, ModifiedAt = DateTimeOffset.UnixEpoch, Sha256 = new string('a', 64)
    };
    internal static ComponentIR Component(string id, string manufacturer, string model) => new()
    {
        Identity = new ComponentIrIdentity { ComponentId = id, Manufacturer = manufacturer, Model = model }
    };
}
