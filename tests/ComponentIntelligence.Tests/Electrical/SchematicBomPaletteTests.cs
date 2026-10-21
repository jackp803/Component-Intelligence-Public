using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicBomPaletteTests
{
    private static readonly SchematicAuthoringService Service = new();

    [Fact]
    public void EmptyProjectNeverDisplaysWholeCentralCatalog()
    {
        var empty = Service.AddPage(new ElectricalProject { ProjectId = "empty" }, "First");
        Assert.Empty(SchematicBomPalette.Build(empty, [Definition("S1", "Sensor"), Definition("IO1", "I/O")]));
    }

    [Fact]
    public async Task ImportedRowsAndQuantitiesSurviveSqliteReloadAndRemainProjectSpecific()
    {
        var sensor = Definition("S1", "Sensor");
        var io = Definition("IO1", "I/O");
        var project = await Import("sensor-project", sensor, Row("1", "S1", 2, 5));
        var path = Path.Combine(Path.GetTempPath(), "bom-palette-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repo = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repo.SaveAsync(project);
            var loaded = (await repo.GetAsync(project.ProjectId))!;
            var row = Assert.Single(SchematicBomPalette.Build(loaded, [sensor, io]));
            Assert.Equal("Vendor S1", row.Label);
            Assert.Equal("Sensor", row.Category);
            Assert.Equal(2, row.UsedQuantity);
            Assert.Equal(5, row.TotalQuantity);
            Assert.Equal(3, row.SpareQuantity);
            Assert.Equal(2, row.RemainingCount);
            Assert.Equal(2, loaded.BomItems.Single().ComponentInstanceIds.Count);
            var another = await Import("io-project", io, Row("2", "IO1", 1, 1));
            Assert.Equal("Vendor IO1", Assert.Single(SchematicBomPalette.Build(another, [sensor, io])).Label);
            Assert.Equal("Vendor S1", Assert.Single(SchematicBomPalette.Build(loaded, [sensor, io])).Label);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public async Task PlacementConsumesExistingInstanceAndRemovingRepresentationReturnsIt()
    {
        var definition = Definition("S1", "Sensor");
        var project = await Import("p", definition, Row("1", "S1", 2, 2));
        var first = SchematicBomPalette.Build(project, [definition]).Single();
        var originalIds = project.Components.Select(c => c.ComponentInstanceId).ToArray();
        var instance = SchematicBomPalette.NextInstance(project, first);
        var placed = Service.PlaceProjectComponent(project, instance, project.Schematic!.Pages[0].PageId, new(40, 40));
        Assert.Equal(originalIds, placed.Components.Select(c => c.ComponentInstanceId));
        Assert.Empty(placed.Connections);
        Assert.Empty(placed.Nets);
        Assert.Equal(1, SchematicBomPalette.Build(placed, [definition]).Single().PlacedCount);
        Assert.Equal(1, SchematicBomPalette.Build(placed, [definition]).Single().RemainingCount);
        Assert.Throws<InvalidOperationException>(() => Service.PlaceProjectComponent(placed, instance,
            placed.Schematic!.Pages[0].PageId, new(60, 40)));
        var removed = Service.DeleteRepresentation(placed, placed.Schematic!.Symbols.Single().SymbolId);
        Assert.Equal(2, SchematicBomPalette.Build(removed, [definition]).Single().RemainingCount);
    }

    [Fact]
    public async Task MultipleRepresentationsDoNotMultiplyPlacedPhysicalQuantity()
    {
        var definition = Definition("S1", "Sensor");
        var project = await Import("p", definition, Row("1", "S1", 1, 1));
        var component = project.Components.Single();
        var symbol = SchematicAuthoringService.CreateSymbol(component, project.Schematic!.Pages[0].PageId, new(20, 20));
        project.Schematic.Symbols.Add(symbol);
        project.Schematic.Symbols.Add(symbol with { SymbolId = "another-representation" });
        var entry = SchematicBomPalette.Build(project, [definition]).Single();
        Assert.Equal(1, entry.PlacedCount);
        Assert.Equal(0, entry.RemainingCount);
        Assert.Throws<InvalidOperationException>(() => SchematicBomPalette.NextInstance(project, entry));
    }

    [Fact]
    public async Task UnknownQuantitiesSpareOnlyRowsAndConnectionMaterialsRemainExplicit()
    {
        var definition = Definition("S1", "Sensor");
        var project = await Import("p", definition, Row("1", "S1", null, null));
        Assert.Contains("待確認", SchematicBomPalette.Build(project, [definition]).Single().QuantityLabel);
        var spare = await Import("spare", definition, Row("2", "S1", 0, 3));
        Assert.Empty(spare.Components);
        Assert.Equal(0, SchematicBomPalette.Build(spare, [definition]).Single().RemainingCount);
        var wire = Definition("WIRE", "Wire");
        var materials = await Import("material", wire, Row("3", "WIRE", 4, 5));
        var entry = SchematicBomPalette.Build(materials, [wire]).Single();
        Assert.True(entry.ConnectionMaterial);
        Assert.Throws<InvalidOperationException>(() => SchematicBomPalette.NextInstance(materials, entry));
        Assert.Equal(4, SchematicBomPalette.MaterialOptions(materials).Single().AvailableQuantity);
    }

    [Fact]
    public async Task DuplicateRowsAggregateWithoutAddingSpareInstances()
    {
        var definition = Definition("S1", "Sensor");
        var project = Service.AddPage(new ElectricalProject { ProjectId = "p" }, "First");
        await new BomTopologySynchronizer().SynchronizeAsync(project,
            [Row("1", "S1", 2, 3), Row("2", "S1", 1, 1), Row("3", "S1", 0, 4)],
            (_, _, _) => Task.FromResult<ComponentIR?>(definition));
        var entry = Assert.Single(SchematicBomPalette.Build(project, [definition]));
        Assert.Equal(3, entry.UsedQuantity);
        Assert.Equal(8, entry.TotalQuantity);
        Assert.Equal(5, entry.SpareQuantity);
        Assert.Equal(3, entry.InstanceIds.Count);
        Assert.Single(SchematicBomPalette.Build(project, []));
    }

    [Fact]
    public void LegacyProjectUsesItsOwnPhysicalInventoryWithoutUnrelatedCatalogItems()
    {
        var source = Definition("S1", "Sensor");
        var project = new ElectricalProject { ProjectId = "legacy", Components =
            [new ComponentProjectBridge().CreateInstance(source, "a"), new ComponentProjectBridge().CreateInstance(source, "b")] };
        var row = Assert.Single(SchematicBomPalette.Build(project, [source, Definition("IO1", "I/O")]));
        Assert.False(row.Imported);
        Assert.Equal(2, row.UsedQuantity);
        Assert.Equal("Sensor", row.Category);
    }

    [Fact]
    public async Task SpareOnlyRowsKeepCategoryAndPhotoIdentityWithoutCatalog()
    {
        var project = await Import("spare", Definition("S1", "Sensor"), Row("1", "S1", 0, 3));
        var entry = Assert.Single(SchematicBomPalette.Build(project, []));
        Assert.Equal("Sensor", entry.Category);
        Assert.Equal("S1", entry.DefinitionId);
        Assert.Equal(3, entry.SpareQuantity);
        Assert.Empty(entry.InstanceIds);
        Assert.Empty(project.Components);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledImportDoesNotLeavePartialPhysicalOrBomInventory(bool lookupThrows)
    {
        var definition = Definition("S1", "Sensor");
        var project = await Import("p", definition, Row("1", "S1", 1, 1));
        var original = System.Text.Json.JsonSerializer.Serialize(project);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BomTopologySynchronizer().SynchronizeAsync(project,
            [Row("1", "S1", 2, 2), Row("2", "IO1", 1, 1)],
            (_, model, token) =>
            {
                if (model == "IO1")
                {
                    cancellation.Cancel();
                    if (lookupThrows) token.ThrowIfCancellationRequested();
                }
                return Task.FromResult<ComponentIR?>(Definition(model, "Sensor"));
            }, cancellation.Token));
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(project));
        Assert.Single(SchematicBomPalette.Build(project, [definition]));
    }

    private static async Task<ElectricalProject> Import(string id, ComponentIR definition, BomRow row)
    {
        var project = Service.AddPage(new ElectricalProject { ProjectId = id }, "First");
        await new BomTopologySynchronizer().SynchronizeAsync(project, [row], (_, _, _) => Task.FromResult<ComponentIR?>(definition));
        return project;
    }
    private static ComponentIR Definition(string model, string category) => new()
    {
        Identity = new() { ComponentId = model, Manufacturer = "Vendor", Model = model },
        Classification = new() { Category = category }
    };
    private static BomRow Row(string id, string model, int? used, int? total) => new()
    {
        RowId = id, Manufacturer = "Vendor", ModelOrPartNumber = model, UsedQuantity = used,
        TotalQuantity = total, SpareQuantity = used is int a && total is int b ? b - a : null
    };
}
