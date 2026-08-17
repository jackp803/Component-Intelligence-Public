using ClosedXML.Excel;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Search;
using ComponentIntelligence.Verification;
using Xunit;

namespace ComponentIntelligence.Tests.Repository;

public sealed class F0320CentralWorkbookIntegrationTests
{
    [Fact]
    public async Task CompleteF0320_FlowsFromWorkbookThroughSqliteIntoPhysicalTopology()
    {
        var directory = CreateDirectory();
        try
        {
            var workbookPath = Path.Combine(directory, "Component_Intelligence_Database.xlsx");
            CreateWorkbook(workbookPath, includeUnusedPin3: true);
            var sqlite = new SqliteComponentIrRepository(Path.Combine(directory, "component-intelligence.db"));

            var result = await new CentralLibraryComponentLookupService(
                sqlite,
                new WorkbookComponentKnowledgeStore(workbookPath)).SearchAsync("OMRON", "F03-20");
            var cached = await sqlite.FindByIdentityAsync("OMRON", "F03-20");

            Assert.NotNull(result.Result.Component);
            Assert.Contains("CENTRAL_LIBRARY_HYDRATED_LOCAL_SQLITE", result.Result.Issues);
            Assert.NotNull(cached);
            Assert.Equal(2, cached!.Ports.Count);
            Assert.Equal(3, cached.Pins.Count(pin => pin.PortId == "INPUT"));
            Assert.Equal("F03-20-IN-1", cached.Pins.Single(pin => pin.PortId == "INPUT" && pin.PinNumber == "1").PinId);
            Assert.Equal("Unused", cached.Pins.Single(pin => pin.PortId == "INPUT" && pin.PinNumber == "3").PinStatus);
            Assert.DoesNotContain(KnowledgeCompletenessPolicy.Assess(cached), gap => gap.Key == "pins.coverage.INPUT");

            var project = new ElectricalProject { ProjectId = "f03-20-test" };
            var sync = await new BomTopologySynchronizer().SynchronizeAsync(
                project,
                [new BomRow
                {
                    RowId = "F03-20",
                    Manufacturer = "OMRON",
                    ModelOrPartNumber = "F03-20",
                    UsedQuantity = 1,
                    TotalQuantity = 1,
                    SpareQuantity = 0
                }],
                (manufacturer, model, cancellationToken) => sqlite.FindByIdentityAsync(manufacturer, model, cancellationToken));

            var instance = Assert.Single(project.Components);
            Assert.Equal(1, sync.RichInstances);
            Assert.Equal(0, sync.PlaceholderInstances);
            Assert.NotNull(instance.Footprint);
            Assert.Equal(29.1, instance.Footprint!.WidthMm, 6);
            Assert.Equal(17, instance.Footprint.HeightMm, 6);
            Assert.Equal(25, instance.Footprint.DepthMm!.Value, 6);

            var input = Assert.Single(instance.Ports, port => port.Name == "INPUT");
            var output = Assert.Single(instance.Ports, port => port.Name == "OUTPUT");
            Assert.Equal(TopologyScreenSide.Left, TopologyPortGeometry.DetermineScreenSide(input));
            Assert.Equal(TopologyScreenSide.Right, TopologyPortGeometry.DetermineScreenSide(output));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DeclaredThreePinInput_WithOnlyTwoRows_IsIncomplete()
    {
        var directory = CreateDirectory();
        try
        {
            var workbookPath = Path.Combine(directory, "Component_Intelligence_Database.xlsx");
            CreateWorkbook(workbookPath, includeUnusedPin3: false);

            var lookup = await new WorkbookComponentKnowledgeStore(workbookPath)
                .FindByIdentityAsync("OMRON", "F03-20");

            Assert.NotNull(lookup.Component);
            var gap = Assert.Single(
                KnowledgeCompletenessPolicy.Assess(lookup.Component!),
                item => item.Key == "pins.coverage.INPUT");
            Assert.Contains("declares 3 pins", gap.EnglishReason);
            Assert.Contains("contains 2", gap.EnglishReason);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"component-intelligence-f03-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CreateWorkbook(string path, bool includeUnusedPin3)
    {
        using var workbook = new XLWorkbook();
        var components = workbook.AddWorksheet("Components");
        WriteRow(components, 1,
            "ComponentID", "Manufacturer", "Model", "Category", "Description", "Voltage",
            "IOType", "OutputType", "Protocol", "GeometryType", "WidthMm", "HeightMm", "DepthMm",
            "DiameterMm", "MountingType", "DatasheetPath", "ImagePath", "DrawingPath",
            "TopologyStatus", "LayoutStatus", "DatasheetURL");
        WriteRow(components, 2,
            "OMRON-F03-20", "OMRON", "F03-20", "Electrode holder", "Electrode holder",
            "24 V DC", "Digital", "Relay", "", "Rectangular", "29.1", "17", "25", "",
            "Panel", "", "", "", "Ready", "Ready", "");

        var ports = workbook.AddWorksheet("Ports");
        WriteRow(ports, 1,
            "PortID", "ComponentID", "PortName", "PortRole", "Direction", "SignalType", "Voltage",
            "Protocol", "Connector", "ConnectorCoding", "Gender", "PinCount", "ActualPinCount",
            "PinCompleteness", "PhysicalSide", "SourcePage", "Notes");
        WriteRow(ports, 2,
            "INPUT", "OMRON-F03-20", "INPUT", "Sensor Input", "Input", "Digital", "24 V DC",
            "", "Terminal", "", "", "3", includeUnusedPin3 ? "3" : "2",
            includeUnusedPin3 ? "Complete" : "Incomplete", "Left", "", "");
        WriteRow(ports, 3,
            "OUTPUT", "OMRON-F03-20", "OUTPUT", "Control Output", "Output", "Digital", "24 V DC",
            "", "Terminal", "", "", "0", "0", "Complete", "Right", "", "");

        var pins = workbook.AddWorksheet("Pins");
        WriteRow(pins, 1,
            "PinID", "PortID", "PinNumber", "PinName", "PinRole", "Direction", "SignalType",
            "Voltage", "Function", "PinStatus", "SourcePage", "Notes");
        WriteRow(pins, 2, "F03-20-IN-1", "INPUT", "1", "IN+", "Input", "Input", "Digital", "24 V DC", "Input +", "Active", "", "");
        WriteRow(pins, 3, "F03-20-IN-2", "INPUT", "2", "IN-", "Return", "Input", "Digital", "0 V", "Input -", "Active", "", "");
        if (includeUnusedPin3)
            WriteRow(pins, 4, "F03-20-IN-3", "INPUT", "3", "", "", "Input", "Digital", "", "", "Unused", "", "");

        workbook.SaveAs(path);
    }

    private static void WriteRow(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var column = 0; column < values.Length; column++)
            sheet.Cell(row, column + 1).Value = values[column];
    }
}
