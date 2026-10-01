using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicCableLabelTests
{
    [Theory]
    [InlineData(false, 60, 20, 0)]
    [InlineData(true, 20, 60, -90)]
    public void AssignedModelFollowsTheLongestSegmentWithoutChangingEngineeringData(bool vertical, double x, double y, int rotation)
    {
        var project = Project();
        var wire = Wire(vertical ? [new(20, 20), new(20, 100)] : [new(20, 20), new(100, 20)]);
        var before = JsonSerializer.Serialize(project);
        var label = SchematicCableLabelPresentation.Resolve(project, wire)!;
        Assert.Equal("EVC014", label.Text);
        Assert.Equal(new SchematicPoint(x, y), label.Center);
        Assert.Equal(rotation, label.Rotation);
        Assert.Equal(76, label.AvailableLength);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void RouteMovementRepositionsCaptionAndModelReassignmentDoesNotRetainOldModel()
    {
        var project = Project();
        var service = new UnifiedCableSettingsService();
        var cable = service.ApplyPointToPoint(project, ["C1"], CableConstructionType.Purchased, "W1", "model-2", null, false, " EVC012 ");
        Assert.Equal("EVC012", cable.DisplayName);
        Assert.Equal("EVC012", SchematicCableLabelPresentation.Resolve(project, Wire([new(40, 30), new(40, 130)]))!.Text);
        Assert.Equal(new SchematicPoint(40, 80), SchematicCableLabelPresentation.Resolve(project, Wire([new(40, 30), new(40, 130)]))!.Center);
        service.ApplyPointToPoint(project, ["C1"], CableConstructionType.Purchased, "W1", "unknown-model", null, false);
        Assert.Null(SchematicCableLabelPresentation.Resolve(project, Wire([new(20, 20), new(20, 100)])));
        Assert.Equal("A", project.Connections.Single().FromEndpointId);
        Assert.Equal("B", project.Connections.Single().ToEndpointId);
        Assert.Empty(cable.CoreAssignments);
    }

    [Fact]
    public void SavedBomModelIsUsedButOpaqueIdsAndOrdinaryWiresAreNotModelLabels()
    {
        var project = Project();
        project.BomItems.Add(new() { ComponentDefinitionId = "model-1", ConnectionMaterial = true,
            Row = new BomRow { RowId = "cable-bom", ModelOrPartNumber = "EVC014" } });
        project.Cables[0].DisplayName = null;
        var wire = Wire([new(20, 20), new(20, 100)]);
        Assert.Equal("EVC014", SchematicCableLabelPresentation.Resolve(project, wire)!.Text);
        project.BomItems.Clear();
        Assert.Null(SchematicCableLabelPresentation.Resolve(project, wire));
        project.Cables[0].DisplayName = "EVC014";
        new UnifiedCableSettingsService().ApplyOrdinaryWire(project, ["C1"]);
        Assert.Null(SchematicCableLabelPresentation.Resolve(project, wire));
        Assert.Null(SchematicCableLabelPresentation.Resolve(project, wire with { ConnectionId = null }));
    }

    [Fact]
    public void EachCrossPageRouteUsesItsOwnGeometryAndRejectsDegenerateSegments()
    {
        var project = Project();
        var first = Wire([new(20, 20), new(30, 20), new(30, 100)]);
        var second = first with { WireId = "other", PageId = "page-2", Points = [new(50, 50), new(140, 50)] };
        Assert.Equal(-90, SchematicCableLabelPresentation.Resolve(project, first)!.Rotation);
        Assert.Equal(0, SchematicCableLabelPresentation.Resolve(project, second)!.Rotation);
        Assert.Null(SchematicCableLabelPresentation.Resolve(project, first with { Points = [new(20, 20), new(20, 20)] }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptionDoesNotCoverCrossingsOrElectricalBranchJunctions(bool branch)
    {
        var project = Project();
        var wire = Wire([new(20, 20), new(20, 100)]);
        var other = wire with { WireId = "cross", ConnectionId = null,
            Start = branch ? SchematicAttachment.Junction(wire.WireId) : SchematicAttachment.Free(),
            Points = branch ? [new(20, 60), new(60, 60)] : [new(0, 60), new(60, 60)] };
        project.Schematic = new() { Wires = [wire, other] };
        var caption = SchematicCableLabelPresentation.Resolve(project, wire)!;
        Assert.NotEqual(new SchematicPoint(20, 60), caption.Center);
        Assert.Equal(new SchematicPoint(20, 40), caption.Center);
        Assert.Equal(36, caption.AvailableLength);
    }

    private static ElectricalProject Project()
    {
        var project = UnifiedCableSettingsTests.Project();
        project.Connections.Clear();
        project.Connections.Add(new() { ConnectionId = "C1", FromEndpointId = "A", ToEndpointId = "B", Kind = ConnectionKind.Cable });
        new UnifiedCableSettingsService().ApplyPointToPoint(project, ["C1"], CableConstructionType.Purchased, "W1", "model-1", null, false, "EVC014");
        return project;
    }

    private static SchematicWire Wire(List<SchematicPoint> points) => new()
    { WireId = "wire", PageId = "page", ConnectionId = "C1", Start = SchematicAttachment.Free(), End = SchematicAttachment.Free(), Points = points };
}
