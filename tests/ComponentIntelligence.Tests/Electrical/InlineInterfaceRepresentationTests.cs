using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class InlineInterfaceRepresentationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "inline-review-" + Guid.NewGuid().ToString("N"));
    private EngineeringReviewService Service() => new([], new SymbolArchiveRepository(_root), "No canonical catalog");

    private static ElectricalProject Project(string definition)
    {
        var factoryId = definition == "inline-mated-adapter:M12 female cable end (3 cores)"
            ? CommonConnectorCatalog.M12FemaleACode4PinCableEndId
            : definition == "inline-mated-adapter:M12 male cable end (3 cores)"
                ? CommonConnectorCatalog.M12MaleACode4PinCableEndId : definition;
        var source = CommonConnectorCatalog.Create(factoryId, "X1");
        var instance = new ComponentInstance { ComponentInstanceId = source.ComponentInstanceId,
            ComponentDefinitionId = definition, TypeKey = source.TypeKey, ReferenceDesignator = "X1",
            Ports = source.Ports };
        return new ElectricalProject { ProjectId = "inline", Components = [instance],
            Connections = [new() { ConnectionId = "connection", FromEndpointId = instance.Ports[0].PortId,
                ToEndpointId = instance.Ports[0].Pins.First().PinId }] };
    }

    [Theory]
    [InlineData("common:cable-end:m12-female-a-4pin")]
    [InlineData("common:cable-end:m12-male-a-4pin")]
    [InlineData("common:cable-end:rj45-male-8p8c")]
    [InlineData("common:cat6-rj45-female-female-shielded-8c-coupler")]
    [InlineData("inline-mated-adapter:M12 female cable end (3 cores)")]
    [InlineData("inline-mated-adapter:M12 male cable end (3 cores)")]
    public async Task ExplicitInterface_IsSafeWithoutCatalogOrArchive_AndDoesNotMutate(string definition)
    {
        var project = Project(definition);
        var before = JsonSerializer.Serialize(project);
        var coverage = await Service().InspectAsync(project);
        Assert.Equal("SAFE_INLINE_INTERFACE", coverage.Components.Single().Classification);
        Assert.Equal(0, coverage.UnsafeComponentRoles);
        Assert.Empty(Service().Candidates);
        var input = DrawingPlanningRuntimeFactory.Create(_root, []).Build(project);
        var representation = Assert.Single(input.Representations);
        Assert.Equal(DrawingRepresentationFamily.FunctionalGeneric, representation.Family);
        Assert.Equal("ProjectInlineInterface", representation.SourceType);
        Assert.True(representation.PhysicalInterfaceMeaning);
        Assert.Null(representation.AssetPath);
        Assert.Null(representation.AssetRevision);
        Assert.Null(representation.AssetHashSha256);
        Assert.DoesNotContain(input.Issues, i => i.Severity == DrawingPlanningIssueSeverity.Blocker);
        Assert.All(project.Connections.SelectMany(c => new[] { c.FromEndpointId, c.ToEndpointId }),
            id => Assert.Single(representation.PortBindings, b => b.EngineeringEndpointId == id));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Empty(new SymbolArchiveRepository(_root).Load().Bindings);
    }

    [Theory]
    [InlineData("missing-used-pin")]
    [InlineData("duplicate-id")]
    [InlineData("missing-connector")]
    [InlineData("missing-contact-count")]
    [InlineData("empty-interface")]
    [InlineData("unowned-used-endpoint")]
    [InlineData("excess-contacts")]
    public async Task InsufficientProjectTruth_FailsClosed(string corruption)
    {
        var project = Project(CommonConnectorCatalog.M12MaleACode4PinCableEndId);
        var component = project.Components[0];
        if (corruption == "missing-used-pin") component.Ports[0].Pins.RemoveAt(0);
        if (corruption == "duplicate-id") component.Ports.Add(component.Ports[0]);
        if (corruption == "missing-connector") component.Ports.ForEach(p => p.Connector = null);
        if (corruption == "missing-contact-count") component.Ports.First(p => p.Connector is not null).Connector!.PinCount = null;
        if (corruption == "empty-interface") component.Ports.First(p => p.Connector is not null).Connector!.Family = "";
        if (corruption == "unowned-used-endpoint") project.Connections.Add(new() { ConnectionId = "orphan", FromEndpointId = "absent", ToEndpointId = component.Ports[0].PortId });
        if (corruption == "excess-contacts") component.Ports.First(p => p.Connector is not null).Connector!.PinCount = 1;
        Assert.Equal("UNSAFE_UNRESOLVED", (await Service().InspectAsync(project)).Components.Single().Classification);
        var input = DrawingPlanningRuntimeFactory.Create(_root, []).Build(project);
        Assert.Contains(input.Issues, i => i.Severity == DrawingPlanningIssueSeverity.Blocker);
        Assert.DoesNotContain(input.Representations, r => r.SourceType == "ProjectInlineInterface");
    }

    [Fact]
    public async Task UnspecifiedNamesCodingAndGender_DoNotInventNewMeaning()
    {
        var project = Project(CommonConnectorCatalog.M12MaleACode4PinCableEndId);
        foreach (var port in project.Components[0].Ports)
        {
            if (port.Connector is { } connector) connector.Coding = null;
            foreach (var pin in port.Pins) { pin.PinName = null; pin.Function = null; }
        }
        var before = JsonSerializer.Serialize(project);
        Assert.Equal("SAFE_INLINE_INTERFACE", (await Service().InspectAsync(project)).Components.Single().Classification);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public async Task LookalikeIdentity_IsNotAnAllowlistedInterface()
    {
        var project = Project(CommonConnectorCatalog.M12MaleACode4PinCableEndId);
        var node = JsonSerializer.SerializeToNode(project.Components[0])!;
        node[nameof(ComponentInstance.ComponentDefinitionId)] = "COMMON:cable-end:m12-male-a-4pin";
        project.Components[0] = node.Deserialize<ComponentInstance>()!;
        Assert.Equal(1, (await Service().InspectAsync(project)).UnsafeComponentRoles);
    }

    [Fact]
    public void ReorderedPortsAndPins_KeepDeterministicPlanning()
    {
        var project = Project(CommonConnectorCatalog.M12MaleACode4PinCableEndId);
        var builder = DrawingPlanningRuntimeFactory.Create(_root, []);
        var before = JsonSerializer.Serialize(builder.Build(project));
        project.Components[0].Ports.Reverse();
        project.Components[0].Ports.ForEach(p => p.Pins.Reverse());
        Assert.Equal(before, JsonSerializer.Serialize(builder.Build(project)));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
