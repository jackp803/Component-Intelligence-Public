using ComponentIntelligence.Search;
using Xunit;

namespace ComponentIntelligence.Tests.Search;

public sealed class CentralLookupTopologyGateTests
{
    [Theory]
    [InlineData(1, false, true, true)]
    [InlineData(2, false, true, false)]
    [InlineData(2, true, true, true)]
    [InlineData(1, false, false, false)]
    public void UnlocksOnlySingleLookupOrPreviouslyReadyBom(
        int rowCount,
        bool previouslyReady,
        bool cached,
        bool expected) =>
        Assert.Equal(expected, CentralLookupTopologyGate.CanUnlockAfterAdd(rowCount, previouslyReady, cached));
}
