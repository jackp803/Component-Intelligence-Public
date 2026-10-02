using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Drawing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;
using System.Text.Json;
using Pin = ComponentIntelligence.Contracts.ComponentPin;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class EndpointVoltageBridgeTests
{
    [Theory]
    [InlineData("+24 VDC", 24)]
    [InlineData("0 VDC", 0)]
    public async Task EndpointVoltageSurvivesSaveReloadAndPlanningProjection(string voltage, double expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"endpoint-voltage-{Guid.NewGuid():N}.db");
        try
        {
            var project = new ElectricalProject { ProjectId = "P", Components = [new ComponentProjectBridge().CreateInstance(Source(voltage), "I")] };
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repository.SaveAsync(project);
            var loaded = await repository.GetAsync("P");
            Assert.NotNull(loaded);
            var input = new DrawingPlanningInputBuilder(new RepresentationPolicy(new NoAssets())).Build(loaded);
            using var json = JsonDocument.Parse(DrawingPlanningJson.Serialize(input));
            var rule = json.RootElement.GetProperty("wiringRules").EnumerateArray().Single(r => r.GetProperty("ruleKind").GetString() == "EndpointPowerEvidence");
            var row = Assert.Single(rule.GetProperty("value").EnumerateArray());
            Assert.Equal(expected, row.GetProperty("voltage").GetProperty("nominalVoltage").GetDouble());
            Assert.Null(loaded.Components[0].Ports[0].Pins[0].PowerDomainId);
            Assert.Empty(loaded.Components[0].PowerConversions);
            Assert.Empty(input.PowerDomains);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    private sealed class NoAssets : IDrawingAssetResolver
    {
        public DrawingAssetResolution? Resolve(string ownerId, DrawingRepresentationRole role) => null;
    }

    private static ComponentIR Source(string? voltage, string direction = "Output") => new()
    {
        Identity = new() { ComponentId = "C", Manufacturer = "Example", Model = "Converter" },
        Power = new() { OperatingVoltage = new() { Min = 28.8m, Max = 67.2m, Type = "DC" } },
        Ports = [new() { PortId = "PORT", VoltageDomain = "24 VDC" }],
        Pins = [new Pin { PinId = "PIN", PortId = "PORT", PinNumber = "2", Function = "Power", SignalType = "Power", Direction = direction, VoltageDomain = voltage }]
    };

    [Theory]
    [InlineData("+24 VDC", 24)]
    [InlineData("0 VDC", 0)]
    [InlineData("0 V return", 0)]
    [InlineData("-12 V DC", -12)]
    public void ExplicitPinVoltageOverridesComponentInputRange(string voltage, double expected)
    {
        var instance = new ComponentProjectBridge().CreateInstance(Source(voltage), "I");
        var pin = Assert.Single(Assert.Single(instance.Ports).Pins);
        Assert.Equal(expected, pin.Power?.Voltage?.NominalVoltage);
        Assert.Equal("PIN", pin.SourcePinId);
        Assert.Null(pin.PowerDomainId);
        Assert.Empty(instance.PowerConversions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown")]
    [InlineData("24 VDC TBD")]
    public void OutputNeverInheritsComponentInputVoltageOrPortVoltage(string? voltage)
    {
        var instance = new ComponentProjectBridge().CreateInstance(Source(voltage), "I");
        Assert.Null(instance.Ports[0].Pins[0].Power?.Voltage);
    }

    [Fact]
    public void InputWithExplicitUnparseableVoltageDoesNotFallBackToDeviceRange()
    {
        var instance = new ComponentProjectBridge().CreateInstance(Source("TBD", "Input"), "I");
        Assert.Null(instance.Ports[0].Pins[0].Power?.Voltage);
    }

    [Fact]
    public void SyncUsesExactTypedSourceIdentityNotRepeatedPinNumber()
    {
        var source = Source("+24 VDC");
        var target = new ComponentProjectBridge().CreateInstance(source, "I");
        var pin = new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = "runtime-existing", SourcePinId = "PIN", PinNumber = "99",
            Power = new() { Voltage = new() { MinVoltage = 28.8, MaxVoltage = 67.2 } } };
        target.Ports[0].Pins.Clear(); target.Ports[0].Pins.Add(pin);
        new ComponentInstanceKnowledgeSynchronizer().Apply(target, source, true);
        Assert.Single(target.Ports[0].Pins);
        Assert.Equal("runtime-existing", pin.PinId);
        Assert.Equal(24, pin.Power?.Voltage?.NominalVoltage);
    }

    [Fact]
    public void DuplicateSourcePortIdentityDoesNotThrowOrRefreshPower()
    {
        var source = Source("+24 VDC");
        var target = new ComponentProjectBridge().CreateInstance(source, "I");
        target.Ports.Add(new() { PortId = "duplicate", SourcePortId = "PORT", Name = "duplicate" });
        target.Ports[0].Pins[0].Power = null;
        new ComponentInstanceKnowledgeSynchronizer().Apply(target, source, true);
        Assert.Null(target.Ports[0].Pins[0].Power);
    }

    [Fact]
    public void MatchingPinNumberAloneCannotRefreshPower()
    {
        var source = Source("+24 VDC");
        var target = new ComponentProjectBridge().CreateInstance(source, "I");
        target.Ports[0].Pins.Clear();
        var unproven = new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = "opaque", PinNumber = "2" };
        target.Ports[0].Pins.Add(unproven);
        new ComponentInstanceKnowledgeSynchronizer().Apply(target, source, true);
        Assert.Null(unproven.Power);
        Assert.Null(unproven.SourcePinId);
    }

    [Theory]
    [InlineData("33.6-62.4 VDC", 33.6, 62.4)]
    [InlineData("33.6..62.4 VDC", 33.6, 62.4)]
    public void ExplicitEndpointRangeIsPreserved(string text, double min, double max)
    {
        var pin = new ComponentProjectBridge().CreateInstance(Source(text, "Input"), "I").Ports[0].Pins[0];
        Assert.Equal(min, pin.Power?.Voltage?.MinVoltage);
        Assert.Equal(max, pin.Power?.Voltage?.MaxVoltage);
    }
}
