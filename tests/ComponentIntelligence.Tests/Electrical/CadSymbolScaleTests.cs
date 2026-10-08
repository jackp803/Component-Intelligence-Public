using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;
using netDxf;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CadSymbolScaleTests
{
    [Fact]
    public void DisplaySizeScalesGeometryAndContactsWithoutEditingNativeEvidence()
    {
        var p = Fixture(); var s = p.Schematic!.Symbols[0]; var before = JsonSerializer.Serialize(s.Geometry);
        s = s with { Width = 40, Height = 20 }; p.Schematic.Symbols[0] = s;
        var view = SchematicSymbolPresentation.GeometryForOwner(s, SchematicSymbolOwner.Resolve(p, s))!;
        Assert.Equal(40, view.Width); Assert.Equal(new SchematicPoint(40, 10), view.Primitives[0].End);
        Assert.Equal(new SchematicPoint(60, 30), SchematicPortPresentation.ConnectionPoint(p, s, p.Cables[0].ArchivedCable!.Ports[1].PortId));
        Assert.Equal(before, JsonSerializer.Serialize(s.Geometry));
    }

    [Fact]
    public void ScalingReanchorsWireAndKeepsNativeBindingsAndCableData()
    {
        var p = Fixture(); var s = p.Schematic!.Symbols[0]; var port = p.Cables[0].ArchivedCable!.Ports[1].PortId;
        p.Schematic.Wires.Add(new() { WireId = "wire", PageId = "page", Start = SchematicAttachment.Port(s.SymbolId, port), Points = [new(100, 40), new(150, 40)] });
        var before = JsonSerializer.Serialize(p); var cable = JsonSerializer.Serialize(p.Cables);
        var resized = Scale(p, s.SymbolId, .5);
        var next = resized.Schematic!.Symbols[0];
        Assert.Equal(new SchematicPoint(60, 30), resized.Schematic.Wires[0].Points[0]);
        Assert.Equal(JsonSerializer.Serialize(s.Anchors), JsonSerializer.Serialize(next.Anchors));
        Assert.Equal(JsonSerializer.Serialize(s.CadPortBindings), JsonSerializer.Serialize(next.CadPortBindings));
        Assert.Equal(JsonSerializer.Serialize(s.Geometry), JsonSerializer.Serialize(next.Geometry));
        Assert.Equal(cable, JsonSerializer.Serialize(resized.Cables)); Assert.Equal(before, JsonSerializer.Serialize(p));
        SchematicAuthoringService.Validate(resized);
    }

    [Theory]
    [InlineData(0, 60, 30)]
    [InlineData(90, 30, 60)]
    [InlineData(180, 20, 30)]
    [InlineData(270, 30, 20)]
    public void RotatedPortAndPinBindingsScaleExactlyOnce(int rotation, double x, double y)
    {
        var service = new SchematicAuthoringService(); var p = Fixture(); var id = p.Schematic!.Symbols[0].SymbolId;
        p.Schematic.Symbols[0] = p.Schematic.Symbols[0] with { Rotation = rotation };
        var port = p.Cables[0].ArchivedCable!.Ports[1].PortId;
        var scaled = Scale(p, id, .5); var s = scaled.Schematic!.Symbols[0];
        Assert.Equal(new(x, y), SchematicPortPresentation.ConnectionPoint(scaled, s, port));
        p = service.SetCableContactBindings(p, id, new Dictionary<string, string> { ["p1.1"] = "CAD-P1", ["p2.1"] = "CAD-P2" });
        var anchor = p.Schematic!.Symbols[0].Anchors.Single(a => a.SourcePinId == "p2.1");
        var start = SchematicAuthoringService.AnchorPoint(p.Schematic.Symbols[0], anchor.EndpointId);
        p.Schematic.Wires.Add(new() { WireId = "pin-wire", PageId = "page", Start = SchematicAttachment.Pin(id, anchor.EndpointId),
            Points = [start, new(start.X + 100, start.Y)] });
        scaled = Scale(p, id, .5); s = scaled.Schematic!.Symbols[0];
        Assert.Equal(new(x, y), SchematicAuthoringService.AnchorPoint(s, anchor.EndpointId));
        Assert.Equal(new(x, y), scaled.Schematic.Wires[0].Points[0]);
        var view = SchematicSymbolPresentation.GeometryForOwner(s, SchematicSymbolOwner.Resolve(scaled, s))!;
        var text = SchematicSymbolPresentation.UprightCadText(s, view).Primitives.Single();
        Assert.Equal(1.5, text.TextHeight); Assert.Equal(0, text.Rotation);
        var dxf = DxfDocument.Load(new MemoryStream(new SchematicDxfExporter().Create(scaled)[0].Dxf));
        var line = dxf.Entities.Lines.Single(l => l.Layer.Name == "SCHEMATIC_SYMBOL");
        Assert.Equal(40, (line.EndPoint - line.StartPoint).Modulus(), 8);
        Assert.Equal(1.5, dxf.Entities.Texts.Single(t => t.Value == "P1").Height, 8);
    }

    [Fact]
    public void CircleArcFillAndMultilineTextPreserveShapeAtHalfScale()
    {
        var p = Fixture(); var s = p.Schematic!.Symbols[0];
        p.Schematic.Symbols[0] = s with { Geometry = s.Geometry! with { Primitives = [
            new() { Kind = "CIRCLE", Start = new(20, 20), Radius = 5 },
            new() { Kind = "ARC", Start = new(30, 20), Radius = 10, StartAngle = 20, EndAngle = 120 },
            new() { Kind = "SOLID_HATCH", Start = new(0, 0), Contours = [new SchematicPoint[] { new(0, 0), new(5, 0), new(5, 5) }] },
            new() { Kind = "MTEXT", Start = new(10, 10), Text = "A\nB", TextHeight = 3, TextWidth = 20, TextWidthFactor = .8 }] } };
        var resized = Scale(p, s.SymbolId, .5); s = resized.Schematic!.Symbols[0];
        var view = SchematicSymbolPresentation.Geometry(s, null)!;
        Assert.Equal(2.5, view.Primitives[0].Radius); Assert.Equal(5, view.Primitives[1].Radius);
        Assert.Equal(20, view.Primitives[1].StartAngle); Assert.Equal(120, view.Primitives[1].EndAngle);
        Assert.Equal(new(2.5, 2.5), view.Primitives[2].Contours[0][2]);
        Assert.Equal(10, view.Primitives[3].TextWidth); Assert.Equal(.8, view.Primitives[3].TextWidthFactor);
    }

    [Fact]
    public void ScaleIsAbsoluteAndRotatedResizeUsesDisplayedBounds()
    {
        var p = Fixture(); var id = p.Schematic!.Symbols[0].SymbolId;
        p.Schematic.Symbols[0] = p.Schematic.Symbols[0] with { Rotation = 90 };
        p = Scale(p, id, .5); p = Scale(p, id, .75);
        Assert.Equal(60, p.Schematic!.Symbols[0].Width);
        p = new SchematicAuthoringService().ResizeCadSymbol(p, id, 20, 40);
        Assert.Equal(40, p.Schematic!.Symbols[0].Width); Assert.Equal(20, p.Schematic.Symbols[0].Height);
        var reset = Scale(p, id, 1); Assert.Equal(80, reset.Schematic!.Symbols[0].Width);
        Assert.Equal(p.Schematic.Symbols[0].AssetRevision, reset.Schematic.Symbols[0].AssetRevision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(.04)]
    [InlineData(21)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidScaleCannotModifyInput(double value)
    {
        var p = Fixture(); var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => Scale(p, p.Schematic!.Symbols[0].SymbolId, value));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockedSymbolOrAttachedWirePreventsScaling(bool lockSymbol)
    {
        var p = Fixture(); var s = p.Schematic!.Symbols[0];
        if (lockSymbol) p.Schematic.Symbols[0] = s with { Locked = true };
        else p.Schematic.Wires.Add(new() { WireId = "locked", PageId = "page", Locked = true,
            Start = SchematicAttachment.Port(s.SymbolId, p.Cables[0].ArchivedCable!.Ports[1].PortId), Points = [new(100, 40), new(150, 40)] });
        var before = JsonSerializer.Serialize(p);
        Assert.Throws<InvalidOperationException>(() => Scale(p, s.SymbolId, .5));
        Assert.Equal(before, JsonSerializer.Serialize(p));
    }

    [Fact]
    public void RebindingAndCopyingScaledRepresentationRetainsNativeCoordinatesAndPerSymbolScale()
    {
        var service = new SchematicAuthoringService(); var p = Fixture(); var id = p.Schematic!.Symbols[0].SymbolId;
        p = service.PlaceExistingRepresentation(p, id, "page", new(200, 20));
        p = Scale(p, id, .5);
        Assert.Equal(80, p.Schematic!.Symbols[1].Width);
        var revision = p.Schematic.Symbols[0].AssetRevision;
        Assert.NotNull(revision);
        p = service.SetCableContactBindings(p, id, new Dictionary<string, string> { ["P1"] = "CAD-P2", ["P2"] = "CAD-P1" });
        var s = p.Schematic!.Symbols[0];
        Assert.Equal(40, s.Width); Assert.Equal(80, s.Geometry!.Width); Assert.Null(s.AssetRevision);
        Assert.Equal(new(80, 20), s.CadPortBindings.Single(b => b.SourcePortId == "P1").Position);
        Assert.Equal(new(60, 30), SchematicPortPresentation.ConnectionPoint(p, s, p.Cables[0].ArchivedCable!.Ports[0].PortId));
        p = service.PlaceExistingRepresentation(p, id, "page", new(250, 20));
        Assert.Equal(40, p.Schematic!.Symbols[2].Width);
    }

    [Fact]
    public void BranchFollowsResizedParentWithoutAddingConnectivityOrChangingFreeEnd()
    {
        var service = new SchematicAuthoringService(); var p = Fixture(); var s = p.Schematic!.Symbols[0];
        var port = p.Cables[0].ArchivedCable!.Ports[1].PortId;
        p = service.DrawWire(p, "page", SchematicAttachment.Port(s.SymbolId, port), SchematicAttachment.Free(), [new(100, 40), new(150, 40)]);
        var parentId = p.Schematic!.Wires[0].WireId;
        p = service.DrawWire(p, "page", SchematicAttachment.Junction(parentId), SchematicAttachment.Free(), [new(120, 40), new(120, 70)]);
        var connections = JsonSerializer.Serialize(p.Connections); var nets = JsonSerializer.Serialize(p.Nets);
        p = Scale(p, s.SymbolId, .5);
        var parent = p.Schematic!.Wires[0]; var branch = p.Schematic.Wires[1];
        Assert.Equal(parentId, branch.Start.WireId); Assert.Equal(new(120, 70), branch.Points[^1]);
        Assert.NotEqual(new(120, 40), branch.Points[0]);
        Assert.Contains(parent.Points.Zip(parent.Points.Skip(1)), segment =>
            segment.First.X == segment.Second.X && segment.First.X == branch.Points[0].X &&
                branch.Points[0].Y >= Math.Min(segment.First.Y, segment.Second.Y) && branch.Points[0].Y <= Math.Max(segment.First.Y, segment.Second.Y) ||
            segment.First.Y == segment.Second.Y && segment.First.Y == branch.Points[0].Y &&
                branch.Points[0].X >= Math.Min(segment.First.X, segment.Second.X) && branch.Points[0].X <= Math.Max(segment.First.X, segment.Second.X));
        Assert.Equal(connections, JsonSerializer.Serialize(p.Connections)); Assert.Equal(nets, JsonSerializer.Serialize(p.Nets));
        SchematicAuthoringService.Validate(p);
    }

    [Fact]
    public void ScaleSchemaIsProtectedBeforeSaveAndAfterResetWithoutReopen()
    {
        var p = Fixture(); var id = p.Schematic!.Symbols[0].SymbolId;
        p = Scale(p, id, .5);
        Assert.Equal("0.10", p.SchemaVersion);
        p = Scale(p, id, 1);
        Assert.Equal("0.10", p.SchemaVersion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(.5)]
    public void LockedRouteAtUnmovedContactIsNeverRewritten(double scale)
    {
        var p = Fixture(); var s = p.Schematic!.Symbols[0];
        p.Schematic.Symbols[0] = s with { Geometry = s.Geometry! with { ConnectionPoints = s.Geometry.ConnectionPoints
            .Select(c => c.Tag == "CAD-P1" ? c with { Position = new(0, 0) } : c).ToArray() } };
        p = new SchematicAuthoringService().SetCableContactBindings(p, s.SymbolId,
            new Dictionary<string, string> { ["p1.1"] = "CAD-P1", ["p2.1"] = "CAD-P2" });
        var pin = p.Schematic!.Symbols[0].Anchors.Single(a => a.SourcePinId == "p1.1").EndpointId;
        p.Schematic.Wires.Add(new() { WireId = "locked-route", PageId = "page", Locked = true,
            Start = SchematicAttachment.Pin(s.SymbolId, pin), Points = [new(20, 20), new(0, 20), new(0, 40), new(-20, 40)] });
        var before = JsonSerializer.Serialize(p.Schematic.Wires[0].Points);
        p = Scale(p, s.SymbolId, scale);
        Assert.Equal(before, JsonSerializer.Serialize(p.Schematic!.Wires[0].Points));
        Assert.True(p.Schematic.Wires[0].Locked);
    }

    [Fact]
    public void PortDragAtHalfScaleStoresNativeAnchorAndRemainsOnEdgeAfterFurtherScaling()
    {
        var p = Fixture(); var id = p.Schematic!.Symbols[0].SymbolId; var service = new SchematicAuthoringService();
        p = service.SetCableContactBindings(p, id, new Dictionary<string, string> { ["p1.1"] = "CAD-P1", ["p2.1"] = "CAD-P2" });
        var port = p.Cables[0].ArchivedCable!.Ports[0].PortId;
        p = Scale(p, id, .5); p = service.MovePortToEdge(p, id, port, "Right", 10);
        var s = p.Schematic!.Symbols[0]; var a = s.Anchors.Single(a => a.SourcePinId == "p1.1");
        Assert.Equal(new(80, 20), a.Position); Assert.Equal(new(60, 30), SchematicAuthoringService.AnchorPoint(s, a.EndpointId));
        Assert.False(a.Confirmed); Assert.Null(a.CadContactId);
        p = Scale(p, id, .75); s = p.Schematic!.Symbols[0];
        Assert.Equal(new(80, 35), SchematicAuthoringService.AnchorPoint(s, a.EndpointId));
    }

    [Fact]
    public void PortWithoutPinsRetainsNativePlacementAcrossScaleAndDrag()
    {
        var p = Fixture(); var geometry = p.Schematic!.Symbols[0].Geometry!;
        p.Cables.Clear(); p.Schematic.Symbols.Clear();
        p.Components.Add(new() { ComponentInstanceId = "C", ComponentDefinitionId = "native", TypeKey = "generic",
            Ports = [new() { PortId = "P", Name = "P" }] });
        p.Schematic.Symbols.Add(new() { SymbolId = "s", ComponentInstanceId = "C", PageId = "page", Position = new(20, 20),
            Width = 80, Height = 40, Geometry = geometry, PortPlacements = [new() { PortId = "P", Side = "Right", Coordinate = 10 }] });
        p = Scale(p, "s", .5);
        Assert.Equal(new(60, 25), SchematicPortPresentation.ConnectionPoint(p, p.Schematic!.Symbols[0], "P"));
        p = new SchematicAuthoringService().MovePortToEdge(p, "s", "P", "Right", 8);
        Assert.Equal(16, p.Schematic!.Symbols[0].PortPlacements[0].Coordinate);
        Assert.Equal(new(60, 28), SchematicPortPresentation.ConnectionPoint(p, p.Schematic.Symbols[0], "P"));
        p = Scale(p, "s", .75);
        Assert.Equal(new(80, 32), SchematicPortPresentation.ConnectionPoint(p, p.Schematic!.Symbols[0], "P"));
    }

    [Fact]
    public async Task SQLiteRetainsScaleAndNativeEvidenceAndProtectsFromOlderReaders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ci-cad-scale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var p = Fixture(); Assert.Equal("0.7", ElectricalProjectMigrator.Migrate(p).SchemaVersion);
            p = Scale(p, p.Schematic!.Symbols[0].SymbolId, .5); var before = JsonSerializer.Serialize(p);
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "synthetic.db"));
            await repository.SaveAsync(p); var saved = (await repository.GetAsync(p.ProjectId))!;
            var revisions = new ProjectRevisionService(new ProjectRevisionRepository(new SqliteConnectionFactory(), Path.Combine(root, "synthetic.db")));
            var checkpoint = await revisions.CreateCheckpointAsync(p, ProjectRevisionTrigger.Save);
            using var snapshot = JsonDocument.Parse(checkpoint.SnapshotJson);
            Assert.Equal("0.10", snapshot.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("0.10", saved.SchemaVersion); Assert.Equal(40, saved.Schematic!.Symbols[0].Width);
            Assert.Equal(JsonSerializer.Serialize(p.Schematic), JsonSerializer.Serialize(saved.Schematic));
            Assert.Equal(JsonSerializer.Serialize(p.Cables), JsonSerializer.Serialize(saved.Cables));
            Assert.Equal(before, JsonSerializer.Serialize(p));
            saved = Scale(saved, saved.Schematic.Symbols[0].SymbolId, 1);
            Assert.Equal("0.10", ElectricalProjectMigrator.Migrate(saved).SchemaVersion);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ElectricalProject Scale(ElectricalProject p, string id, double scale) => new SchematicAuthoringService().ScaleCadSymbol(p, id, scale);

    internal static ElectricalProject Fixture()
    {
        var template = CableManufacturingEditorTests.Template();
        var geometry = new SchematicCadAsset { SourceSha256 = template.AssetSha256, Width = 80, Height = 40, MillimetresPerUnit = 1,
            Primitives = [new() { Kind = "LINE", Start = new(0, 20), End = new(80, 20) }, new() { Kind = "TEXT", Start = new(5, 5), Text = "P1", TextHeight = 3 }],
            ConnectionPoints = [new("CAD-P1", "", new(0, 20), Direction: "Left"), new("CAD-P2", "", new(80, 20), Direction: "Right")] };
        var p = new ElectricalProject { ProjectId = "cad-scale", Schematic = new() { Pages = [new() { PageId = "page", Title = "Synthetic" }] } };
        return new SchematicAuthoringService().AddArchivedCable(p, template, CableConstructionType.Unknown, geometry,
            new Dictionary<string, string> { ["P1"] = "CAD-P1", ["P2"] = "CAD-P2" }, "page", new(20, 20), assetApproved: true);
    }
}
