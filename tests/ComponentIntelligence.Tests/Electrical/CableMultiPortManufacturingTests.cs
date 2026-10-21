using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableMultiPortManufacturingTests
{
    [Fact]
    public void MappingOverrideDoesNotReuseTemplateTableWhenInstanceMetadataIsAbsent()
    {
        var source = CableManufacturingEditorTests.Template(3);
        var service = new CableManufacturingEditorService(); var draft = service.Prepare(source);
        draft.Rows[0].ToSourcePinId = "p2.1";
        var cable = ArchivedCableInstanceFactory.Create(service.Apply(source, draft), CableConstructionType.Unknown);
        cable.ArchivedCable = cable.ArchivedCable! with { Manufacturing = null };
        var changed = ArchivedCableInstanceFactory.WithMappingOverride(cable, [new("p1.1", "p3.1")]);
        var effective = CableManufacturingEditorService.EffectiveTemplate(changed);
        CableManufacturingEditorService.ValidateMetadata(effective);
        Assert.Null(effective.Manufacturing!.Rows);
    }

    [Fact]
    public void LegacyPartialRowRetainsUnboundDestinationText()
    {
        var service = new CableManufacturingEditorService();
        var template = CableManufacturingEditorTests.Template() with { Manufacturing = new()
        {
            FromSourcePortId = "P1", ToSourcePortId = "P2",
            IncompleteRows = [new() { FromSourcePinId = "p1.1", ToFunction = "pending destination" }]
        } };
        var saved = service.Apply(template, service.Prepare(template));
        var reopened = service.Prepare(saved);
        Assert.Contains(reopened.Rows, r => r.FromSourcePinId == "p1.1" && r.ToSourcePinId is null && r.ToFunction == "pending destination");
        Assert.Empty(saved.Mapping);
    }

    [Fact]
    public void NarrowMultiPortTableRequiresAdequateColumnWidth()
    {
        var cable = ArchivedCableInstanceFactory.Create(CableManufacturingEditorTests.Template(10), CableConstructionType.Unknown);
        var page = new SchematicPage { PageId = "page", Title = "Cable" };
        var project = new ElectricalProject { ProjectId = "narrow", Cables = [cable], Schematic = new() { Pages = [page] } };
        var binding = new SchematicCableDetailBinding(cable.CableInstanceId, null) { SketchVisible = false, TableWidth = 60 };
        var error = Assert.Throws<InvalidOperationException>(() => new SchematicCableDetailService().BuildArchived(project, page, binding));
        Assert.Contains("寬", error.Message);
    }

    [Fact]
    public void ThirdPortPinDoesNotOccupyFirstPortColumn()
    {
        var service = new CableManufacturingEditorService();
        var draft = service.Prepare(CableManufacturingEditorTests.Template());
        var port = service.AddEnd(draft);
        Assert.DoesNotContain(draft.Rows, r => r.FromSourcePinId == port.Pins[0].PinId);
        Assert.Contains(port.Pins[0].PinId, JsonSerializer.Serialize(draft.Rows));
    }

    [Fact]
    public void ExtraPortCellsPersistAndCreateOnlyExplicitMappingPairs()
    {
        var template = CableManufacturingEditorTests.Template(3);
        var service = new CableManufacturingEditorService();
        var draft = service.Prepare(template);
        draft.Rows = JsonSerializer.Deserialize<List<CableManufacturingRow>>("""
            [{"FromSourcePinId":"p1.1","ToSourcePinId":"p2.1",
              "AdditionalEnds":{"P3":{"SourcePinId":"p3.2","Function":"Signal"}}},
             {"AdditionalEnds":{"P3":{"SourcePinId":"p3.1","Function":"pending"}}}]
            """)!;
        var saved = service.Apply(template, draft);
        Assert.Equal(2, saved.Mapping.Count);
        Assert.Contains(new CablePinMapping("p1.1", "p3.2"), saved.Mapping);
        Assert.False(saved.MappingConfirmed);
        var loaded = service.Prepare(JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(saved))!);
        Assert.Contains("Signal", JsonSerializer.Serialize(loaded.Rows));
        Assert.Contains("pending", JsonSerializer.Serialize(loaded.Rows));
        Assert.DoesNotContain(saved.Mapping, m => m.FromSourcePinId == "p3.1" || m.ToSourcePinId == "p3.1");
    }

    [Fact]
    public void ExtraPortPinCannotBeAssignedToAnotherPortColumn()
    {
        var service = new CableManufacturingEditorService();
        var draft = service.Prepare(CableManufacturingEditorTests.Template(3));
        draft.Rows = JsonSerializer.Deserialize<List<CableManufacturingRow>>("""
            [{"AdditionalEnds":{"P3":{"SourcePinId":"p2.1"}}}]
            """)!;
        Assert.NotEmpty(service.Validate(draft));
    }

    [Fact]
    public void ManufacturingPresentationIncludesEveryPortHeader()
    {
        var template = CableManufacturingEditorTests.Template(3);
        var cable = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Unknown);
        var project = new ElectricalProject { ProjectId = "multi-port", Cables = [cable],
            Schematic = new() { Pages = [new() { PageId = "page", Title = "Cable" }] } };
        var page = project.Schematic.Pages[0];
        var binding = new SchematicCableDetailBinding(cable.CableInstanceId, null) { SketchVisible = false, TableWidth = 250 };
        var text = new SchematicCableDetailService().BuildArchived(project, page, binding).Primitives
            .Where(p => p.Kind == "TEXT").Select(p => p.Text);
        Assert.Contains("P3 PIN", text);
        Assert.Contains("P3 FUNCTION", text);
    }
}
