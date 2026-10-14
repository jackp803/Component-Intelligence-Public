using System.Text.Json;
using ComponentIntelligence.Electrical.Connectivity;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicWireContactSummaryTests
{
    [Theory]
    [InlineData("COM / black", "Common for the non-isolated 24 V supply and communication reference", "COM")]
    [InlineData("V24 / RD", "Positive 24 V supply for the auxiliary device power input", "V24")]
    [InlineData("D+ / white", "Positive differential data conductor for the communication interface", "D+")]
    [InlineData("V24 / amber", "Positive 24 V supply for the auxiliary device power input", "V24 / amber")]
    public void InferredSummaryUsesExactSourcePinNameAndOnlyRemovesKnownColor(string name, string function, string expected)
    {
        var project = Project(name, function);
        var sink = Pin(project, "sink");
        Assert.Equal("source", sink.InferredFunction!.SourcePinId);
        var before = JsonSerializer.Serialize(project);
        var contact = Resolve(project);
        Assert.Equal(expected, contact.ShortFunction);
        Assert.Equal(function, contact.Function);
        Assert.Equal(function, sink.DisplayFunction);
        Assert.Null(sink.Function);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void MultiplePropagatingSourcesWithSameCaptionProduceOneShortFunction()
    {
        var project = Project("COM / black", LongCommon);
        AddSource(project, "other-source", "COM / BK", LongCommon);
        ProjectPinFunctionService.Synchronize(project);
        var before = JsonSerializer.Serialize(project);
        Assert.Equal("COM", Resolve(project).ShortFunction);
        Assert.Equal(LongCommon, Pin(project, "sink").DisplayFunction);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void ArchivedCableRuntimeSourceIsResolvedInsteadOfItsArchiveLineageId()
    {
        var project = Project("D+ / WH", LongData);
        var sourcePort = project.Components[0].Ports[0];
        project.Components.RemoveAt(0);
        project.Cables.Add(new() { CableInstanceId = "physical", CableDefinitionId = "template", ArchivedCable = new() {
            Template = new() { TemplateId = "template", TemplateRevision = "fixture", AssetSha256 = new('a', 64) }, Ports = [sourcePort] } });
        Assert.Equal("D+", Resolve(project).ShortFunction);
        Assert.Equal(LongData, Pin(project, "sink").DisplayFunction);
    }

    [Theory]
    [InlineData("Manual COM")]
    [InlineData("")]
    [InlineData("   ")]
    public void DestinationManualOverrideIncludingBlankNeverBorrowsSourceName(string manual)
    {
        var project = Project("COM / black", LongCommon);
        var sink = Pin(project, "sink");
        sink.FunctionManualOverride = true;
        sink.Function = manual;
        // Retain an old link deliberately: the authoritative override must still win.
        var before = JsonSerializer.Serialize(project);
        Assert.Equal(manual.Trim(), Resolve(project).ShortFunction);
        Assert.Equal(manual, sink.DisplayFunction);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("sink")]
    public void SourceManualOverrideDoesNotReuseItsStalePinName(string overridden)
    {
        var project = Project("COM / black", LongCommon);
        var pin = Pin(project, overridden);
        pin.FunctionManualOverride = true;
        pin.Function = "Manual V24";
        ProjectPinFunctionService.Synchronize(project);
        Assert.Equal("Manual V24", Resolve(project).ShortFunction);
        Assert.Equal("Manual V24", Pin(project, "sink").DisplayFunction);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("archive-source")]
    public void UnresolvedRuntimeSourceNeverFallsBackToPinNumberOrArchiveSourceId(string sourceId)
    {
        var project = Project("COM / black", LongCommon);
        var sink = Pin(project, "sink");
        sink.InferredFunction = new(sourceId, LongCommon);
        Assert.Equal(LongCommon, Resolve(project).ShortFunction);
        Assert.Equal(LongCommon, sink.DisplayFunction);
        Assert.Equal("17", sink.PinNumber);
    }

    [Fact]
    public void ConflictingFunctionsDoNotCreateASourceCaptionOrPinNumberFunction()
    {
        var project = Project("COM / black", LongCommon);
        AddSource(project, "other-source", "V24 / red", "Different power supply function");
        var issues = ProjectPinFunctionService.Synchronize(project);
        Assert.Contains(issues, i => i.Code == "FUNCTION_CONFLICT");
        Assert.Null(Pin(project, "sink").InferredFunction);
        Assert.Equal("", Resolve(project).ShortFunction);
        Assert.Null(Pin(project, "sink").DisplayFunction);
    }

    [Theory]
    [InlineData("changed-function")]
    [InlineData("blank")]
    [InlineData("unused")]
    [InlineData("duplicate-runtime-id")]
    public void StaleOrAmbiguousSourceIdentityDoesNotAuthorizeAnAbbreviation(string invalid)
    {
        var project = Project("COM / black", LongCommon);
        var source = Pin(project, "source");
        switch (invalid)
        {
            case "changed-function": source.Function = "Changed authoritative function"; break;
            case "blank": source.FunctionManualOverride = true; source.Function = ""; break;
            case "unused": source.Status = PinStatus.Unused; break;
            default: AddSource(project, "source", "V24 / red", LongCommon); break;
        }
        Assert.Equal(LongCommon, Resolve(project).ShortFunction);
        Assert.Equal(LongCommon, Pin(project, "sink").DisplayFunction);
    }

    [Theory]
    [InlineData("0-10 V / 4-20 mA")]
    [InlineData("0-10 V or 4-20 mA")]
    [InlineData("0-10 V | 4-20 mA")]
    [InlineData("0-10 V; 4-20 mA")]
    [InlineData("0-10 V \u6216 4-20 mA")]
    [InlineData("Analog input with selectable voltage or current operating modes")]
    public void SelectableAnalogModesRemainFullOrExplicitlyUnselected(string function)
    {
        var project = Project("AI / blue", function);
        Pin(project, "source").Analog = new();
        var before = JsonSerializer.Serialize(project);
        Assert.Equal(function.Length <= 32 ? function : function + "（模式待選）", Resolve(project).ShortFunction);
        Assert.Equal(function, Pin(project, "sink").DisplayFunction);
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("An explicit caption longer than thirty-two characters")]
    [InlineData("COM\nV24")]
    [InlineData("COM\rV24")]
    public void MissingNumericLongOrMultilineSourceNameDoesNotBecomeAShortFunction(string name)
    {
        var project = Project(name, LongCommon);
        Assert.Equal(LongCommon, Resolve(project).ShortFunction);
        Assert.Equal(LongCommon, Pin(project, "sink").DisplayFunction);
    }

    [Fact]
    public void ShortCaptionLimitIsInclusive()
    {
        var project = Project(new('C', 32), LongCommon);
        Assert.Equal(new string('C', 32), Resolve(project).ShortFunction);
    }

    [Fact]
    public void ExplicitDestinationFunctionWinsOverStaleInferredProvenance()
    {
        var project = Project("COM / black", LongCommon);
        var sink = Pin(project, "sink");
        sink.Function = "Local return";
        sink.PinName = "Local COM / BK";
        Assert.Equal("Local COM", Resolve(project).ShortFunction);
        Assert.Equal("Local return", sink.DisplayFunction);
    }

    private const string LongCommon = "Common for the non-isolated 24 V supply and communication reference";
    private const string LongData = "Positive differential data conductor for the communication interface";

    private static SchematicWireContact Resolve(ElectricalProject project) =>
        SchematicWireContacts.Resolve(project, SchematicAttachment.Pin("sink-symbol", "sink"))!;

    private static ComponentPin Pin(ElectricalProject project, string id) => ProjectPinFunctionService.Pins(project).Single(p => p.PinId == id);

    private static ElectricalProject Project(string sourceName, string sourceFunction)
    {
        var project = new ElectricalProject { ProjectId = "synthetic-contact-summary", Components = [new() {
            ComponentInstanceId = "sink-component", ComponentDefinitionId = "sink-def", TypeKey = "fixture",
            Ports = [new() { PortId = "sink-port", Name = "Connector", Pins = [new() { PinId = "sink", PinNumber = "17", SourcePinId = "archive-sink" }] }] }] };
        AddSource(project, "source", sourceName, sourceFunction);
        // Runtime source order is independent of component placement and archive lineage.
        (project.Components[0], project.Components[1]) = (project.Components[1], project.Components[0]);
        ProjectPinFunctionService.Synchronize(project);
        return project;
    }

    private static void AddSource(ElectricalProject project, string id, string name, string function)
    {
        project.Components.Add(new() { ComponentInstanceId = "component-" + project.Components.Count, ComponentDefinitionId = "fixture-source", TypeKey = "fixture",
            Ports = [new() { PortId = "port-" + project.Components.Count, Name = "Source", Pins = [new() {
                PinId = id, SourcePinId = "archive-source", PinNumber = "1", PinName = name, Function = function }] }] });
        project.Connections.Add(new() { ConnectionId = "connection-" + project.Connections.Count, FromEndpointId = id, ToEndpointId = "sink" });
    }
}
