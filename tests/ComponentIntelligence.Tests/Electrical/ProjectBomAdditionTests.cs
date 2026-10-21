using System.Reflection;
using System.Text.Json;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ProjectBomAdditionTests
{
    private static ElectricalProject Add(ElectricalProject project, ComponentIR source, int quantity)
    {
        var method = typeof(SchematicAuthoringService).GetMethod("AddBomComponent");
        Assert.NotNull(method);
        try { return (ElectricalProject)method.Invoke(new SchematicAuthoringService(), [project, source, quantity])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
    }

    [Fact]
    public void AppendPreservesExistingRowsPositionsAndExternalWiring()
    {
        var project = CableInstanceDuplicationTests.Project(); var before = JsonSerializer.Serialize(project);
        var next = Add(project, Definition(), 2);
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal(JsonSerializer.Serialize(project.Schematic), JsonSerializer.Serialize(next.Schematic));
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(next.Connections));
        Assert.Equal(JsonSerializer.Serialize(project.Components), JsonSerializer.Serialize(next.Components.Take(project.Components.Count)));
        Assert.Equal(project.Components.Count + 2, next.Components.Count);
        Assert.All(next.Components.TakeLast(2), c => Assert.Null(c.ReferenceDesignator));
        var row = next.BomItems.Last(); Assert.Equal(2, row.Row.UsedQuantity); Assert.Equal(2, row.ComponentInstanceIds.Count);
        Assert.All(project.Components, c => Assert.Contains(SchematicBomPalette.Build(next, []), b => b.InstanceIds.Contains(c.ComponentInstanceId)));
        Assert.Equal(2, SchematicBomPalette.Build(next, []).Single(b => b.DefinitionId == "custom:probe").RemainingCount);
    }

    [Fact]
    public void ExistingImportedInventoryIsNotReplacedAndRepeatedAddsAreIndependent()
    {
        var project = Add(new() { ProjectId = "append" }, Definition(), 1);
        var before = JsonSerializer.Serialize(project.BomItems);
        var next = Add(project, Definition(), 2);
        Assert.Equal(before, JsonSerializer.Serialize(next.BomItems.Take(project.BomItems.Count)));
        Assert.Equal(3, next.Components.Count);
        Assert.Equal(3, next.Components.Select(c => c.ComponentInstanceId).Distinct().Count());
        var pins = next.Components.SelectMany(c => c.Ports.SelectMany(p => p.Pins)).ToArray();
        Assert.Equal(6, pins.Select(p => p.PinId).Distinct().Count());
        next.Components[1].Ports[0].Pins[0].Function = "copy only";
        Assert.Equal("24V", next.Components[0].Ports[0].Pins[0].Function);
        Assert.Equal("24V", next.Components[2].Ports[0].Pins[0].Function);
        Assert.Equal(3, Assert.Single(SchematicBomPalette.Build(next, [])).UsedQuantity);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1001)]
    public void BadQuantityCannotPartiallyMutateProject(int quantity)
    {
        var project = CableInstanceDuplicationTests.Project(); var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => Add(project, Definition(), quantity));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void LocalVariantsKeepTheirOwnIdentityEvenWhenManufacturerAndModelMatchCatalog()
    {
        var catalog = Definition();
        var local = catalog with { Identity = catalog.Identity with { ComponentId = "project-local:variant" },
            Classification = new() { Category = "Custom variant" }, Pins = [catalog.Pins[0]] };
        var project = Add(Add(new() { ProjectId = "variants" }, catalog, 1), local, 1);
        var entries = SchematicBomPalette.Build(project, [catalog]);
        Assert.Equal(2, entries.Count);
        Assert.Equal("Custom variant", entries.Single(e => e.DefinitionId == "project-local:variant").Category);
        Assert.Single(entries.Single(e => e.DefinitionId == "project-local:variant").InstanceIds);
    }

    [Fact]
    public void CentralRefreshCannotOverwriteSameNamedProjectLocalContacts()
    {
        var central = Definition();
        var local = central with { Identity = central.Identity with { ComponentId = "project-local:isolated" },
            Pins = [central.Pins[0] with { Function = "local function" }] };
        var project = Add(new() { ProjectId = "isolation" }, local, 1);
        var before = JsonSerializer.Serialize(project.Components.Single());
        new CentralArchiveProjectSynchronizer().Synchronize(project, [central]);
        Assert.Equal(before, JsonSerializer.Serialize(project.Components.Single()));
    }

    [Theory]
    [InlineData("identity")] [InlineData("port")] [InlineData("pin")] [InlineData("material")]
    public void InvalidDefinitionCannotCreateAmbiguousEndpoints(string invalid)
    {
        var source = Definition();
        source = invalid switch {
            "identity" => source with { Identity = source.Identity with { Model = " " } },
            "port" => source with { Ports = [source.Ports[0], source.Ports[0]] },
            "pin" => source with { Pins = [source.Pins[0], source.Pins[0]] },
            _ => source with { Classification = new() { Category = "Wire" } }
        };
        var project = new ElectricalProject { ProjectId = "invalid" }; var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => Add(project, source, 1));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public async Task CustomPinsAndQuantitiesSurviveUndoRedoAndSqliteWithoutCentralCatalog()
    {
        var original = new SchematicAuthoringService().AddPage(new() { ProjectId = "custom" }, "Sheet");
        var history = new ProjectMutationHistory(); history.RecordBeforeMutation(original, "Add component");
        var next = Add(original, Definition(), 2);
        Assert.True(history.TryUndo(next, out var undone, out _)); Assert.Empty(undone.Components);
        Assert.True(history.TryRedo(undone, out var redone, out _));
        Assert.Equal(JsonSerializer.Serialize(next), JsonSerializer.Serialize(redone));
        var path = Path.Combine(Path.GetTempPath(), "bom-add-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repository.SaveAsync(next); var loaded = (await repository.GetAsync(next.ProjectId))!;
            var entry = Assert.Single(SchematicBomPalette.Build(loaded, [])); Assert.Equal(2, entry.RemainingCount);
            Assert.Equal(JsonSerializer.Serialize(Definition()), JsonSerializer.Serialize(loaded.BomItems.Single().DefinitionSnapshot));
            var author = new SchematicAuthoringService();
            loaded = author.PlaceProjectComponent(loaded, SchematicBomPalette.NextInstance(loaded, entry), loaded.Schematic!.Pages[0].PageId, new(80, 80));
            Assert.Single(loaded.Schematic!.Symbols); Assert.Equal(2, loaded.Components[0].Ports[0].Pins.Count);
            Assert.Equal("24V", loaded.Components[0].Ports[0].Pins[0].Function);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    internal static ComponentIR Definition() => new() {
        Identity = new() { ComponentId = "custom:probe", Manufacturer = "Custom", Model = "Probe" },
        Classification = new() { Category = "Sensor" },
        Ports = [new() { PortId = "P1" }],
        Pins = [new() { PortId = "P1", PinNumber = "1", Function = "24V" }, new() { PortId = "P1", PinNumber = "2", Function = "0V" }]
    };
}
