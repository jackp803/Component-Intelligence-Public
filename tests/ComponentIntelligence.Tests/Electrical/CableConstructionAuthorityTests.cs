using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableConstructionAuthorityTests
{
    [Fact]
    public async Task OptionalWorkbookAuthorityFlowsToBomAndOnePhysicalReviewRow()
    {
        var root = Path.Combine(Path.GetTempPath(), "cable-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "catalog.xlsx");
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Components");
                workbook.AddWorksheet("Ports");
                workbook.AddWorksheet("Pins");
                string[] headers = ["ComponentID", "Manufacturer", "Model", "Category", "CableProductKind", "CableProductEvidence"];
                string[] values = ["DEF", "Maker", "M12", "Cable", "PurchasedPreassembled", "approved source p2"];
                for (var i = 0; i < headers.Length; i++) { sheet.Cell(1, i + 1).Value = headers[i]; sheet.Cell(2, i + 1).Value = values[i]; }
                workbook.SaveAs(path);
            }
            var catalog = await new ComponentIntelligence.Repository.WorkbookComponentKnowledgeStore(path).ListAsync();
            Assert.Equal(CableProductKind.PurchasedPreassembled, Assert.Single(catalog).CableProduct?.Kind);
            var project = UnifiedCableSettingsTests.Project();
            project.Cables.Add(new() { CableInstanceId = "C", CableDefinitionId = "DEF" });
            foreach (var connection in project.Connections) connection.CableInstanceId = "C";
            var service = new EngineeringReviewService(catalog, new ComponentIntelligence.SymbolArchive.SymbolArchiveRepository(root), "test catalog");
            var coverage = await service.InspectAsync(project);
            Assert.Single(coverage.Cables);
            Assert.Equal(0, coverage.UnsafeCableRoles);
            Assert.Equal(CableConstructionType.Unknown, project.Cables[0].CableConstructionType);
            var repository = new ComponentIntelligence.Electrical.Persistence.ElectricalProjectRepository(
                new ComponentIntelligence.Repository.SqliteConnectionFactory(), Path.Combine(root, "disposable.db"));
            var draft = CableConstructionAuthority.ApplyDefinitions(project, catalog);
            await repository.SaveAsync(draft);
            var reloaded = await repository.GetAsync(project.ProjectId);
            Assert.Equal(CableConstructionType.Purchased, reloaded!.Cables.Single().CableConstructionType);
            Assert.Equal(project.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId, c.NetId)),
                reloaded.Connections.Select(c => (c.ConnectionId, c.FromEndpointId, c.ToEndpointId, c.NetId)));
            var noEvidence = new EngineeringReviewService([], new ComponentIntelligence.SymbolArchive.SymbolArchiveRepository(root), "empty");
            Assert.Equal(1, (await noEvidence.InspectAsync(project)).UnsafeCableRoles);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WireAndDirectMatingDoNotRequireConstructionOrCreateCable()
    {
        var project = UnifiedCableSettingsTests.Project();
        var before = JsonSerializer.Serialize(project);
        var wire = CableConstructionAuthority.Describe(null);
        Assert.Equal("Ordinary Wire", wire.State);
        Assert.False(wire.RequiresChoice);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        var root = Path.Combine(Path.GetTempPath(), "cable-review-" + Guid.NewGuid().ToString("N"));
        var service = new EngineeringReviewService([], new ComponentIntelligence.SymbolArchive.SymbolArchiveRepository(root), "none");
        Assert.Empty((await service.InspectAsync(project)).Cables);
        project.Connections[0].Kind = ConnectionKind.DirectMating;
        Assert.Empty((await service.InspectAsync(project)).Cables);
        Assert.Empty(project.Cables);
    }

    [Fact]
    public async Task MultiEndCustomHasOnePhysicalClassificationNotOnePerConductor()
    {
        var project = MultiEndCableEditorServiceTests.Fixture(3);
        var editor = new MultiEndCableEditorService();
        var draft = editor.PrepareNew(project, project.Connections.Select(c => c.ConnectionId).ToArray());
        editor.ConfirmEnds(project, draft, "common");
        draft.ConstructionType = CableConstructionType.Custom;
        editor.Apply(project, draft);
        var service = new EngineeringReviewService([], new ComponentIntelligence.SymbolArchive.SymbolArchiveRepository(
            Path.Combine(Path.GetTempPath(), "cable-review-" + Guid.NewGuid().ToString("N"))), "none");
        var coverage = await service.InspectAsync(project);
        Assert.Equal(6, project.Connections.Count);
        Assert.Single(coverage.Cables);
        Assert.Equal(0, coverage.UnsafeCableRoles);
        Assert.Equal("Custom", coverage.Cables[0].ConstructionType);
        Assert.Equal(3, project.CableAssemblies.Single().PhysicalTopology!.Branches.Count);
    }

    [Theory]
    [InlineData(CableConstructionType.Purchased, "Purchased")]
    [InlineData(CableConstructionType.Custom, "Custom")]
    [InlineData(CableConstructionType.Unknown, "Unknown Cable")]
    public void PersistedPhysicalCableStateIsVisibleWithoutReconfirmation(CableConstructionType type, string state)
    {
        var cable = new CableInstance { CableInstanceId = "C", CableDefinitionId = "M12-RJ45", CableConstructionType = type };
        var result = CableConstructionAuthority.Describe(cable);
        Assert.Equal(state, result.State);
        Assert.Equal(type == CableConstructionType.Unknown, result.RequiresChoice);
    }

    [Fact]
    public void ExplicitFinishedProductResolvesPurchasedButNotModelOrBulkMaterial()
    {
        var cable = new CableInstance { CableInstanceId = "C", CableDefinitionId = "DEF" };
        var source = new ComponentIR { Identity = new() { ComponentId = "DEF", Manufacturer = "Maker", Model = "M12 Y preassembled" } };
        Assert.True(CableConstructionAuthority.Describe(cable, source).RequiresChoice);
        source = source with { CableProduct = new() { Kind = CableProductKind.BulkMaterial, Evidence = "catalog p1" } };
        Assert.True(CableConstructionAuthority.Describe(cable, source).RequiresChoice);
        source = source with { CableProduct = new() { Kind = CableProductKind.PurchasedPreassembled, Evidence = "catalog p2 finished assembly" } };
        Assert.Equal("Purchased", CableConstructionAuthority.Describe(cable, source).State);
        var project = new ElectricalProject { ProjectId = "P", Cables = [cable] };
        var before = JsonSerializer.Serialize(project);
        var draft = CableConstructionAuthority.ApplyDefinitions(project, [source]);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal(CableConstructionType.Purchased, draft.Cables.Single().CableConstructionType);
        Assert.Equal(draft.Cables[0].CableConstructionType, JsonSerializer.Deserialize<ElectricalProject>(JsonSerializer.Serialize(draft))!.Cables[0].CableConstructionType);
        Assert.True(CableConstructionAuthority.Describe(cable, source with { Identity = source.Identity with { ComponentId = "OTHER" } }).RequiresChoice);
        Assert.True(CableConstructionAuthority.Describe(cable, source with { CableProduct = new() { Kind = CableProductKind.PurchasedPreassembled } }).RequiresChoice);
    }

    [Fact]
    public void LegacyCatalogJsonHasNoConstructionAuthority()
    {
        var source = JsonSerializer.Deserialize<ComponentIR>("{\"Identity\":{\"ComponentId\":\"D\",\"Manufacturer\":\"M\",\"Model\":\"Y\"}}")!;
        Assert.Null(source.CableProduct);
    }
}
