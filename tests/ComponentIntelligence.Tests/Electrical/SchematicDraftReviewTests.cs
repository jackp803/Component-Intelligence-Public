using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicDraftReviewTests
{
    [Fact]
    public void ListsBothFreeEndsWithoutInferringNcOrChangingProject()
    {
        var project = new ElectricalProject { ProjectId = "test", Schematic = new()
        {
            Pages = [new() { PageId = "P", Title = "Control" }],
            Wires = [new() { WireId = "W", PageId = "P", Points = [new(1, 1), new(10, 1)] }]
        } };
        var before = JsonSerializer.Serialize(project);
        var issues = SchematicDraftReview.Inspect(project);
        Assert.Equal(2, issues.Count(i => i.Code == "UNFINISHED_WIRE_END"));
        Assert.All(issues.Where(i => i.Code == "UNFINISHED_WIRE_END"), i => Assert.Equal("W", i.ObjectId));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void UsesHumanReferenceAndListsUnconfirmedAnchorSeparatelyFromAppearance()
    {
        var project = new ElectricalProject { ProjectId = "test", Components =
            [new() { ComponentInstanceId = "C", ComponentDefinitionId = "CAT", TypeKey = "Device", ReferenceDesignator = "K1" }],
            Schematic = new() { Pages = [new() { PageId = "P", Title = "Control" }], Symbols =
            [new() { SymbolId = "S", ComponentInstanceId = "C", PageId = "P", Anchors =
            [new() { EndpointId = "A1", Label = "Coil A1", Confirmed = false }] }] } };
        var issues = SchematicDraftReview.Inspect(project);
        Assert.Contains(issues, i => i.Code == "UNCONFIRMED_ANCHOR" && i.Description.Contains("K1") && i.Description.Contains("Coil A1"));
        Assert.Contains(issues, i => i.Code == "UNAPPROVED_REPRESENTATION");
    }
}
