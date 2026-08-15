using ComponentIntelligence.Contracts;
using Xunit;

namespace ComponentIntelligence.Tests.TaskCoverage;

public sealed class T008Tests
{
    [Fact]
    public void ComponentIR_StoresEverySkeletonSection()
    {
        var port = new ComponentPort { PortId = "P1" };
        var pin = new ComponentPin { PinNumber = "1" };
        var ir = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "acme-ax-100", Manufacturer = "ACME", Model = "AX-100", Mpn = "AX100" },
            Classification = new ComponentClassification { Category = "Sensor", Subcategory = "Proximity" },
            Power = new ComponentPower { OperatingVoltage = new NormalizedVoltage { Min = 18m, Max = 30m, Unit = "V", Type = "DC" } },
            Io = new ComponentIo { OutputType = "PNP" },
            Connector = new ComponentConnector { Family = "M12", Coding = "A", Pins = 4 },
            Ports = [port],
            Pins = [pin],
            Assets = new ComponentAssets
            {
                ProductPageUrl = new Uri("https://example.test/product"), DatasheetUrl = new Uri("https://example.test/datasheet.pdf"),
                ImageUrl = new Uri("https://example.test/image.png"), CadUrl = new Uri("https://example.test/model.step")
            },
            Readiness = new ComponentReadiness { Wiring = default, Topology = default, Validation = default, Drawing = default }
        };

        Assert.Equal("acme-ax-100", ir.Identity.ComponentId);
        Assert.Equal("ACME", ir.Identity.Manufacturer);
        Assert.Equal("AX-100", ir.Identity.Model);
        Assert.Equal("AX100", ir.Identity.Mpn);
        Assert.Equal("Sensor", ir.Classification.Category);
        Assert.Equal("Proximity", ir.Classification.Subcategory);
        Assert.Equal(18m, ir.Power.OperatingVoltage!.Min);
        Assert.Equal(30m, ir.Power.OperatingVoltage.Max);
        Assert.Equal("V", ir.Power.OperatingVoltage.Unit);
        Assert.Equal("DC", ir.Power.OperatingVoltage.Type);
        Assert.Equal("PNP", ir.Io.OutputType);
        Assert.Equal("M12", ir.Connector.Family);
        Assert.Equal("A", ir.Connector.Coding);
        Assert.Equal(4, ir.Connector.Pins);
        Assert.Same(port, Assert.Single(ir.Ports));
        Assert.Same(pin, Assert.Single(ir.Pins));
        Assert.Equal(new Uri("https://example.test/product"), ir.Assets.ProductPageUrl);
        Assert.Equal(new Uri("https://example.test/datasheet.pdf"), ir.Assets.DatasheetUrl);
        Assert.Equal(new Uri("https://example.test/image.png"), ir.Assets.ImageUrl);
        Assert.Equal(new Uri("https://example.test/model.step"), ir.Assets.CadUrl);
        Assert.Equal(default(ReadinessStatus), ir.Readiness.Wiring);
        Assert.Equal(default(ReadinessStatus), ir.Readiness.Topology);
        Assert.Equal(default(ReadinessStatus), ir.Readiness.Validation);
        Assert.Equal(default(ReadinessStatus), ir.Readiness.Drawing);
    }
}
