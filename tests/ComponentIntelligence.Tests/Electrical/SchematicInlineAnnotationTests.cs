using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicInlineAnnotationTests
{
    [Fact]
    public void CaptionIncludesDesignationCompactFunctionAndPhysicalColor()
    {
        var project = SchematicWireIdentificationTests.Fixture(.2);
        foreach (var pin in project.Components.SelectMany(c => c.Ports).SelectMany(p => p.Pins))
        { pin.Function = "Common for 24 V supply and non-isolated communication reference"; pin.PinName = "COM / black"; }
        project = SchematicWireIdentificationTests.Draw(project);
        var wire = project.Schematic!.Wires.Single();
        var before = JsonSerializer.Serialize(project);
        var entry = SchematicWireLegendPresentation.Entry(project, wire);
        Assert.Equal(wire.Designation + " · COM · BK", entry.Caption);
        Assert.Contains("Common for 24 V", entry.FullText);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData("Sensor (optional)", "Optional external sensor input for Pt1000, Ni1000, NTC10k, active 0...10 V sensor or switching contact.", "Sensor")]
    [InlineData("0...10 V / MP-Bus", "Selectable analogue output 0...10 V / 0.5...10 V / 2...10 V, or MP-Bus conductor.", "MP-Bus")]
    public void InferredMultimodeFunctionIsCompactButNeverSelectsAMode(string name, string function, string expected)
    {
        var project = SchematicWireIdentificationTests.Fixture(.2);
        var source = project.Components[0].Ports[0].Pins[0];
        var sink = project.Components[1].Ports[0].Pins[0];
        source.PinName = name + " / white"; source.Function = function;
        sink.PinName = ""; sink.Function = null;
        project = SchematicWireIdentificationTests.Draw(project);
        var entry = SchematicWireLegendPresentation.Entries(project).Single();
        Assert.DoesNotContain("功能見明細", entry.Caption);
        Assert.Contains(expected, entry.ShortFunction);
        Assert.Contains("待選", entry.ShortFunction);
        Assert.Contains(function, entry.Functions);
        Assert.Null(project.Components[1].Ports[0].Pins[0].Function);
    }

    [Fact]
    public void UnknownLongFunctionIsRetainedInsteadOfAPlaceholderOrInventedMeaning()
    {
        var project = SchematicWireIdentificationTests.Fixture(.2);
        project.Components[0].Ports[0].Pins[0].Function = new string('x', 160);
        project = SchematicWireIdentificationTests.Draw(project);
        var entry = SchematicWireLegendPresentation.Entries(project).Single();
        Assert.DoesNotContain("功能見明細", entry.Caption);
        Assert.Contains(new string('x', 160), entry.ShortFunction);
    }

    [Fact]
    public void ExplicitlyBlankManualEndpointRemainsVisibleAsPendingInTheWireCaption()
    {
        var project = SchematicWireIdentificationTests.Fixture(.2);
        project.Components[0].Ports[0].Pins[0].Function = "COM";
        project.Components[0].Ports[0].Pins[0].PinName = "COM / black";
        project.Components[1].Ports[0].Pins[0].Function = "";
        project.Components[1].Ports[0].Pins[0].FunctionManualOverride = true;
        project = SchematicWireIdentificationTests.Draw(project);
        var entry = SchematicWireLegendPresentation.Entries(project).Single();
        Assert.Contains("COM", entry.ShortFunction);
        Assert.Contains("待設定", entry.ShortFunction);
        Assert.Equal("", project.Components[1].Ports[0].Pins[0].Function);
    }

    [Theory]
    [InlineData("Negative differential data or positive differential data, selectable by switch", true, "positive", "switch")]
    [InlineData("Selectable input: 0-10 V or dry contact or Pt1000 sensor", false, "dry contact", "Pt1000")]
    [InlineData("BACnet MS/TP differential data negative / Modbus RTU differential data positive", false, "negative", "positive")]
    public void CompactionNeverDropsManualDetailsOrUnrecognizedAlternatives(string function, bool manual, string first, string second)
    {
        var p = SchematicWireIdentificationTests.Fixture(.2);
        p.Components[0].Ports[0].Pins[0].Function = function;
        p.Components[0].Ports[0].Pins[0].PinName = "AI / blue";
        p.Components[0].Ports[0].Pins[0].FunctionManualOverride = manual;
        p = SchematicWireIdentificationTests.Draw(p);
        var contact = SchematicWireContacts.Resolve(p, p.Schematic!.Wires[0].Start)!;
        Assert.Contains(first, contact.ShortFunction);
        Assert.Contains(second, contact.ShortFunction);
        if (manual) Assert.Equal(function, contact.ShortFunction);
    }

    [Fact]
    public void LabelExposesEveryClearSpanOfACorneredRouteWithoutChangingIt()
    {
        var project = SchematicWireIdentificationTests.Draw(SchematicWireIdentificationTests.Fixture(.2));
        var wire = project.Schematic!.Wires[0] with { Points = [new(60, 50), new(180, 50), new(180, 80), new(270, 80)] };
        var label = SchematicCableLabelPresentation.Resolve(project, wire)!;
        var property = label.GetType().GetProperty("Spans");
        Assert.NotNull(property);
        var spans = ((System.Collections.IEnumerable)property!.GetValue(label)!).Cast<object>().ToArray();
        Assert.Equal(3, spans.Length);
        Assert.Equal(new SchematicPoint(225, 80), spans[1].GetType().GetProperty("Center")!.GetValue(spans[1]));
        Assert.Equal(new SchematicPoint(180, 65), spans[2].GetType().GetProperty("Center")!.GetValue(spans[2]));
    }

    [Fact]
    public void CollinearExitStubsAreOneContinuousLabelSpan()
    {
        var project = SchematicWireIdentificationTests.Draw(SchematicWireIdentificationTests.Fixture(.2));
        var wire = project.Schematic!.Wires[0] with { Points = [new(20, 50), new(25, 50), new(100, 50), new(105, 50)] };
        var label = SchematicCableLabelPresentation.Resolve(project, wire)!;
        Assert.Equal(81, label.AvailableLength);
        Assert.Single(label.Spans);
        Assert.Equal(new SchematicPoint(62.5, 50), label.Center);
    }
}
