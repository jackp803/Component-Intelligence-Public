using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingPlanningContractsTests
{
    [Fact]
    public void Enums_AreExactlyProductOwnerApprovedV1Values()
    {
        Assert.Equal(new[] { "ArchivedExact", "CableDetail", "CableFunctional", "ConnectorDetail", "FunctionalGeneric", "HeavyDuty", "ImageModule", "ParentChildElectrical", "StandardSymbol" }, Enum.GetNames<DrawingRepresentationFamily>().OrderBy(x => x));
        Assert.Equal(new[] { "CableAssembly", "CableInstance", "Component", "HeavyDutyConnector", "Network", "PowerDomain", "SeriesChain", "Terminal" }, Enum.GetNames<DrawingRepresentationOwnerKind>().OrderBy(x => x));
        Assert.Equal(new[] { "CableDetail", "CableFunctional", "ConnectorDetail", "HeavyDuty", "PanelFootprint", "ParentChildElectrical", "PowerReference", "Schematic", "TopologyVisual" }, Enum.GetNames<DrawingRepresentationRole>().OrderBy(x => x));
    }

    [Fact]
    public void Serialize_SortsNonsemanticCollectionsAndComputesUppercaseHash()
    {
        var input = TestInput();
        input.Representations.Reverse();
        var first = DrawingPlanningJson.Serialize(input);
        input.Representations.Reverse();
        var second = DrawingPlanningJson.Serialize(input);
        Assert.Equal(first, second);
        var parsed = DrawingPlanningJson.Deserialize(first);
        Assert.Matches("^[A-F0-9]{64}$", parsed.PlanningInputHash);
        Assert.Equal(new[] { "REP-A", "REP-B" }, parsed.Representations.Select(x => x.RepresentationId));
    }

    [Fact]
    public void AllowedRotations_AreExplicitAndCannotContainInventedAngle()
    {
        var input = TestInput();
        input.Representations[0] = input.Representations[0] with { AllowedRotations = [0, 45] };
        Assert.Throws<InvalidDataException>(() => DrawingPlanningJson.Serialize(input));
    }

    internal static DrawingPlanningInput TestInput() => new()
    {
        ProjectId = "P1",
        Representations =
        [
            Rep("REP-B", "C2"),
            Rep("REP-A", "C1")
        ],
        Connections = [], Cables = [], ControllerModules = [], Networks = [], SeriesChains = [],
        HeavyDutyConnectors = [], PowerDomains = [], WiringRules = [], Issues = []
    };

    private static DrawingRepresentationDecision Rep(string id, string owner) => new()
    {
        RepresentationId = id, OwnerKind = DrawingRepresentationOwnerKind.Component, OwnerId = owner,
        Role = DrawingRepresentationRole.Schematic, Family = DrawingRepresentationFamily.FunctionalGeneric,
        ControlState = DrawingRepresentationControlState.Auto, AllowedRotations = [0, 90], PortBindings = [],
        PhysicalInterfaceMeaning = false
    };
}
