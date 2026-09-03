using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class RepresentationPolicyTests
{
    [Fact]
    public void ExistingAsset_DoesNotForceArchivedExactWhenExplicitContextPrefersImageModule()
    {
        var policy = new RepresentationPolicy(new FakeAssets());
        var result = policy.Decide(Request(DrawingRepresentationFamily.ImageModule));
        Assert.Equal(DrawingRepresentationFamily.ImageModule, result.Decision.Family);
        Assert.Null(result.Decision.AssetPath);
    }

    [Fact]
    public void ConnectorDetail_MissingRequiredEndpointEvidence_BlocksWithoutInventingPins()
    {
        var policy = new RepresentationPolicy(new FakeAssets());
        var result = policy.Decide(Request(DrawingRepresentationFamily.ConnectorDetail) with
        {
            Role = DrawingRepresentationRole.ConnectorDetail,
            RequiresExplicitEndpointEvidence = true,
            PortBindings = []
        });
        Assert.Contains(result.Issues, i => i.Code == "DRAWING_REQUIRED_ENGINEERING_EVIDENCE_MISSING" && i.Severity == DrawingPlanningIssueSeverity.Blocker);
        Assert.Empty(result.Decision.PortBindings);
    }

    [Theory]
    [InlineData(DrawingRepresentationControlState.ManualOverride)]
    [InlineData(DrawingRepresentationControlState.Locked)]
    public void ManualOrLockedLegalChoice_IsPreserved(DrawingRepresentationControlState state)
    {
        var result = new RepresentationPolicy(new FakeAssets()).Decide(Request(DrawingRepresentationFamily.FunctionalGeneric) with { ControlState = state });
        Assert.Equal(state, result.Decision.ControlState);
        Assert.Equal(DrawingRepresentationFamily.FunctionalGeneric, result.Decision.Family);
    }

    private static RepresentationRequest Request(DrawingRepresentationFamily family) => new()
    {
        RepresentationId = "REP-1", OwnerKind = DrawingRepresentationOwnerKind.Component, OwnerId = "INST-1",
        AssetComponentId = "DEF-1", Role = DrawingRepresentationRole.Schematic, PreferredFamily = family,
        ControlState = DrawingRepresentationControlState.Auto, AllowedRotations = [0], PortBindings = [],
        PhysicalInterfaceMeaning = false
    };

    private sealed class FakeAssets : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => new()
        {
            SourceType = "Manufacturer", Revision = "rev-001", AssetPath = "Documents/a.dwg",
            AssetHashSha256 = new string('A', 64), PortBindings = []
        };
    }
}
