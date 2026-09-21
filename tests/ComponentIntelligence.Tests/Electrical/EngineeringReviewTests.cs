using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EngineeringReviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "engineering-review-" + Guid.NewGuid().ToString("N"));
    private static ComponentIR Catalog() => ComponentSourceIdentityRestorerTests.Catalog();
    private EngineeringReviewService Service() => new([Catalog()], new SymbolArchiveRepository(_root), "Configured central catalog");
    private static ElectricalProject Project()
    {
        var instance = new ComponentProjectBridge().CreateInstance(Catalog(), "I", "S1");
        return new ElectricalProject { ProjectId = "review", Components = [instance],
            TopologyRoutes = [new() { ConnectionId = "wire", Points = [new() { X = 10, Y = 20 }, new() { X = 30, Y = 20 }] }],
            Cables = [new() { CableInstanceId = "C", CableDefinitionId = "CATALOG-LOOKING", DisplayName = "Sensor supply", ReferenceDesignator = "CB1" }],
            Connections = [new() { ConnectionId = "wire", FromEndpointId = instance.Ports[0].Pins[0].PinId,
                ToEndpointId = instance.Ports[0].Pins[1].PinId, CableInstanceId = "C" }] };
    }

    [Fact]
    public async Task Audit_IsPure_CoversExplicitUsedPinDespiteConnectorInteractionHint()
    {
        var project = Project(); var before = JsonSerializer.Serialize(project);
        var report = await Service().InspectAsync(project);
        Assert.Equal(0, report.UnsafeComponentRoles);
        Assert.Equal(1, report.UnsafeCableRoles);
        Assert.Contains("S1", report.Cables.Single().From);
        Assert.Contains("Sensor supply", report.Cables.Single().Label);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void UnknownOrUnconfirmedCableChoice_FailsWithoutChangingAnything()
    {
        var project = Project(); var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => Service().ReviewCable(project, "C", null, true));
        Assert.Throws<InvalidOperationException>(() => Service().ReviewCable(project, "C", CableConstructionType.Custom, false));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public async Task ExplicitCableChoice_ChangesOnlyConstruction_OnIndependentDraft_AndRechecks()
    {
        var project = Project();
        var draft = Service().ReviewCable(project, "C", CableConstructionType.Purchased, true);
        Assert.Equal(CableConstructionType.Unknown, project.Cables[0].CableConstructionType);
        Assert.Equal(CableConstructionType.Purchased, draft.Cables[0].CableConstructionType);
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(draft.Connections));
        Assert.Equal(JsonSerializer.Serialize(project.Components), JsonSerializer.Serialize(draft.Components));
        Assert.Equal(0, (await Service().InspectAsync(draft)).UnsafeCableRoles);
    }

    [Fact]
    public void AlreadyClassifiedCable_IsNotOverwrittenByUnresolvedReview()
    {
        var project = Project(); project.Cables[0].CableConstructionType = CableConstructionType.Custom;
        Assert.Throws<InvalidOperationException>(() => Service().ReviewCable(project, "C", CableConstructionType.Purchased, true));
    }

    [Fact]
    public async Task ConfirmedIdentityWithoutRequiredCatalogEndpointBridge_StillCannotApply()
    {
        var project = new ElectricalProject { ProjectId = "P", Components =
            [new() { ComponentInstanceId = "U", ComponentDefinitionId = "legacy", TypeKey = "X" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ReviewComponentAsync(project, "U", "DEF",
            new Dictionary<string, string>(), "Human identity only, no endpoint evidence", true));
        Assert.Equal("legacy", project.Components[0].ComponentDefinitionId);
    }

    [Theory]
    [InlineData("not-in-catalog")]
    [InlineData("1")]
    public void RequiredEndpointCannotBeInventedOrMatchedByPinNumber(string id)
    {
        Assert.Throws<InvalidOperationException>(() => new GeneratedGenericSymbolFactory().Create(Catalog(), SymbolRole.Schematic, [id]));
    }

    [Fact]
    public async Task MissingIdentity_IsUnresolved_CandidatesAreContextNotAutomaticMatches()
    {
        var project = new ElectricalProject { ProjectId = "unknown", Components =
            [new() { ComponentInstanceId = "U", ComponentDefinitionId = "legacy", TypeKey = "X", DisplayName = "M X", ReferenceDesignator = "K1" }] };
        var report = await Service().InspectAsync(project);
        Assert.Equal(1, report.UnsafeComponentRoles);
        var row = report.Components.Single();
        Assert.Contains("K1", row.Label); Assert.Contains("M X", row.Label);
        Assert.Equal("DEF", Service().Candidates.Single().ComponentId);
        Assert.Contains("Configured central catalog", Service().Candidates.Single().Evidence);
        Assert.Equal("legacy", project.Components[0].ComponentDefinitionId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-parent")]
    [InlineData("unconfirmed")]
    public async Task InvalidIdentityOrMapping_FailsAtomically(string mode)
    {
        var project = Project(); var before = JsonSerializer.Serialize(project);
        var c = project.Components[0];
        var mappings = new Dictionary<string, string> { [c.Ports[0].PortId] = "P", [c.Ports[0].Pins[0].PinId] = "SOURCE-A", [c.Ports[0].Pins[1].PinId] = "SOURCE-B" };
        if (mode == "missing") mappings.Remove(c.Ports[0].Pins[0].PinId);
        if (mode == "duplicate") mappings[c.Ports[0].Pins[1].PinId] = "SOURCE-A";
        if (mode == "wrong-parent") mappings[c.Ports[0].PortId] = "SOURCE-A";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ReviewComponentAsync(project, "I", "DEF", mappings, "PO checked drawing section A", mode != "unconfirmed"));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public async Task ExplicitIdentityAndAllMappings_PreserveGeometryAndConnections()
    {
        var project = Project(); var c = project.Components[0];
        var mappings = new Dictionary<string, string> { [c.Ports[0].PortId] = "P", [c.Ports[0].Pins[0].PinId] = "SOURCE-A", [c.Ports[0].Pins[1].PinId] = "SOURCE-B" };
        var draft = await Service().ReviewComponentAsync(project, "I", "DEF", mappings, "PO reviewed exact source", true);
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(draft.Connections));
        Assert.Equal(JsonSerializer.Serialize(project.TopologyRoutes), JsonSerializer.Serialize(draft.TopologyRoutes));
        Assert.Equal(c.ComponentInstanceId, draft.Components[0].ComponentInstanceId);
        Assert.Equal(0, (await Service().InspectAsync(draft)).UnsafeComponentRoles);
    }

    [Fact]
    public void ConfiguredPlanning_UnknownCable_IsAnActionableBlocker()
    {
        var input = ComponentIntelligence.Electrical.Drawing.DrawingPlanningRuntimeFactory.Create(_root, [Catalog()]).Build(Project());
        Assert.Contains(input.Issues, i => i.Code == "ENGINEERING_CABLE_REVIEW_REQUIRED" && i.TargetId == "C");
    }

    [Fact]
    public async Task SaveAndRevision_ReloadPreservesOnlyDeliberateChoice()
    {
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "review.db");
        var factory = new ComponentIntelligence.Repository.SqliteConnectionFactory();
        var repo = new ComponentIntelligence.Electrical.Persistence.ElectricalProjectRepository(factory, db);
        var revisions = new ProjectRevisionService(new ComponentIntelligence.Electrical.Persistence.ProjectRevisionRepository(factory, db));
        var project = Project(); await repo.SaveAsync(project);
        var draft = Service().ReviewCable(project, "C", CableConstructionType.Custom, true);
        await revisions.CreateCheckpointAsync(project, ProjectRevisionTrigger.TopologyChange, "Before explicit Custom confirmation C");
        await repo.SaveAsync(draft);
        await revisions.CreateCheckpointAsync(draft, ProjectRevisionTrigger.TopologyChange, "Confirmed Custom C");
        var reloaded = (await repo.GetAsync(project.ProjectId))!;
        Assert.Equal(CableConstructionType.Custom, reloaded.Cables[0].CableConstructionType);
        Assert.Equal(2, (await revisions.ListAsync(project.ProjectId)).Count);
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(reloaded.Connections));
    }
    [Fact]
    public async Task ExactPersistedCache_IsReadableContext_NotCatalogAuthority()
    {
        var service = new EngineeringReviewService([], new SymbolArchiveRepository(_root), "empty catalog", [Catalog()]);
        var report = await service.InspectAsync(Project());
        Assert.Empty(service.Candidates);
        Assert.Equal("M", report.Components.Single().Manufacturer);
        Assert.Equal("X", report.Components.Single().Model);
        Assert.Equal(1, report.UnsafeComponentRoles);
    }

    [Fact]
    public async Task ExplicitIdentity_SaveRevisionReload_PreservesEndpointsRoutesAndEvidence()
    {
        Directory.CreateDirectory(_root);
        var db = Path.Combine(_root, "identity.db");
        var factory = new ComponentIntelligence.Repository.SqliteConnectionFactory();
        var repo = new ComponentIntelligence.Electrical.Persistence.ElectricalProjectRepository(factory, db);
        var revisions = new ProjectRevisionService(new ComponentIntelligence.Electrical.Persistence.ProjectRevisionRepository(factory, db));
        var project = Project();
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(project.Components[0]))!;
        node[nameof(ComponentInstance.ComponentDefinitionId)] = "legacy";
        project.Components[0] = node.Deserialize<ComponentInstance>()!;
        var c = project.Components[0];
        c.Ports[0].SourcePortId = null; c.Ports[0].Capabilities.Clear();
        foreach (var pin in c.Ports[0].Pins) pin.SourcePinId = null;
        var before = JsonSerializer.Serialize(project);
        var mappings = new Dictionary<string, string> { [c.Ports[0].PortId] = "P", [c.Ports[0].Pins[0].PinId] = "SOURCE-A", [c.Ports[0].Pins[1].PinId] = "SOURCE-B" };
        var draft = await Service().ReviewComponentAsync(project, "I", "DEF", mappings, "Signed engineering table A", true);
        await revisions.CreateCheckpointAsync(project, ProjectRevisionTrigger.TopologyChange, "Before identity confirmation");
        await repo.SaveAsync(draft);
        await revisions.CreateCheckpointAsync(draft, ProjectRevisionTrigger.TopologyChange, "Signed engineering table A: DEF; P; SOURCE-A; SOURCE-B");
        var reloaded = (await repo.GetAsync(project.ProjectId))!;
        Assert.Equal("DEF", reloaded.Components.Single().ComponentDefinitionId);
        Assert.Equal("SOURCE-A", reloaded.Components[0].Ports[0].Pins[0].SourcePinId);
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(reloaded.Connections));
        Assert.Equal(JsonSerializer.Serialize(project.TopologyRoutes), JsonSerializer.Serialize(reloaded.TopologyRoutes));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal(2, (await revisions.ListAsync(project.ProjectId)).Count);
        Assert.Equal(0, (await Service().InspectAsync(reloaded)).UnsafeComponentRoles);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
