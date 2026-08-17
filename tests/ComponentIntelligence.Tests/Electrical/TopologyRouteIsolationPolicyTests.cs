using ComponentIntelligence.Electrical.Topology;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class TopologyRouteIsolationPolicyTests
{
    [Fact]
    public void OrdinaryRefresh_WithUnchangedEndpoints_PreservesExistingRoute()
    {
        var decision = TopologyRouteIsolationPolicy.Decide(
            forceGlobalReroute: false,
            hasCachedRoute: true,
            manualWaypointChanged: false,
            cachedStartX: 100,
            cachedStartY: 100,
            cachedEndX: 500,
            cachedEndY: 200,
            currentStartX: 100,
            currentStartY: 100,
            currentEndX: 500,
            currentEndY: 200);

        Assert.Equal(TopologyRouteIsolationAction.Preserve, decision.Action);
    }

    [Fact]
    public void NewConnection_RecomputesOnlyThatRoute()
    {
        var decision = TopologyRouteIsolationPolicy.Decide(
            forceGlobalReroute: false,
            hasCachedRoute: false,
            manualWaypointChanged: false,
            cachedStartX: 0,
            cachedStartY: 0,
            cachedEndX: 0,
            cachedEndY: 0,
            currentStartX: 100,
            currentStartY: 100,
            currentEndX: 500,
            currentEndY: 200);

        Assert.Equal(TopologyRouteIsolationAction.Recompute, decision.Action);
    }

    [Fact]
    public void MovingOneEndpoint_RecomputesIncidentRoute()
    {
        var decision = TopologyRouteIsolationPolicy.Decide(
            forceGlobalReroute: false,
            hasCachedRoute: true,
            manualWaypointChanged: false,
            cachedStartX: 100,
            cachedStartY: 100,
            cachedEndX: 500,
            cachedEndY: 200,
            currentStartX: 140,
            currentStartY: 120,
            currentEndX: 500,
            currentEndY: 200);

        Assert.Equal(TopologyRouteIsolationAction.Recompute, decision.Action);
    }

    [Fact]
    public void MovingBothEndpointsBySameDelta_TranslatesWithoutChangingShape()
    {
        var decision = TopologyRouteIsolationPolicy.Decide(
            forceGlobalReroute: false,
            hasCachedRoute: true,
            manualWaypointChanged: false,
            cachedStartX: 100,
            cachedStartY: 100,
            cachedEndX: 500,
            cachedEndY: 200,
            currentStartX: 140,
            currentStartY: 130,
            currentEndX: 540,
            currentEndY: 230);

        Assert.Equal(TopologyRouteIsolationAction.Translate, decision.Action);
        Assert.Equal(40d, decision.TranslationX, 6);
        Assert.Equal(30d, decision.TranslationY, 6);
    }

    [Fact]
    public void DraggingManualBend_RecomputesSelectedRouteEvenWhenEndpointsStayPut()
    {
        var decision = TopologyRouteIsolationPolicy.Decide(
            forceGlobalReroute: false,
            hasCachedRoute: true,
            manualWaypointChanged: true,
            cachedStartX: 100,
            cachedStartY: 100,
            cachedEndX: 500,
            cachedEndY: 200,
            currentStartX: 100,
            currentStartY: 100,
            currentEndX: 500,
            currentEndY: 200);

        Assert.Equal(TopologyRouteIsolationAction.Recompute, decision.Action);
    }

    [Fact]
    public void ExplicitAutoLayout_AllowsGlobalReroute()
    {
        var decision = TopologyRouteIsolationPolicy.Decide(
            forceGlobalReroute: true,
            hasCachedRoute: true,
            manualWaypointChanged: false,
            cachedStartX: 100,
            cachedStartY: 100,
            cachedEndX: 500,
            cachedEndY: 200,
            currentStartX: 100,
            currentStartY: 100,
            currentEndX: 500,
            currentEndY: 200);

        Assert.Equal(TopologyRouteIsolationAction.Recompute, decision.Action);
    }
}
