using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class SchematicInspectorEditTests
{
    [Fact]
    public void CompositeEditIsIsolatedAndOneUndoRestoresEverything()
    {
        var project = Fixture(); var before = JsonSerializer.Serialize(project);
        var next = SchematicInspectorEdit.ApplyModule(project, "S", new("Valve1", 35, 40, 60, 55, 0, false, null));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        Assert.Equal("Valve1", next.Components[0].ReferenceDesignator);
        Assert.Equal(new SchematicPoint(35, 40), next.Schematic!.Symbols[0].Position);
        Assert.Equal(60, next.Schematic.Symbols[0].Width);
        var history = new ProjectMutationHistory(); history.RecordBeforeMutation(project, "Inspector");
        Assert.True(history.TryUndo(next, out var restored, out _));
        Assert.Equal(before, JsonSerializer.Serialize(restored));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-20)]
    [InlineData(500)]
    public void InvalidPositionRejectsEntireDraft(double x)
    {
        var project = Fixture(); var before = JsonSerializer.Serialize(project);
        Assert.Throws<InvalidOperationException>(() => SchematicInspectorEdit.ApplyModule(project, "S", new("Changed", x, 30, 40, 40, 0, false, null)));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    [Fact]
    public void LockMustBeExplicitlyClearedAndMissingTargetCannotMutate()
    {
        var project = new SchematicAuthoringService().SetLocked(Fixture(), "S", true);
        Assert.Throws<InvalidOperationException>(() => SchematicInspectorEdit.ApplyModule(project, "S", new("Changed", 25, 30, 40, 40, 0, true, null)));
        var next = SchematicInspectorEdit.ApplyModule(project, "S", new("Changed", 25, 30, 40, 40, 0, false, null));
        Assert.False(next.Schematic!.Symbols[0].Locked);
        Assert.Throws<InvalidOperationException>(() => SchematicInspectorEdit.ApplyModule(project, "missing", new("Changed", 25, 30, 40, 40, 0, false, null)));
    }

    [Fact]
    public void ViewingAndNoOpDraftKeepSameInstanceAndManualWireSpecification()
    {
        var project = Fixture();
        Assert.Same(project, SchematicInspectorEdit.ApplyModule(project, "S", new(null, 20, 20, 40, 40, 0, false, null)));
        var service = new SchematicAuthoringService();
        project = service.DrawWire(project, project.Schematic!.Pages[0].PageId, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(90, 30), new(150, 30)]);
        var id = project.Schematic!.Wires[0].WireId; project = service.SetWireArea(project, id, .75);
        var designation = project.Schematic!.Wires[0].Designation;
        Assert.Same(project, SchematicInspectorEdit.ApplyWire(project, id, new(designation, 0, 20, .5)));
        var next = SchematicInspectorEdit.ApplyWire(project, id, new("W-custom", 0, 20, .5));
        Assert.Equal(.75, next.Schematic!.Wires[0].AreaMm2);
        Assert.True(next.Schematic.Wires[0].ManualSpecification);
    }

    private static ElectricalProject Fixture()
    {
        var service = new SchematicAuthoringService();
        var project = service.AddPage(new ElectricalProject { ProjectId = "inspector", Components = [new() {
            ComponentInstanceId = "C", ComponentDefinitionId = "C", TypeKey = "Valve", Ports = [new() { PortId = "P", Name = "P", Pins = [new() { PinId = "pin", PinNumber = "1" }] }] }] }, "One");
        return service.PlaceSymbol(project, new() { SymbolId = "S", ComponentInstanceId = "C", PageId = project.Schematic!.Pages[0].PageId,
            Position = new(20, 20), Width = 40, Height = 40, Anchors = [new() { EndpointId = "pin", Position = new(40, 20) }] });
    }

    [Fact]
    public void ExactPinMetadataEditDoesNotChangeAnotherPinOrConnectivity()
    {
        var p = Fixture(); var component = p.Components[0];
        component.Ports[0].Pins.Add(new() { PinId = "other", PinNumber = "2", Function = "V24", PinName = "V24 / red" });
        var before = JsonSerializer.Serialize(p);
        var next = SchematicInspectorEdit.ApplyPinMetadata(p, "S", "pin", "COM", "black");
        Assert.Equal(before, JsonSerializer.Serialize(p));
        Assert.Equal("COM", next.Components[0].Ports[0].Pins[0].Function);
        Assert.Equal("black", SchematicWireContacts.Color(next.Components[0].Ports[0].Pins[0])?.Name);
        Assert.Equal("V24 / red", next.Components[0].Ports[0].Pins[1].PinName);
        Assert.Empty(next.Connections);
        Assert.Equal("pin", next.Schematic!.Symbols[0].Anchors[0].EndpointId);
    }
}
