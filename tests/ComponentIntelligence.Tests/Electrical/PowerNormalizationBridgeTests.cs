using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Normalization;
using Xunit;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class PowerNormalizationBridgeTests
{
    [Fact]
    public async Task Normalizer_ConvertsCurrentAndPowerUnitsIntoComponentIr()
    {
        var raw = new RawComponentProfile
        {
            Identity = new ComponentIdentity { OfficialManufacturer = "IFM", OfficialModel = "TEST1" },
            Specifications = new[]
            {
                Spec("power.operating_voltage", "Operating voltage", "18...30 V DC"),
                Spec("power.current_consumption", "Current consumption", "< 250 mA"),
                Spec("power.maximum_current", "Maximum current consumption", "500 mA"),
                Spec("power.power_consumption", "Power consumption", "2400 mW")
            }
        };

        var component = await new ComponentNormalizer().NormalizeAsync(raw);

        Assert.Equal(18m, component.Power.OperatingVoltage?.Min);
        Assert.Equal(30m, component.Power.OperatingVoltage?.Max);
        Assert.Equal("DC", component.Power.OperatingVoltage?.Type);
        Assert.Equal(0.25m, component.Power.CurrentConsumptionAmp);
        Assert.Equal(0.5m, component.Power.MaximumCurrentAmp);
        Assert.Equal(2.4m, component.Power.PowerConsumptionWatt);
    }

    [Fact]
    public void Bridge_AttachesDeviceConsumptionToPositivePowerPinWithoutTreatingItAsSourceCapacity()
    {
        var component = new ComponentIR
        {
            Identity = new ComponentIrIdentity { ComponentId = "cmp", Manufacturer = "IFM", Model = "TEST1" },
            Power = new ComponentPower
            {
                OperatingVoltage = new NormalizedVoltage { Min = 18, Max = 30, Type = "DC" },
                CurrentConsumptionAmp = 0.25m,
                MaximumCurrentAmp = 0.5m
            },
            Pins = new[]
            {
                new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "1", Function = "L+", SignalType = "Power", Direction = "Input" },
                new ComponentIntelligence.Contracts.ComponentPin { PinNumber = "3", Function = "L-", SignalType = "Power", Direction = "Input" }
            }
        };

        var instance = new ComponentProjectBridge().CreateInstance(component, "inst", "S1");
        var port = Assert.Single(instance.Ports);
        var plus = Assert.Single(port.Pins, pin => pin.PinNumber == "1");
        var minus = Assert.Single(port.Pins, pin => pin.PinNumber == "3");

        Assert.Equal(PowerRole.Input, plus.Power?.Role);
        Assert.Equal(0.25, plus.Power?.RequiredCurrentAmp);
        Assert.Null(plus.Power?.MaxCurrentAmp);
        Assert.Equal(VoltageType.Dc, plus.Power?.Voltage?.Type);
        Assert.Equal(18, plus.Power?.Voltage?.MinVoltage);
        Assert.Equal(30, plus.Power?.Voltage?.MaxVoltage);
        Assert.Equal(PowerRole.Input, minus.Power?.Role);
        Assert.Null(minus.Power?.RequiredCurrentAmp);
        Assert.Equal(Polarity.Return, minus.Power?.Polarity);
    }

    private static RawSpecification Spec(string key, string name, string value) => new()
    {
        RawName = name,
        RawValue = value,
        ProposedKey = key,
        Evidence = new[]
        {
            new Evidence
            {
                SourceType = ComponentSourceType.ManufacturerDatasheet,
                ExtractionMethod = ExtractionMethod.TableParser,
                RawValue = value,
                RetrievedAt = DateTimeOffset.UtcNow,
                VerificationStatus = VerificationStatus.SingleSource
            }
        }
    };
}
