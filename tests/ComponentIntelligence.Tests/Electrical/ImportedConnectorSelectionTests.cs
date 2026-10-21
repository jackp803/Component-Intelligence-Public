using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class ImportedConnectorSelectionTests
{
    [Fact]
    public void ImportedElectricalCapabilitiesSurviveInstanceApplyAndReconstruction()
    {
        var template = new ArchivedCableTemplate { TemplateId = "new", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Manufacturing = new() { InventoryOnly = true }, Ports = [new() { PortId = "A", Name = "P1",
                Pins = [new() { PinId = "target", PinNumber = "1" }] }] };
        var service = new CableManufacturingEditorService(); var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        var draft = service.Prepare(cable);
        service.SetConnector(draft, "A", new() { PortId = "J", Name = "J", Pins = [new() {
            PinId = "source", PinNumber = "A-7", Function = "SENSE", Protocol = "ANALOG", SignalStandardRaw = "0-10 V" }] });
        var next = service.ApplyToInstance(cable, draft);
        var pin = next.ArchivedCable!.Ports[0].Pins[0];
        Assert.Equal("ANALOG", pin.Protocol); Assert.Equal("0-10 V", pin.SignalStandardRaw);
        var reopened = service.Prepare(next).Ends[0].Pins[0];
        Assert.Equal("ANALOG", reopened.Protocol); Assert.Equal("0-10 V", reopened.SignalStandardRaw);
        Assert.Equal(cable.ArchivedCable!.Ports[0].Pins[0].PinId, pin.PinId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnboundCableEndAdoptsImportedPinFunctionsWithoutCopyingIdentities(bool visual)
    {
        var template = new ArchivedCableTemplate { TemplateId = "new", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Manufacturing = new() { InventoryOnly = visual }, Ports = [new() { PortId = "A", Name = "P1",
                Pins = [new() { PinId = "target", PinNumber = "1" }] }] };
        var draft = new CableManufacturingEditorService().Prepare(template);
        var source = new ComponentPort { PortId = "imported", Name = "J1", Connector = new() {
            ConnectorId = "custom", Family = "Custom", PinCount = 1 }, Pins = [new() { PinId = "source", PinNumber = "A-7", Function = "SENSE", Protocol = "ANALOG" }] };
        new CableManufacturingEditorService().SetConnector(draft, "A", source);
        var pin = Assert.Single(draft.Ends[0].Pins);
        Assert.Equal("target", pin.PinId); Assert.Equal("A-7", pin.PinNumber); Assert.Equal("SENSE", pin.Function); Assert.Equal("ANALOG", pin.Protocol);
        Assert.Equal("SENSE", CableManufacturingCells.Get(draft, draft.Rows[0], "A").Function);
        Assert.Equal("source", source.Pins[0].PinId);
    }

    [Fact]
    public void InventoryColumnsAreNotMistakenForAnExistingInternalConnection()
    {
        var template = new ArchivedCableTemplate { TemplateId = "new", TemplateRevision = "r1", AssetSha256 = new('a', 64),
            Manufacturing = new() { InventoryOnly = true }, Ports = new[] { "A", "B" }.Select(id => new ComponentPort {
                PortId = id, Name = id, Pins = [new() { PinId = id + "1", PinNumber = "1" }] }).ToList() };
        var draft = new CableManufacturingEditorService().Prepare(template);
        var source = new ComponentPort { PortId = "J", Name = "J", Pins = [new() { PinId = "source", PinNumber = "A-7", Function = "SENSE" }] };
        new CableManufacturingEditorService().SetConnector(draft, "A", source);
        Assert.Equal("A-7", draft.Ends[0].Pins[0].PinNumber);
        Assert.Empty(CableWiringEditorService.Mapping(draft));
        Assert.Equal("B1", draft.Ends[1].Pins[0].PinId);
    }
}
