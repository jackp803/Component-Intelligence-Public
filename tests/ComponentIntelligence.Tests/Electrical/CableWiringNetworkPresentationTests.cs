using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableWiringNetworkPresentationTests
{
    [Fact]
    public void BranchesAndCyclesShareOneNetworkButIdenticalPinLabelsDoNot()
    {
        var template = CableManufacturingEditorTests.Template(3) with { Mapping = [
            new("p1.1", "p2.1"), new("p2.1", "p3.1"), new("p1.1", "p3.1"), new("p1.2", "p3.2")] };
        foreach (var pin in template.Ports.SelectMany(p => p.Pins)) pin.Function = "GND";
        var draft = new CableManufacturingEditorService().Prepare(template);
        var service = new CableWiringEditorService(); service.EnterVisualMode(draft);
        var before = JsonSerializer.Serialize(draft);
        var view = service.Build(draft);
        Assert.Equal(view.EdgePaths.Keys.Order(), view.EdgeNetworks.Keys.Order());
        Assert.Equal(3, view.EdgeNetworks.Values.Count(n => n == "p1.1"));
        Assert.Equal("p1.2", view.EdgeNetworks[CableWiringEditorService.EdgeKey("p1.2", "p3.2")]);
        Assert.Equal(before, JsonSerializer.Serialize(draft));
        draft.ExplicitMapping!.Reverse();
        var reversed = service.Build(draft);
        Assert.Equal(view.EdgeNetworks.OrderBy(p => p.Key), reversed.EdgeNetworks.OrderBy(p => p.Key));
        service.Disconnect(draft, "p1.1", "p3.1");
        Assert.Equal(2, service.Build(draft).EdgeNetworks.Values.Count(n => n == "p1.1"));
        service.Disconnect(draft, "p2.1", "p3.1");
        Assert.Equal(2, service.Build(draft).EdgeNetworks.Values.Distinct().Count());
    }

    [Fact]
    public void RemovingBridgeSeparatesTwoNetworksThatStillContainWires()
    {
        var template = CableManufacturingEditorTests.Template() with { Mapping = [
            new("p1.1", "p2.1"), new("p1.2", "p2.2"), new("p2.1", "p2.2")] };
        var draft = new CableManufacturingEditorService().Prepare(template);
        var service = new CableWiringEditorService(); service.EnterVisualMode(draft);
        Assert.Single(service.Build(draft).EdgeNetworks.Values.Distinct());
        service.Disconnect(draft, "p2.1", "p2.2");
        var result = service.Build(draft);
        Assert.Equal(2, result.EdgePrimitives.Count);
        Assert.Equal(2, result.EdgeNetworks.Values.Distinct().Count());
    }

    [Fact]
    public void UnconnectedInventoryDoesNotInventNetworkMembership()
    {
        var draft = new CableManufacturingEditorService().Prepare(CableManufacturingEditorTests.Template());
        var service = new CableWiringEditorService(); service.EnterVisualMode(draft);
        Assert.Empty(service.Build(draft).EdgeNetworks);
    }
}
