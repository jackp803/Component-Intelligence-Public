using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Tests.SymbolArchive;

public sealed class SchematicModuleLayoutStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "module-layout-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SavedLayoutAppliesToNewRuntimePinsWithoutChangingApprovedAsset()
    {
        var repository = new SymbolArchiveRepository(_root);
        var approved = new ComponentSymbolBinding { ComponentId = "source-module", Role = SymbolRole.Schematic,
            Revisions = [new SymbolRevisionRecord { Revision = "rev-001", Status = SymbolRevisionStatus.Approved,
                SourceType = SymbolSourceType.ApprovedCustom, AssetPath = "Documents/approved.dxf",
                AssetHashSha256 = new string('a', 64) }] };
        repository.Save(new SymbolArchiveDocument { Bindings = [approved] });
        var store = new SchematicModuleLayoutStore(repository);
        var project = Project("instance-a", "port-a", "pin-a");
        var symbol = project.Schematic!.Symbols.Single();
        project = new SchematicAuthoringService().ResizeGenericSymbol(project, symbol.SymbolId, 95, 70);
        project = new SchematicAuthoringService().MovePortToEdge(project, symbol.SymbolId, "port-a", "Top", 30);
        project = new SchematicAuthoringService().TransformSymbol(project, symbol.SymbolId,
            project.Schematic.Symbols.Single().Position, 90);
        var saved = store.Save(project, symbol.SymbolId);

        var anotherProject = Project("instance-b", "port-b", "pin-b");
        var result = store.ApplyActive(anotherProject, anotherProject.Schematic!.Symbols.Single().SymbolId);
        Assert.True(result.Applied, result.Warning);
        var placed = result.Project.Schematic!.Symbols.Single();
        Assert.Equal("pin-b", placed.Anchors.Single().EndpointId);
        Assert.Equal("port-b", placed.PortPlacements.Single().PortId);
        Assert.Equal(saved.Width, placed.Width);
        Assert.Equal(saved.Height, placed.Height);
        Assert.Equal(90, placed.Rotation);
        Assert.Equal(saved.Pins.Single().Position, placed.Anchors.Single().Position);
        Assert.Equal(saved.Revision, Assert.Single(repository.Load().SchematicLayouts).Revision);
        Assert.Equal(approved.Revisions.Single(), Assert.Single(repository.Load().Bindings).Revisions.Single());
        Assert.Equal(SymbolArchiveRepository.SchematicLayoutSchemaVersion, repository.Load().SchemaVersion);
    }

    [Fact]
    public void ChangedSourcePinInventoryDoesNotGetGuessedOntoNewModule()
    {
        var repository = new SymbolArchiveRepository(_root);
        var store = new SchematicModuleLayoutStore(repository);
        var source = Project("instance-a", "port-a", "pin-a");
        store.Save(source, source.Schematic!.Symbols.Single().SymbolId);
        var changed = Project("instance-b", "port-b", "pin-b", sourcePinId: "different-source-pin");

        var result = store.ApplyActive(changed, changed.Schematic!.Symbols.Single().SymbolId);

        Assert.False(result.Applied);
        Assert.Contains("source Port/Pin", result.Warning);
        Assert.Same(changed, result.Project);
        Assert.Empty(result.Project.Connections);
    }

    [Fact]
    public void SavingAgainRetainsHistoryAndOnlyNewestRevisionIsActive()
    {
        var repository = new SymbolArchiveRepository(_root);
        var store = new SchematicModuleLayoutStore(repository);
        var project = Project("instance-a", "port-a", "pin-a");
        store.Save(project, project.Schematic!.Symbols.Single().SymbolId);
        store.Save(project, project.Schematic.Symbols.Single().SymbolId);

        Assert.Equal(2, repository.Load().SchematicLayouts.Count);
        Assert.Single(repository.Load().SchematicLayouts, profile => profile.Active);
    }

    private static ElectricalProject Project(string instanceId, string portId, string pinId,
        string sourcePinId = "source-pin")
    {
        var component = new ComponentInstance { ComponentInstanceId = instanceId,
            ComponentDefinitionId = "source-module", TypeKey = "MODULE", Ports =
            [new ComponentPort { PortId = portId, SourcePortId = "source-port", Name = "P", Pins =
                [new ComponentPin { PinId = pinId, SourcePinId = sourcePinId, PinNumber = "1" }] }] };
        return new ElectricalProject { ProjectId = "test", Components = [component],
            Schematic = new SchematicDocument { Pages = [new SchematicPage { PageId = "page", Title = "First" }],
                Symbols = [SchematicAuthoringService.CreateSymbol(component, "page", new(20, 20))] } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
