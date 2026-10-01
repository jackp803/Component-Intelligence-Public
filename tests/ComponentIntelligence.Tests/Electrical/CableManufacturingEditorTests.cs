using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableManufacturingEditorTests
{
    private readonly CableManufacturingEditorService _service = new();

    [Fact]
    public void PinCountCreatesPinsWithoutMapping()
    {
        var template = Template(); var before = JsonSerializer.Serialize(template);
        var draft = _service.Prepare(template);
        _service.SetPinCount(draft, "P1", 3);
        var next = _service.Apply(template, draft);
        Assert.Equal(3, next.Ports[0].Pins.Count);
        Assert.Empty(next.Mapping); Assert.False(next.MappingConfirmed);
        Assert.Equal(before, JsonSerializer.Serialize(template));
        Assert.Equal("p1.1", next.Ports[0].Pins[0].PinId);
    }

    [Fact]
    public void BlankTargetIsNotNc()
    {
        var next = _service.Apply(Template(), _service.Prepare(Template()));
        Assert.Empty(next.Mapping);
        Assert.Empty(next.Manufacturing!.PinUsage);
        Assert.False(next.MappingConfirmed);
        Assert.Contains(next.Manufacturing.IncompleteRows, r => r.FromSourcePinId == "p1.1" && r.ToSourcePinId is null);
    }

    [Fact]
    public void ExplicitNcAndUnusedRemainDistinct()
    {
        var draft = _service.Prepare(Template());
        draft.Rows.Single(r => r.FromSourcePinId == "p1.1").FromUsage = CablePinUsage.Nc;
        draft.Rows.Single(r => r.FromSourcePinId == "p1.2").FromUsage = CablePinUsage.Unused;
        var next = _service.Apply(Template(), draft);
        Assert.Contains(next.Manufacturing!.PinUsage, u => u.SourcePinId == "p1.1" && u.Usage == CablePinUsage.Nc);
        Assert.Contains(next.Manufacturing.PinUsage, u => u.SourcePinId == "p1.2" && u.Usage == CablePinUsage.Unused);
        Assert.Empty(next.Mapping);
        Assert.Equal(CablePinUsage.Nc, _service.Prepare(next).Rows.Single(r => r.FromSourcePinId == "p1.1").FromUsage);
    }

    [Fact]
    public void MappedPinRemovalRejected()
    {
        var template = Template() with { Mapping = [new("p1.1", "p2.1")] };
        var draft = _service.Prepare(template);
        draft.Ends[0].Pins.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => _service.Apply(template, draft));
        Assert.Equal(2, template.Ports[0].Pins.Count);
        Assert.Single(template.Mapping);
    }

    [Fact]
    public void UnknownConnectorRemainsDraft()
    {
        var draft = _service.Prepare(Template());
        draft.Ends[0].Connector = new() { ConnectorId = "custom-end", Family = "Custom", PinCount = 2 };
        var next = _service.Apply(Template(), draft);
        Assert.False(next.MappingConfirmed);
        Assert.Equal("Custom", next.Ports[0].Connector!.Family);
        Assert.Empty(next.Mapping);
    }

    [Fact]
    public void MappingEditRevokesOnlyEditedConfirmation()
    {
        var original = Template() with { Mapping = [new("p1.1", "p2.1")], MappingConfirmed = true,
            MappingRevision = "m1", MappingEvidence = "fixture confirmation" };
        var before = JsonSerializer.Serialize(original);
        var draft = _service.Prepare(original);
        draft.Rows[0].ToSourcePinId = "p2.2";
        var next = _service.Apply(original, draft);
        Assert.False(next.MappingConfirmed);
        Assert.Equal(new CablePinMapping("p1.1", "p2.2"), Assert.Single(next.Mapping));
        Assert.Equal(before, JsonSerializer.Serialize(original));
        Assert.True(original.MappingConfirmed);
    }

    [Fact]
    public void OpeningConfirmedTemplateWithoutEditsDoesNotRevokeConfirmation()
    {
        var template = Template() with { Mapping = [new("p1.1", "p2.1")], MappingConfirmed = true,
            MappingRevision = "m1", MappingEvidence = "fixture confirmation" };
        Assert.True(_service.Apply(template, _service.Prepare(template)).MappingConfirmed);
    }

    [Fact]
    public void YAppearanceDoesNotShortBranches()
    {
        var template = Template(3); var draft = _service.Prepare(template);
        var next = _service.Apply(template, draft);
        Assert.Equal(3, next.Ports.Count);
        Assert.Empty(next.Mapping); Assert.False(next.MappingConfirmed);
    }

    [Fact]
    public void PartialRowsAndUnboundFunctionTextRoundTripWithoutContinuity()
    {
        var draft = _service.Prepare(Template());
        draft.Rows.Add(new() { FromFunction = "pending source", ToFunction = "pending destination" });
        var next = JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(_service.Apply(Template(), draft)))!;
        var reloaded = _service.Prepare(next);
        Assert.Contains(reloaded.Rows, r => r.FromFunction == "pending source" && r.FromSourcePinId is null);
        Assert.Empty(next.Mapping);
    }

    [Fact]
    public void CompleteTableNeedsExplicitEvidenceBeforeConfirmation()
    {
        var template = Template(); var draft = _service.Prepare(template);
        draft.Rows.Clear();
        draft.Rows.Add(new() { FromSourcePinId = "p1.1", ToSourcePinId = "p2.1", FromFunction = "+24V", ToFunction = "+24V" });
        draft.Rows.Add(new() { FromSourcePinId = "p1.2", ToSourcePinId = "p2.2", FromFunction = "GND", ToFunction = "GND" });
        draft.ConfirmMapping = true;
        Assert.Throws<InvalidOperationException>(() => _service.Apply(template, draft));
        draft.MappingRevision = "m1"; draft.MappingEvidence = "explicit fixture source";
        var next = _service.Apply(template, draft);
        Assert.True(next.MappingConfirmed); Assert.Equal(2, next.Mapping.Count);
        Assert.Equal("+24V", next.Ports[0].Pins[0].Function);
        Assert.Equal("GND", next.Ports[1].Pins[1].Function);
    }

    internal static ArchivedCableTemplate Template(int ends = 2) => new()
    {
        TemplateId = "cable", TemplateRevision = "r1", AssetSha256 = new('a', 64),
        Ports = Enumerable.Range(1, ends).Select(e => new ComponentPort { PortId = $"P{e}", Name = $"P{e}",
            Pins = Enumerable.Range(1, 2).Select(p => new ComponentPin { PinId = $"p{e}.{p}", PinNumber = p.ToString() }).ToList() }).ToList()
    };
}
