using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableConnectionSelectionTests
{
    [Fact]
    public void CableSettingsCanSelectWholePortWithoutInventingConductors()
    {
        var project = UnifiedCableSettingsTests.Project();
        project.Connections.Add(new() { ConnectionId = "PORT", FromEndpointId = "A", ToEndpointId = "B", Kind = ConnectionKind.Cable });
        var before = JsonSerializer.Serialize(project);
        Assert.Equal(2, CableConnectionSelection.Build(project).Count);
        var row = Assert.Single(CableConnectionSelection.Build(project, includePorts: true), r => r.ConnectionId == "PORT");
        Assert.True(row.IncludesPort);
        Assert.Contains("A / Port (Port)", row.Label);
        Assert.Contains("B / Port (Port)", row.Label);
        Assert.DoesNotContain("Pin", row.Label);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void DirectMatesMissingAndAmbiguousEndpointsRemainExcluded()
    {
        var project = UnifiedCableSettingsTests.Project();
        project.Connections.Add(new() { ConnectionId = "DIRECT", FromEndpointId = "A", ToEndpointId = "B", Kind = ConnectionKind.DirectMating });
        project.Connections.Add(new() { ConnectionId = "MISSING", FromEndpointId = "A", ToEndpointId = "missing" });
        Assert.Equal(2, CableConnectionSelection.Build(project, includePorts: true).Count);
        project.Components.Add(UnifiedCableSettingsTests.Component("A"));
        Assert.Empty(CableConnectionSelection.Build(project, includePorts: true));
    }

    [Fact]
    public void ExistingOwnershipFilterAndPinOnlyMultiEndSelectionArePreserved()
    {
        var project = UnifiedCableSettingsTests.Project();
        project.Cables.Add(new() { CableInstanceId = "other", CableDefinitionId = "different" });
        project.Connections[1].CableInstanceId = "other";
        Assert.Equal("C1", Assert.Single(CableConnectionSelection.Build(project, "editing")).ConnectionId);
        Assert.False(Assert.Single(CableConnectionSelection.Build(project, "editing")).IncludesPort);
    }

    [Fact]
    public async Task PurchasedPortCableSavesAndReloadsWithoutChangingRouteOrEndpointIdentity()
    {
        var project = UnifiedCableSettingsTests.Project();
        project.Connections.Clear();
        project.Schematic = new() { Pages = [new() { PageId = "page", Title = "Sheet" }],
            Symbols = project.Components.Select((c, i) => SchematicAuthoringService.CreateSymbol(c, "page", new(30 + i * 100, 30))).ToList() };
        var symbols = project.Schematic.Symbols;
        var start = SchematicPortPresentation.ConnectionPoint(project, symbols[0], "A");
        var end = SchematicPortPresentation.ConnectionPoint(project, symbols[1], "B");
        project = new SchematicAuthoringService().DrawWire(project, "page", SchematicAttachment.Port(symbols[0].SymbolId, "A"),
            SchematicAttachment.Port(symbols[1].SymbolId, "B"), start.Y == end.Y ? [start, end] : [start, new(end.X, start.Y), end]);
        var row = Assert.Single(CableConnectionSelection.Build(project, includePorts: true));
        var wireBefore = JsonSerializer.Serialize(project.Schematic!.Wires);
        var connection = project.Connections.Single();
        var identity = (connection.ConnectionId, connection.FromEndpointId, connection.ToEndpointId, connection.NetId);
        var cable = new UnifiedCableSettingsService().ApplyPointToPoint(project, [row.ConnectionId], CableConstructionType.Purchased,
            "W1", "purchased-definition", 2000, false, "EVC014");
        Assert.Empty(cable.CoreAssignments);
        Assert.Empty(project.Nets);
        Assert.Equal(wireBefore, JsonSerializer.Serialize(project.Schematic.Wires));
        var path = Path.Combine(Path.GetTempPath(), "port-purchased-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repository.SaveAsync(project);
            var loaded = (await repository.GetAsync(project.ProjectId))!;
            var restored = Assert.Single(loaded.Connections);
            Assert.Equal(identity, (restored.ConnectionId, restored.FromEndpointId, restored.ToEndpointId, restored.NetId));
            var physical = Assert.Single(loaded.Cables);
            Assert.Equal(cable.CableInstanceId, restored.CableInstanceId);
            Assert.Equal("purchased-definition", physical.CableDefinitionId);
            Assert.Equal("EVC014", physical.DisplayName);
            Assert.Equal("EVC014", SchematicCableLabelPresentation.Resolve(loaded, loaded.Schematic!.Wires[0])!.Text);
            Assert.Equal("W1", physical.ReferenceDesignator);
            Assert.Equal(2000, physical.ProvidedLengthMm);
            Assert.Equal(CableConstructionType.Purchased, physical.CableConstructionType);
            Assert.Empty(physical.CoreAssignments);
            Assert.Equal(wireBefore, JsonSerializer.Serialize(loaded.Schematic!.Wires));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
