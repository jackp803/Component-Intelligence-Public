using System.Text.Json;
using ClosedXML.Excel;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Repository;

public sealed class WorkbookPowerEvidenceIngestionTests
{
    private static readonly string[] ConversionHeaders =
    [
        "ComponentID",
        "ConversionID",
        "InputPowerDomainID",
        "OutputPowerDomainID",
        "InputPortIDs",
        "InputPinIDs",
        "OutputPortIDs",
        "OutputPinIDs"
    ];

    [Fact]
    public async Task LegacyWorkbookWithoutNewColumnsOrSheet_LoadsBackwardCompatibly()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(path, includePowerDomainColumns: false, conversionRows: null);

            var lookup = await new WorkbookComponentKnowledgeStore(path).FindByIdentityAsync("ACME", "PWR-1");

            var component = Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(lookup.Component);
            var port = Assert.Single(component.Ports);
            var pin = Assert.Single(component.Pins);
            Assert.Equal("24 V DC", port.VoltageDomain);
            Assert.Equal("24 V DC", pin.VoltageDomain);
            Assert.Null(port.PowerDomainId);
            Assert.Null(pin.PowerDomainId);
            Assert.Empty(component.PowerConversions);
            Assert.DoesNotContain(lookup.Diagnostics,
                item => item.StartsWith("CENTRAL_WORKBOOK_POWER_CONVERSION_", StringComparison.Ordinal));
        }
        finally
        {
            DeletePath(path);
        }
    }

    [Fact]
    public async Task ExplicitPortAndPinPowerDomainId_CopyExactly_WithoutReinterpretingVoltage()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(
                path,
                includePowerDomainColumns: true,
                conversionRows: null,
                portPowerDomainId: "PWR-DOMAIN-24A",
                pinPowerDomainId: "PWR-DOMAIN-24A");

            var lookup = await new WorkbookComponentKnowledgeStore(path).FindByIdentityAsync("ACME", "PWR-1");
            var component = Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(lookup.Component);

            var port = Assert.Single(component.Ports);
            var pin = Assert.Single(component.Pins);
            Assert.Equal("PWR-DOMAIN-24A", port.PowerDomainId);
            Assert.Equal("PWR-DOMAIN-24A", pin.PowerDomainId);
            Assert.Equal("24 V DC", port.VoltageDomain);
            Assert.Equal("24 V DC", pin.VoltageDomain);
        }
        finally
        {
            DeletePath(path);
        }
    }

    [Fact]
    public async Task CompletePowerConversion_MapsExactEngineeringIds_AndNormalizesListsOrdinally()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(path, includePowerDomainColumns: false,
            [
                ["ACME-PWR-1", "CONV-1", "DOMAIN-IN", "DOMAIN-OUT", "P2; P1;P2;;", "PIN-B;PIN-A;PIN-A", "P4;P3", "PIN-D; PIN-C;PIN-D"]
            ]);

            var lookup = await new WorkbookComponentKnowledgeStore(path).FindByIdentityAsync("ACME", "PWR-1");
            var conversion = Assert.Single(Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(lookup.Component).PowerConversions);

            Assert.Equal("CONV-1", conversion.ConversionId);
            Assert.Equal("DOMAIN-IN", conversion.InputPowerDomainId);
            Assert.Equal("DOMAIN-OUT", conversion.OutputPowerDomainId);
            Assert.Equal(["P1", "P2"], conversion.InputPortIds);
            Assert.Equal(["PIN-A", "PIN-B"], conversion.InputPinIds);
            Assert.Equal(["P3", "P4"], conversion.OutputPortIds);
            Assert.Equal(["PIN-C", "PIN-D"], conversion.OutputPinIds);
        }
        finally
        {
            DeletePath(path);
        }
    }

    [Fact]
    public async Task IncompletePowerConversion_IsPreservedIncomplete_WithoutRepair()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(path, includePowerDomainColumns: false,
            [
                ["ACME-PWR-1", "CONV-INCOMPLETE", "", "DOMAIN-OUT", "INPUT-PORT", "", "", ""]
            ]);

            var lookup = await new WorkbookComponentKnowledgeStore(path).FindByIdentityAsync("ACME", "PWR-1");
            var conversion = Assert.Single(Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(lookup.Component).PowerConversions);

            Assert.Equal("CONV-INCOMPLETE", conversion.ConversionId);
            Assert.Null(conversion.InputPowerDomainId);
            Assert.Equal("DOMAIN-OUT", conversion.OutputPowerDomainId);
            Assert.Equal(["INPUT-PORT"], conversion.InputPortIds);
            Assert.Empty(conversion.OutputPortIds);
        }
        finally
        {
            DeletePath(path);
        }
    }

    [Fact]
    public async Task DuplicateConflictingConversionIds_AreBothPreserved_ForDownstreamFailClosedDetection()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(path, includePowerDomainColumns: false,
            [
                ["ACME-PWR-1", "CONV-DUP", "DOMAIN-IN", "DOMAIN-OUT-B", "", "", "", ""],
                ["ACME-PWR-1", "CONV-DUP", "DOMAIN-IN", "DOMAIN-OUT-A", "", "", "", ""]
            ]);

            var lookup = await new WorkbookComponentKnowledgeStore(path).FindByIdentityAsync("ACME", "PWR-1");
            var conversions = Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(lookup.Component).PowerConversions;

            Assert.Equal(2, conversions.Count);
            Assert.All(conversions, item => Assert.Equal("CONV-DUP", item.ConversionId));
            Assert.Equal(["DOMAIN-OUT-A", "DOMAIN-OUT-B"], conversions.Select(item => item.OutputPowerDomainId).ToArray());
        }
        finally
        {
            DeletePath(path);
        }
    }

    [Fact]
    public async Task MissingOrUnknownConversionOwner_IsNotReassigned_AndProducesDeterministicDiagnostics()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(path, includePowerDomainColumns: false,
            [
                ["UNKNOWN-COMPONENT", "CONV-Z", "DOMAIN-IN", "DOMAIN-OUT", "", "", "", ""],
                ["", "CONV-A", "DOMAIN-IN", "DOMAIN-OUT", "", "", "", ""]
            ]);

            var lookup = await new WorkbookComponentKnowledgeStore(path).FindByIdentityAsync("ACME", "PWR-1");
            var component = Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(lookup.Component);

            Assert.Empty(component.PowerConversions);
            var diagnostics = lookup.Diagnostics
                .Where(item => item.StartsWith("CENTRAL_WORKBOOK_POWER_CONVERSION_COMPONENT_UNRESOLVED", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, diagnostics.Length);
            Assert.Equal(
                "CENTRAL_WORKBOOK_POWER_CONVERSION_COMPONENT_UNRESOLVED:ComponentID=<blank>;ConversionID=CONV-A",
                diagnostics[0]);
            Assert.Equal(
                "CENTRAL_WORKBOOK_POWER_CONVERSION_COMPONENT_UNRESOLVED:ComponentID=UNKNOWN-COMPONENT;ConversionID=CONV-Z",
                diagnostics[1]);
        }
        finally
        {
            DeletePath(path);
        }
    }

    [Fact]
    public async Task ConversionRowAndIdListPermutation_ProducesIdenticalLogicalComponentOutput()
    {
        var firstPath = CreatePath();
        var secondPath = CreatePath();
        try
        {
            CreateWorkbook(firstPath, includePowerDomainColumns: false,
            [
                ["ACME-PWR-1", "CONV-B", "DOMAIN-B-IN", "DOMAIN-B-OUT", "P2;P1", "B2;B1", "", ""],
                ["ACME-PWR-1", "CONV-A", "DOMAIN-A-IN", "DOMAIN-A-OUT", "A2;A1", "", "O2;O1", ""]
            ]);
            CreateWorkbook(secondPath, includePowerDomainColumns: false,
            [
                ["ACME-PWR-1", "CONV-A", "DOMAIN-A-IN", "DOMAIN-A-OUT", "A1; A2;A1", "", "O1;O2", ""],
                ["ACME-PWR-1", "CONV-B", "DOMAIN-B-IN", "DOMAIN-B-OUT", "P1; P2", "B1;B2", "", ""]
            ]);

            var first = Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(
                (await new WorkbookComponentKnowledgeStore(firstPath).FindByIdentityAsync("ACME", "PWR-1")).Component);
            var second = Assert.IsType<ComponentIntelligence.Contracts.ComponentIR>(
                (await new WorkbookComponentKnowledgeStore(secondPath).FindByIdentityAsync("ACME", "PWR-1")).Component);

            Assert.Equal(
                JsonSerializer.Serialize(first.PowerConversions),
                JsonSerializer.Serialize(second.PowerConversions));
        }
        finally
        {
            DeletePath(firstPath);
            DeletePath(secondPath);
        }
    }

    [Fact]
    public async Task ListAsync_UsesSameOptionalPowerConversionContract()
    {
        var path = CreatePath();
        try
        {
            CreateWorkbook(path, includePowerDomainColumns: true,
            [
                ["ACME-PWR-1", "CONV-LIST", "DOMAIN-IN", "DOMAIN-OUT", "", "", "", ""]
            ],
            portPowerDomainId: "DOMAIN-IN",
            pinPowerDomainId: "DOMAIN-IN");

            var components = await new WorkbookComponentKnowledgeStore(path).ListAsync();
            var component = Assert.Single(components);

            Assert.Equal("DOMAIN-IN", Assert.Single(component.Ports).PowerDomainId);
            Assert.Equal("DOMAIN-IN", Assert.Single(component.Pins).PowerDomainId);
            Assert.Equal("CONV-LIST", Assert.Single(component.PowerConversions).ConversionId);
        }
        finally
        {
            DeletePath(path);
        }
    }

    private static string CreatePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"component-intelligence-power-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "Component_Intelligence_Database.xlsx");
    }

    private static void DeletePath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static void CreateWorkbook(
        string path,
        bool includePowerDomainColumns,
        IReadOnlyList<string[]>? conversionRows,
        string? portPowerDomainId = null,
        string? pinPowerDomainId = null)
    {
        using var workbook = new XLWorkbook();

        var components = workbook.AddWorksheet("Components");
        WriteRow(components, 1, "ComponentID", "Manufacturer", "Model", "Voltage", "TopologyStatus");
        WriteRow(components, 2, "ACME-PWR-1", "ACME", "PWR-1", "24 V DC", "Ready");

        var ports = workbook.AddWorksheet("Ports");
        if (includePowerDomainColumns)
        {
            WriteRow(ports, 1, "PortID", "ComponentID", "PortName", "PortRole", "Direction", "SignalType", "Voltage", "PowerDomainId", "PinCount");
            WriteRow(ports, 2, "PWR", "ACME-PWR-1", "PWR", "Power", "Input", "Power", "24 V DC", portPowerDomainId ?? "", "1");
        }
        else
        {
            WriteRow(ports, 1, "PortID", "ComponentID", "PortName", "PortRole", "Direction", "SignalType", "Voltage", "PinCount");
            WriteRow(ports, 2, "PWR", "ACME-PWR-1", "PWR", "Power", "Input", "Power", "24 V DC", "1");
        }

        var pins = workbook.AddWorksheet("Pins");
        if (includePowerDomainColumns)
        {
            WriteRow(pins, 1, "PinID", "PortID", "PinNumber", "PinName", "PinRole", "Direction", "SignalType", "Voltage", "PowerDomainId", "Function", "PinStatus");
            WriteRow(pins, 2, "ACME-PWR-1-PWR-1", "PWR", "1", "L+", "Power", "Input", "Power", "24 V DC", pinPowerDomainId ?? "", "Supply", "Used");
        }
        else
        {
            WriteRow(pins, 1, "PinID", "PortID", "PinNumber", "PinName", "PinRole", "Direction", "SignalType", "Voltage", "Function", "PinStatus");
            WriteRow(pins, 2, "ACME-PWR-1-PWR-1", "PWR", "1", "L+", "Power", "Input", "Power", "24 V DC", "Supply", "Used");
        }

        if (conversionRows is not null)
        {
            var conversions = workbook.AddWorksheet("PowerConversions");
            WriteRow(conversions, 1, ConversionHeaders);
            for (var index = 0; index < conversionRows.Count; index++)
                WriteRow(conversions, index + 2, conversionRows[index]);
        }

        workbook.SaveAs(path);
    }

    private static void WriteRow(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var column = 0; column < values.Length; column++)
            sheet.Cell(row, column + 1).Value = values[column];
    }
}
