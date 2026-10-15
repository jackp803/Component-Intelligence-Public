using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableInstanceDuplicationTests
{
    [Fact]
    public void ConfirmedInternalWiringStaysConfirmedButExternalAssignmentsAreNotCopied()
    {
        var template = CableManufacturingEditorTests.Template() with { Mapping = [new("p1.1", "p2.1")],
            MappingConfirmed = true, MappingRevision = "m1", MappingEvidence = "synthetic verified mapping" };
        var original = ArchivedCableInstanceFactory.Create(template, CableConstructionType.Custom);
        original.CoreAssignments.Add(new() { CoreId = "core-1", FromEndpointId = "external-source", ToEndpointId = "external-destination" });
        original.ArchivedCable!.Ports[0].Pins[0].InferredFunction = new("external-source", "sensor function");
        var before = JsonSerializer.Serialize(original);
        var copy = ArchivedCableInstanceFactory.Duplicate(original);
        Assert.True(copy.ArchivedCable!.MappingConfirmed);
        Assert.Equal("m1", copy.ArchivedCable.Template.MappingRevision);
        Assert.Equal("p2.1", Assert.Single(copy.ArchivedCable.Mapping).ToSourcePinId);
        Assert.Empty(copy.CoreAssignments);
        Assert.All(copy.ArchivedCable.Ports.SelectMany(p => p.Pins), p => Assert.Null(p.InferredFunction));
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void DuplicateHasFreshEndpointsButKeepsInstanceWiringAndDisplayLayout()
    {
        var project = Project(); var author = new SchematicAuthoringService();
        var source = project.Schematic!.Symbols[0]; var original = project.Cables[0];
        var before = JsonSerializer.Serialize(project);
        var next = author.DuplicateArchivedCable(project, source.SymbolId, "sheet", new(180, 20));
        var copy = next.Cables[1]; var view = next.Schematic!.Symbols.Last();
        Assert.Equal(2, next.Cables.Count);
        Assert.Equal(2, new ComponentIntelligence.Electrical.Bom.DerivedBomEngine().Build(next).Count);
        Assert.NotEqual(original.CableInstanceId, copy.CableInstanceId);
        var ids = original.ArchivedCable!.Ports.SelectMany(p => p.Pins.Select(pin => pin.PinId).Prepend(p.PortId));
        Assert.Empty(ids.Intersect(copy.ArchivedCable!.Ports.SelectMany(p => p.Pins.Select(pin => pin.PinId).Prepend(p.PortId))));
        Assert.Equal(JsonSerializer.Serialize(original.ArchivedCable.Template), JsonSerializer.Serialize(copy.ArchivedCable.Template));
        Assert.Equal(original.ArchivedCable.Mapping, copy.ArchivedCable.Mapping);
        Assert.Equal(original.ArchivedCable.HasMappingOverride, copy.ArchivedCable.HasMappingOverride);
        Assert.False(copy.ArchivedCable.MappingConfirmed);
        Assert.Equal(JsonSerializer.Serialize(original.ArchivedCable.Manufacturing), JsonSerializer.Serialize(copy.ArchivedCable.Manufacturing));
        Assert.Equal(JsonSerializer.Serialize(original.ArchivedCable.ManufacturingGeometry), JsonSerializer.Serialize(copy.ArchivedCable.ManufacturingGeometry));
        Assert.Equal(source.Anchors.Select(a => a.SourcePinId), view.Anchors.Select(a => a.SourcePinId));
        Assert.All(view.Anchors, a => Assert.Contains(copy.ArchivedCable.Ports.SelectMany(p => p.Pins), pin => pin.PinId == a.EndpointId));
        Assert.All(view.EndpointDisplayPlacements, p => Assert.Contains(copy.ArchivedCable.Ports, port => port.PortId == p.PortId && port.Pins.Any(pin => pin.PinId == p.EndpointId)));
        Assert.All(view.CadPortBindings, b => Assert.Contains(copy.ArchivedCable.Ports, p => p.PortId == b.PortId && p.SourcePortId == b.SourcePortId));
        Assert.All(view.CollapsedPortIds.Concat(view.PortPlacements.Select(p => p.PortId)), id => Assert.Contains(copy.ArchivedCable.Ports, p => p.PortId == id));
        Assert.Equal(source.EndpointDisplayPlacements.Select(p => p.Position), view.EndpointDisplayPlacements.Select(p => p.Position));
        Assert.Equal(new(180, 20), view.Position);
        Assert.False(view.Locked);
        Assert.Null(copy.ReferenceDesignator); Assert.Null(copy.ProvidedLengthMm); Assert.Null(copy.Notes);
        Assert.Equal(CableLengthSource.Unknown, copy.LengthSource);
        Assert.Empty(copy.CoreAssignments);
        Assert.All(copy.ArchivedCable.Ports.SelectMany(p => p.Pins), pin => Assert.Null(pin.InferredFunction));
        Assert.Equal(JsonSerializer.Serialize(project.Connections), JsonSerializer.Serialize(next.Connections));
        Assert.Equal(JsonSerializer.Serialize(project.Schematic.Wires), JsonSerializer.Serialize(next.Schematic.Wires));
        Assert.Equal(before, JsonSerializer.Serialize(project));
        copy.ArchivedCable.Ports[0].Pins[0].Function = "copy only";
        Assert.NotEqual("copy only", original.ArchivedCable.Ports[0].Pins[0].Function);
    }

    [Fact]
    public async Task SeparateLengthsAndNotesPersistWithoutAnySharedCableViews()
    {
        var author = new SchematicAuthoringService(); var project = Project();
        project = author.DuplicateArchivedCable(project, project.Schematic!.Symbols[0].SymbolId, "sheet", new(180, 20));
        var first = project.Cables[0]; var second = project.Cables[1];
        project = author.SetArchivedCableDetails(project, first.CableInstanceId, "C1", 500, "6 cores",
            CableConstructionType.Custom, first.ArchivedCable!.Mapping, "Upper sensor");
        project = author.SetArchivedCableDetails(project, second.CableInstanceId, "C2", 1200, "6 cores",
            CableConstructionType.Custom, second.ArchivedCable!.Mapping, "Lower sensor\nService loop");
        Assert.Equal(2, project.Cables.Count); Assert.Equal(2, project.Schematic!.Symbols.Count);
        Assert.Equal(new[] { 500d, 1200d }, project.Cables.Select(c => c.ProvidedLengthMm!.Value));
        Assert.Single(project.Schematic.Symbols.Where(s => SchematicSymbolOwner.Resolve(project, s).Reference == "C1"));
        var details = new SchematicCableDetailService();
        foreach (var cable in project.Cables.ToArray())
            project = details.AddArchivedDetail(project, cable.CableInstanceId, layout: new(cable.CableInstanceId, null) { SketchVisible = false });
        var path = Path.Combine(Path.GetTempPath(), "cable-instance-notes-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repo = new ElectricalProjectRepository(new SqliteConnectionFactory(), path);
            await repo.SaveAsync(project); var loaded = (await repo.GetAsync(project.ProjectId))!;
            Assert.Equal(new[] { "Upper sensor", "Lower sensor\nService loop" }, loaded.Cables.Select(c => c.Notes));
            foreach (var page in loaded.Schematic!.Pages.Where(p => p.CableDetail is not null))
            {
                var text = string.Join("\n", details.Build(loaded, page).Primitives.Select(p => p.Text));
                var isFirst = page.CableDetail!.CableInstanceId == first.CableInstanceId;
                Assert.Contains(isFirst ? "500 mm" : "1200 mm", text);
                Assert.Contains(isFirst ? "Upper sensor" : "Lower sensor", text);
                Assert.DoesNotContain(isFirst ? "Lower sensor" : "Upper sensor", text);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }

    [Fact]
    public void CopyCanBeUndoneAndInvalidLengthOrNotesCannotPartiallyApply()
    {
        var project = Project(); var author = new SchematicAuthoringService(); var source = project.Schematic!.Symbols[0];
        var history = new ProjectMutationHistory(); history.RecordBeforeMutation(project, "Duplicate cable");
        var next = author.DuplicateArchivedCable(project, source.SymbolId, "sheet", new(180, 20));
        Assert.True(history.TryUndo(next, out var undone, out _)); Assert.Single(undone.Cables);
        Assert.True(history.TryRedo(undone, out var redone, out _));
        Assert.Equal(JsonSerializer.Serialize(next), JsonSerializer.Serialize(redone));
        var before = JsonSerializer.Serialize(project); var cable = project.Cables[0];
        foreach (var length in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            Assert.Throws<InvalidOperationException>(() => author.SetArchivedCableDetails(project, cable.CableInstanceId,
                "C2", length, "spec", CableConstructionType.Custom, cable.ArchivedCable!.Mapping, "New note"));
        Assert.Throws<InvalidOperationException>(() => author.SetArchivedCableDetails(project, cable.CableInstanceId,
            "C2", 1000, "spec", CableConstructionType.Custom, cable.ArchivedCable!.Mapping, "bad\0note"));
        Assert.Throws<InvalidOperationException>(() => author.DuplicateArchivedCable(project, source.SymbolId, "missing", new(20, 20)));
        Assert.Equal(before, JsonSerializer.Serialize(project));
    }

    internal static ElectricalProject Project()
    {
        var project = ArchivedCableManufacturingDetailTests.Project(); var author = new SchematicAuthoringService();
        var symbol = project.Schematic!.Symbols[0];
        project.Schematic.Symbols[0] = symbol with { Geometry = symbol.Geometry! with {
            ConnectionPoints = [new("A", "", new(0, 10), Direction: "Left"), new("B", "", new(60, 10), Direction: "Right")] } };
        project = author.SetCableContactBindings(project, symbol.SymbolId, new Dictionary<string, string> { ["P1"] = "A", ["P2"] = "B" });
        var cable = project.Cables[0];
        project = author.SetArchivedCableDetails(project, cable.CableInstanceId, "C1", 500, "6 cores", CableConstructionType.Custom,
            [new("p1.1", "p2.2")], "Original cable note");
        project = author.SetPortFanout(project, symbol.SymbolId, project.Cables[0].ArchivedCable!.Ports[0].PortId, true);
        project.Components.Add(new() { ComponentInstanceId = "sensor", ComponentDefinitionId = "sensor", TypeKey = "sensor",
            Ports = [new() { PortId = "sensor-port", Name = "P", Pins = [new() { PinId = "sensor-pin", PinNumber = "1" }] }] });
        new ComponentIntelligence.Electrical.Topology.TopologyEndpointConnectionService().ConnectEndpoints(project,
            project.Cables[0].ArchivedCable!.Ports[1].Pins[0].PinId, "sensor-pin");
        var view = project.Schematic!.Symbols[0]; var pin = view.Anchors[0].EndpointId;
        var start = SchematicAuthoringService.AnchorPoint(view, pin);
        project = author.DrawWire(project, "sheet", SchematicAttachment.Pin(view.SymbolId, pin), SchematicAttachment.Free(),
            [start, new(start.X - 5, start.Y)]);
        return project;
    }
}
