using ComponentIntelligence.Electrical.Export;

namespace ComponentIntelligence.Tests.Electrical.PowerTopology;

internal static class PowerEndpointCoverageTestExtensions
{
    // Avoid the array -> Span<T> MemoryExtensions.Reverse overload (void) so the permutation
    // regression can continue as an IEnumerable pipeline on the CI compiler/runtime.
    public static IEnumerable<AutocadStagingRoute> Reverse(this AutocadStagingRoute[] items) =>
        Enumerable.Reverse(items);
}
