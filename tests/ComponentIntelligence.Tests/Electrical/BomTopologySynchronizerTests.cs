using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class BomTopologySynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_UsesInstalledQuantityOnly_AndCreatesRichInstancesWhenIrExists()
    {
        var project = NewProject();
        var row = NewRow("1", "IFM", "O5D100", used: 2, spare: 3);
        var component = NewComponentIr("CMP-O5D100", "IFM", "O5D100", category: "Sensor");
        var lookupCalls = 0;

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) =>
            {
                lookupCalls++;
                return Task.FromResult<ComponentIR?>(component);
            });

        Assert.Equal(1, lookupCalls);
        Assert.Equal(2, result.AddedInstances);
        Assert.Equal(2, result.RichInstances);
        Assert.Equal(0, result.PlaceholderInstances);
        Assert.Equal(0, result.DeferredConnectionMaterialRows);
        Assert.Equal(2, project.Components.Count);
        Assert.All(project.Components, instance => Assert.Equal("CMP-O5D100", instance.ComponentDefinitionId));
        Assert.DoesNotContain(project.Components, instance => instance.DisplayName?.Contains("Spare", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task SynchronizeAsync_MissingIrCreatesVisibleInstalledPlaceholders()
    {
        var project = NewProject();
        var row = NewRow("2", "Acme", "X100", used: 2, spare: 1);

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) => Task.FromResult<ComponentIR?>(null));

        Assert.Equal(2, result.AddedInstances);
        Assert.Equal(0, result.RichInstances);
        Assert.Equal(2, result.PlaceholderInstances);
        Assert.Equal(0, result.DeferredConnectionMaterialRows);
        Assert.Equal(2, project.Components.Count);
        Assert.All(project.Components, instance =>
        {
            Assert.Equal("BOM_ITEM", instance.TypeKey);
            Assert.Contains("Acme X100", instance.DisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task SynchronizeAsync_UnknownUsedQuantityCreatesOneQtyReviewPlaceholder()
    {
        var project = NewProject();
        var row = NewRow("3", "IFM", "TA2115", used: null, spare: 2);
        var lookupCalls = 0;

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) =>
            {
                lookupCalls++;
                return Task.FromResult<ComponentIR?>(null);
            });

        Assert.Equal(1, lookupCalls);
        Assert.Equal(1, result.AddedInstances);
        Assert.Equal(1, result.UnknownQuantityRows);
        var placeholder = Assert.Single(project.Components);
        Assert.Equal("BOM_ITEM_QTY_UNKNOWN", placeholder.TypeKey);
        Assert.Contains("Qty ?", placeholder.DisplayName ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynchronizeAsync_ZeroInstalledQuantityDoesNotPutSpareOnlyRowOnTopology()
    {
        var project = NewProject();
        var row = NewRow("4", "IFM", "EVC001", used: 0, spare: 4);
        var lookupCalls = 0;

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) =>
            {
                lookupCalls++;
                return Task.FromResult<ComponentIR?>(NewComponentIr("CMP-EVC001", "IFM", "EVC001", category: "Cable Assembly"));
            });

        Assert.Equal(0, lookupCalls);
        Assert.Equal(0, result.AddedInstances);
        Assert.Equal(1, result.SkippedSpareOnlyRows);
        Assert.Empty(project.Components);
    }

    [Theory]
    [InlineData("Cable")]
    [InlineData("Cable Assembly")]
    [InlineData("Wire")]
    [InlineData("Wire Harness")]
    [InlineData("Cordset")]
    public async Task SynchronizeAsync_ConnectionMaterialDoesNotBecomeFakeDeviceNode(string category)
    {
        var project = NewProject();
        var row = NewRow("C1", "Vendor", "CABLE-001", used: 3, spare: 1);
        var component = NewComponentIr("CMP-CABLE-001", "Vendor", "CABLE-001", category);

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) => Task.FromResult<ComponentIR?>(component));

        Assert.Equal(0, result.AddedInstances);
        Assert.Equal(0, result.RichInstances);
        Assert.Equal(0, result.PlaceholderInstances);
        Assert.Equal(1, result.DeferredConnectionMaterialRows);
        Assert.Empty(project.Components);
    }

    [Fact]
    public async Task SynchronizeAsync_UnknownQuantityCableIsDeferredInsteadOfShownAsQtyUnknownDevice()
    {
        var project = NewProject();
        var row = NewRow("C2", "Vendor", "WIRE-001", used: null, spare: 0);
        var component = NewComponentIr("CMP-WIRE-001", "Vendor", "WIRE-001", category: "Wire");

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) => Task.FromResult<ComponentIR?>(component));

        Assert.Equal(1, result.UnknownQuantityRows);
        Assert.Equal(1, result.DeferredConnectionMaterialRows);
        Assert.Empty(project.Components);
    }

    [Fact]
    public async Task SynchronizeAsync_UnknownClassificationRemainsVisibleRatherThanBeingGuessedAway()
    {
        var project = NewProject();
        var row = NewRow("U1", "Vendor", "MYSTERY-001", used: 1, spare: 0);
        var component = NewComponentIr("CMP-MYSTERY-001", "Vendor", "MYSTERY-001", category: null);

        var result = await new BomTopologySynchronizer().SynchronizeAsync(
            project,
            [row],
            (_, _, _) => Task.FromResult<ComponentIR?>(component));

        Assert.Equal(1, result.AddedInstances);
        Assert.Equal(1, result.RichInstances);
        Assert.Equal(0, result.DeferredConnectionMaterialRows);
        Assert.Single(project.Components);
    }

    [Fact]
    public async Task SynchronizeAsync_RepeatedSyncIsIdempotentAndDoesNotDuplicateBomInstances()
    {
        var project = NewProject();
        var rows = new[]
        {
            NewRow("5", "IFM", "O5D100", used: 2, spare: 0),
            NewRow("6", "IFM", "TA2115", used: null, spare: 0)
        };
        var synchronizer = new BomTopologySynchronizer();

        var first = await synchronizer.SynchronizeAsync(project, rows, (_, _, _) => Task.FromResult<ComponentIR?>(null));
        var second = await synchronizer.SynchronizeAsync(project, rows, (_, _, _) => Task.FromResult<ComponentIR?>(null));

        Assert.Equal(3, first.AddedInstances);
        Assert.Equal(0, second.AddedInstances);
        Assert.Equal(3, project.Components.Count);
        Assert.Equal(3, project.Components.Select(component => component.ComponentInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task SynchronizeAsync_NewBomRowsMergeIntoSavedProjectWithoutChangingExistingLayoutOrConnections()
    {
        var project = NewProject();
        var originalRow = NewRow("10", "IFM", "AL1342", used: 1, spare: 0);
        var addedRow = NewRow("11", "IFM", "PL1514", used: 1, spare: 0);
        var synchronizer = new BomTopologySynchronizer();

        await synchronizer.SynchronizeAsync(project, [originalRow], (_, _, _) => Task.FromResult<ComponentIR?>(null));
        var original = Assert.Single(project.Components);
        project.TopologyPlacements.Add(new TopologyPlacement
        {
            ObjectId = original.ComponentInstanceId,
            ObjectKind = "Component",
            X = 321,
            Y = 123
        });
        project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = "saved-connection",
            FromEndpointId = "saved-from",
            ToEndpointId = "saved-to"
        });

        var result = await synchronizer.SynchronizeAsync(
            project,
            [originalRow, addedRow],
            (_, _, _) => Task.FromResult<ComponentIR?>(null));

        Assert.Equal(1, result.AddedInstances);
        Assert.Equal(2, project.Components.Count);
        Assert.Contains(project.Components, component =>
            component.DisplayName?.Contains("IFM PL1514", StringComparison.OrdinalIgnoreCase) == true);
        var placement = Assert.Single(project.TopologyPlacements);
        Assert.Equal(original.ComponentInstanceId, placement.ObjectId);
        Assert.Equal(321, placement.X);
        Assert.Equal(123, placement.Y);
        Assert.Equal("saved-connection", Assert.Single(project.Connections).ConnectionId);
    }

    private static ElectricalProject NewProject() => new()
    {
        ProjectId = "TEST-PROJECT"
    };

    private static BomRow NewRow(string id, string manufacturer, string model, int? used, int? spare) => new()
    {
        RowId = id,
        RawManufacturer = manufacturer,
        RawModelOrPartNumber = model,
        Manufacturer = manufacturer,
        ModelOrPartNumber = model,
        UsedQuantity = used,
        SpareQuantity = spare,
        TotalQuantity = used is int installed && spare is int spareCount ? installed + spareCount : null
    };

    private static ComponentIR NewComponentIr(string id, string manufacturer, string model, string? category = null) => new()
    {
        Identity = new ComponentIrIdentity
        {
            ComponentId = id,
            Manufacturer = manufacturer,
            Model = model,
            Mpn = model
        },
        Classification = new ComponentClassification
        {
            Category = category
        },
        Ports =
        [
            new ComponentIntelligence.Contracts.ComponentPort
            {
                PortId = "P1",
                PortType = "M12",
                ConnectorFamily = "M12"
            }
        ]
    };
}
