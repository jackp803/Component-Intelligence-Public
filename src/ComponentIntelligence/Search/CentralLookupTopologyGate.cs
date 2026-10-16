namespace ComponentIntelligence.Search;

/// <summary>Controls whether an Add-to-BOM lookup may unlock Topology without a batch load.</summary>
public static class CentralLookupTopologyGate
{
    public static bool CanUnlockAfterAdd(int bomRowCount, bool wasReadyBeforeAdd, bool addedComponentCached) =>
        addedComponentCached && bomRowCount > 0 && (bomRowCount == 1 || wasReadyBeforeAdd);
}
