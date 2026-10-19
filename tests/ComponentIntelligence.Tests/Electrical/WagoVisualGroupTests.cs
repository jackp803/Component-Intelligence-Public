using System.Reflection;
using System.Text.Json;
using ComponentIntelligence.Electrical.Bom;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class WagoVisualGroupTests
{
    private static ElectricalProject Group(ElectricalProject project, params string[] ids) => Invoke("GroupWagoSymbols", project, ids);
    private static ElectricalProject Invoke(string name, ElectricalProject project, object argument)
    {
        var method = typeof(SchematicAuthoringService).GetMethod(name); Assert.NotNull(method);
        try { return (ElectricalProject)method.Invoke(new SchematicAuthoringService(), [project, argument])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
    }

    [Fact]
    public void GroupAndDissolveNeverMergeElectricalOwnersReferencesOrBom()
    {
        var project = Fixture();
        project.BomItems.Add(new() { Row = new() { RowId = "wago-bom", Manufacturer = "WAGO", ModelOrPartNumber = "221-2411", UsedQuantity = 3 },
            ComponentDefinitionId = "wago-221-2411", ComponentInstanceIds = ["w1", "w2", "w3"] });
        var before = JsonSerializer.Serialize(project); var history = new ProjectMutationHistory();
        history.RecordBeforeMutation(project, "Group WAGO");
        var grouped = Group(project, "w1", "w2", "w3");
        Assert.Equal(JsonSerializer.Serialize(project.Components), JsonSerializer.Serialize(grouped.Components));
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(grouped.Connections));
        Assert.Equal(JsonSerializer.Serialize(project.BomItems), JsonSerializer.Serialize(grouped.BomItems));
        Assert.Equal(JsonSerializer.Serialize(new DerivedBomEngine().Build(project)), JsonSerializer.Serialize(new DerivedBomEngine().Build(grouped)));
        Assert.Equal(project.Schematic!.Wires.Select(w => (w.WireId, w.ConnectionId, w.Start, w.End)),
            grouped.Schematic!.Wires.Select(w => (w.WireId, w.ConnectionId, w.Start, w.End)));
        Assert.Equal(3, grouped.Schematic.Symbols.Count);
        Assert.NotNull(Assert.Single(grouped.Schematic.Symbols.Select(s => s.GetType().GetProperty("WagoVisualGroupId")!.GetValue(s)).Distinct()));
        var rows = grouped.Schematic.Symbols.OrderBy(s => s.Position.Y).ToArray();
        Assert.Single(rows.Select(s => s.Position.X).Distinct());
        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second.Position.Y >= pair.First.Position.Y + pair.First.Height));
        foreach (var wire in grouped.Schematic.Wires) {
            Assert.Equal(SchematicAuthoringService.AnchorPoint(rows.Single(s => s.SymbolId == wire.Start.SymbolId), wire.Start.EndpointId!), wire.Points[0]);
            Assert.Equal(project.Schematic.Wires.Single(w => w.WireId == wire.WireId).Points[^1], wire.Points[^1]); }
        Assert.True(history.TryUndo(grouped, out var undone, out _)); Assert.Equal(before, JsonSerializer.Serialize(undone));
        Assert.True(history.TryRedo(undone, out var redone, out _)); Assert.Equal(JsonSerializer.Serialize(grouped), JsonSerializer.Serialize(redone));
        var dissolved = Invoke("DissolveWagoGroup", grouped, "w2");
        Assert.Equal(JsonSerializer.Serialize(grouped.Schematic.Wires), JsonSerializer.Serialize(dissolved.Schematic!.Wires));
        Assert.All(dissolved.Schematic.Symbols, s => Assert.Null(s.GetType().GetProperty("WagoVisualGroupId")!.GetValue(s)));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData("model")] [InlineData("mixed-page")] [InlineData("locked")]
    [InlineData("wire-locked")] [InlineData("single")] [InlineData("duplicate")]
    public void UnsupportedOrUnsafeSelectionsFailAtomically(string invalid)
    {
        var project = Fixture();
        if (invalid == "model") project.Components[1].DisplayName = "WAGO 221-24110";
        if (invalid == "mixed-page") project.Schematic!.Symbols[1] = project.Schematic.Symbols[1] with { PageId = "other" };
        if (invalid == "locked") project.Schematic!.Symbols[1] = project.Schematic.Symbols[1] with { Locked = true };
        if (invalid == "wire-locked") project.Schematic!.Wires[1] = project.Schematic.Wires[1] with { Locked = true };
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => Group(project, invalid == "single" ? ["w1"] : invalid == "duplicate" ? ["w1", "w1"] : ["w1", "w2"]));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public async Task WholeGroupMovesAndReloadsWithoutChangingUnrelatedObjects()
    {
        var grouped = Group(Fixture(), "w1", "w2", "w3"); var service = new SchematicAuthoringService();
        var first = grouped.Schematic!.Symbols.Single(s => s.SymbolId == "w1");
        var moved = service.TransformSymbol(grouped, "w1", new(first.Position.X + 30, first.Position.Y + 10), 0);
        foreach (var old in grouped.Schematic.Symbols)
            Assert.Equal(new(old.Position.X + 30, old.Position.Y + 10), moved.Schematic!.Symbols.Single(s => s.SymbolId == old.SymbolId).Position);
        Assert.Throws<InvalidOperationException>(() => service.TransformSymbol(grouped, "w1", first.Position, 90));
        Assert.Throws<InvalidOperationException>(() => service.MoveSymbolsToPage(grouped, ["w1"], "other"));
        Assert.Throws<InvalidOperationException>(() => service.ResizeGenericSymbol(grouped, "w1", 60, 40));
        Assert.Throws<InvalidOperationException>(() => service.DeleteRepresentations(grouped, ["w1"], true));
        var path = Path.Combine(Path.GetTempPath(), "wago-group-" + Guid.NewGuid().ToString("N") + ".db");
        try { var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path); await repository.SaveAsync(moved);
            var loaded = (await repository.GetAsync(moved.ProjectId))!;
            Assert.Equal(JsonSerializer.Serialize(moved.Schematic), JsonSerializer.Serialize(loaded.Schematic));
            SchematicAuthoringService.Validate(loaded); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    internal static ElectricalProject Fixture()
    {
        var project = new ElectricalProject { ProjectId = "wago", Schematic = new() { Pages = [new() { PageId = "page", Title = "WAGO" }, new() { PageId = "other", Title = "Other" }] } };
        var author = new SchematicAuthoringService();
        for (var i = 1; i <= 3; i++) {
            var component = new ComponentInstance { ComponentInstanceId = "w" + i, ComponentDefinitionId = "wago-221-2411", TypeKey = "SPLICE",
                DisplayName = "WAGO 221-2411", ReferenceDesignator = "X" + i,
                Ports = [new() { PortId = "port" + i, Name = "SPLICE", Pins = [new() { PinId = $"p{i}1", PinNumber = "1", Function = "A" + i }, new() { PinId = $"p{i}2", PinNumber = "2", Function = "B" + i }] }] };
            project.Components.Add(component);
            var symbol = SchematicAuthoringService.CreateSymbol(component, "page", new(100 + i * 12, 40 + i * 32)) with { SymbolId = "w" + i, CollapsedPortIds = [] };
            symbol = symbol with { Anchors = symbol.Anchors.Select((a, n) => a with { Direction = n == 0 ? "Left" : "Right", Position = new(n == 0 ? 0 : 50, 15) }).ToList() };
            project.Schematic!.Symbols.Add(symbol);
            var start = SchematicAuthoringService.AnchorPoint(symbol, $"p{i}1");
            project = author.DrawWire(project, "page", SchematicAttachment.Pin(symbol.SymbolId, $"p{i}1"), SchematicAttachment.Free(), [start, new(20, start.Y)]);
        }
        return project;
    }

    [Fact]
    public void RotatedInputReferencesAndGeometryGuardsStayIndependent()
    {
        var author = new SchematicAuthoringService(); var project = Fixture();
        project.Schematic!.Symbols[1] = project.Schematic.Symbols[1] with { Rotation = 90 };
        var grouped = Group(project, "w1", "w2"); var before = JsonSerializer.Serialize(grouped);
        Assert.All(grouped.Schematic!.Symbols.Where(s => s.WagoVisualGroupId is not null), s => {
            Assert.Equal(24, s.Width); Assert.Equal(18, s.Height); Assert.Equal(0, s.Rotation); });
        Assert.Throws<InvalidOperationException>(() => author.MovePinToEdge(grouped, "w1", "p11", "Top", 8));
        Assert.Throws<InvalidOperationException>(() => author.MovePortToEdge(grouped, "w1", "port1", "Top", 8));
        Assert.Throws<InvalidOperationException>(() => author.TogglePortCollapsed(grouped, "w1", "port1"));
        Assert.Throws<InvalidOperationException>(() => author.SetPortFanout(grouped, "w1", "port1", true, 10, 10));
        Assert.Equal(before, JsonSerializer.Serialize(grouped));
        var symbol = grouped.Schematic.Symbols.Single(s => s.SymbolId == "w1");
        var renamed = SchematicInspectorEdit.ApplyModule(grouped, "w1", new("X-Independent", symbol.Position.X, symbol.Position.Y, 24, 18, 0, false, null));
        Assert.Equal("X-Independent", renamed.Components[0].ReferenceDesignator);
        Assert.Equal("X2", renamed.Components[1].ReferenceDesignator);
        Assert.Equal(JsonSerializer.Serialize(grouped.Schematic.Wires), JsonSerializer.Serialize(renamed.Schematic!.Wires));
        Assert.Equal(JsonSerializer.Serialize(grouped.Schematic.Symbols.Single(s => s.SymbolId == "w3")),
            JsonSerializer.Serialize(renamed.Schematic.Symbols.Single(s => s.SymbolId == "w3")));
        Assert.Equal(JsonSerializer.Serialize(grouped.Schematic.Wires[2]), JsonSerializer.Serialize(renamed.Schematic.Wires[2]));
    }

    [Fact]
    public void OverlappingGroupOrUnrelatedRouteRejectsWithoutMutation()
    {
        var project = Fixture(); var first = project.Schematic!.Symbols[0];
        project.Schematic.Symbols[2] = project.Schematic.Symbols[2] with { Position = new(first.Position.X + 3, first.Position.Y - 8) };
        var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => Group(project, "w1", "w2"));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ExistingEngineeringConnectionAndNonmemberStayAttachedAfterGroupingAndMove()
    {
        var project = Fixture(); var service = new SchematicAuthoringService();
        var start = SchematicAuthoringService.AnchorPoint(project.Schematic!.Symbols[0], "p12");
        var end = SchematicAuthoringService.AnchorPoint(project.Schematic.Symbols[2], "p32");
        project = service.DrawWire(project, "page", SchematicAttachment.Pin("w1", "p12"), SchematicAttachment.Pin("w3", "p32"),
            [start, new(260, start.Y), new(260, end.Y), end]);
        Assert.NotEmpty(project.Connections);
        var connections = JsonSerializer.Serialize(project.Connections);
        var unrelatedSymbol = JsonSerializer.Serialize(project.Schematic!.Symbols[2]);
        var unrelatedWire = JsonSerializer.Serialize(project.Schematic.Wires[2]);
        var grouped = Group(project, "w1", "w2");
        var first = grouped.Schematic!.Symbols[0];
        var moved = service.TransformSymbol(grouped, first.SymbolId, new(first.Position.X - 10, first.Position.Y + 5), 0);
        Assert.Equal(connections, JsonSerializer.Serialize(moved.Connections));
        Assert.Equal(unrelatedSymbol, JsonSerializer.Serialize(moved.Schematic!.Symbols[2]));
        Assert.Equal(unrelatedWire, JsonSerializer.Serialize(moved.Schematic.Wires[2]));
        var wire = moved.Schematic.Wires.Last();
        Assert.Equal(project.Schematic.Wires.Last().ConnectionId, wire.ConnectionId);
        Assert.Equal(SchematicAuthoringService.AnchorPoint(moved.Schematic.Symbols[0], "p12"), wire.Points[0]);
        Assert.Equal(end, wire.Points[^1]);
    }

    [Fact]
    public void ExistingPortWireSurvivesExpandedGroupAndDissolve()
    {
        var project = Fixture(); var service = new SchematicAuthoringService();
        project = service.DeleteWires(project, [project.Schematic!.Wires[0].WireId]);
        project = service.TogglePortCollapsed(project, "w1", "port1");
        var symbol = project.Schematic!.Symbols[0]; var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var point = SchematicPortPresentation.GroupContact(project.Schematic, symbol, owner, owner.Ports[0],
            SchematicPortPresentation.GenericBodyBounds(project.Schematic, symbol, owner)).Position;
        project = service.DrawWire(project, "page", SchematicAttachment.Port("w1", "port1"), SchematicAttachment.Free(),
            [point, new(point.X, 30), new(250, 30)]);
        var wire = project.Schematic!.Wires.Last();
        var grouped = Group(project, "w1", "w2");
        symbol = grouped.Schematic!.Symbols[0]; owner = SchematicSymbolOwner.Resolve(grouped, symbol);
        point = SchematicPortPresentation.GroupContact(grouped.Schematic, symbol, owner, owner.Ports[0],
            SchematicPortPresentation.GenericBodyBounds(grouped.Schematic, symbol, owner)).Position;
        Assert.Equal(point, grouped.Schematic.Wires.Last().Points[0]);
        Assert.Equal(wire.Start, grouped.Schematic.Wires.Last().Start);
        Assert.Equal(wire.Points[^1], grouped.Schematic.Wires.Last().Points[^1]);
        var dissolved = service.DissolveWagoGroup(grouped, "w1");
        Assert.Equal(JsonSerializer.Serialize(grouped.Schematic.Wires), JsonSerializer.Serialize(dissolved.Schematic!.Wires));
    }
}
