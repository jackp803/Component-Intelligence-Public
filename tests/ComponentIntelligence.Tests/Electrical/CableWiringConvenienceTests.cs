using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class CableWiringConvenienceTests
{
    private readonly CableManufacturingEditorService _table = new();
    private readonly CableWiringEditorService _wire = new();

    private CableManufacturingDraft Draft()
    {
        var draft = _table.Prepare(CableManufacturingEditorTests.Template(3)); _wire.EnterVisualMode(draft); return draft;
    }

    private static CableManufacturingCell Cell(CableManufacturingDraft draft, string id) => draft.Rows.SelectMany(CableManufacturingCells.All).Single(c => c.SourcePinId == id);

    [Fact]
    public void ExplicitConnectionsFillBlankFunctionsTransitivelyWithoutAddingMappings()
    {
        var draft = Draft();
        _wire.SetPinFunction(draft, "p1.1", "GND");
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p2.1", "p3.1");
        Assert.Equal("GND", Cell(draft, "p2.1").Function); Assert.Equal("GND", Cell(draft, "p3.1").Function);
        Assert.Null(Cell(draft, "p1.2").Function);
        Assert.Equal("p1.1", draft.FunctionLinks["p3.1"].SourcePinId);
        Assert.Equal(2, draft.ExplicitMapping!.Count);
    }

    [Fact]
    public void SourceChangesUpdateDerivedLabelsWhileManualValuesAndBlankOverridesSurvive()
    {
        var draft = Draft(); _wire.SetPinFunction(draft, "p1.1", "GND");
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p1.1", "p3.1");
        _wire.SetPinFunction(draft, "p2.1", "PE");
        _wire.SetPinFunction(draft, "p1.1", "0V");
        Assert.Equal("PE", Cell(draft, "p2.1").Function);
        Assert.Equal("0V", Cell(draft, "p3.1").Function);
        Assert.NotEmpty(CableFunctionAssociationService.Conflicts(draft));
        _wire.SetPinFunction(draft, "p3.1", null); _wire.SetPinFunction(draft, "p1.1", "GND");
        Assert.Null(Cell(draft, "p3.1").Function);
        Assert.Contains("p3.1", draft.FunctionOverrides);
    }

    [Fact]
    public void DisconnectionClearsOnlyOrphanedAutoLabels()
    {
        var draft = Draft(); _wire.SetPinFunction(draft, "p1.1", "GND");
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p2.1", "p3.1");
        _wire.SetPinFunction(draft, "p3.1", "Manual ground");
        _wire.Disconnect(draft, "p1.1", "p2.1");
        Assert.Equal("Manual ground", Cell(draft, "p3.1").Function);
        Assert.Equal("Manual ground", Cell(draft, "p2.1").Function);
        _wire.Disconnect(draft, "p2.1", "p3.1");
        Assert.Null(Cell(draft, "p2.1").Function);
        Assert.Equal("Manual ground", Cell(draft, "p3.1").Function);
    }

    [Fact]
    public void JoiningConflictingManualLabelsNeverOverwritesEitherSideOrFillsBlank()
    {
        var draft = Draft(); _wire.SetPinFunction(draft, "p1.1", "GND"); _wire.SetPinFunction(draft, "p2.1", "+24V");
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p1.1", "p3.1");
        Assert.Equal("GND", Cell(draft, "p1.1").Function); Assert.Equal("+24V", Cell(draft, "p2.1").Function);
        Assert.Null(Cell(draft, "p3.1").Function);
        Assert.NotEmpty(CableFunctionAssociationService.Conflicts(draft));
        Assert.Empty(draft.FunctionLinks);
    }

    [Fact]
    public void EditingAnAutoCellInTheTableCreatesAPersistentManualOverride()
    {
        var draft = Draft(); _wire.SetPinFunction(draft, "p1.1", "GND"); _wire.Connect(draft, "p1.1", "p2.1");
        draft.Rows.Single(r => r.ToSourcePinId == "p2.1").ToFunction = "manual";
        CableFunctionAssociationService.Synchronize(draft);
        _wire.SetPinFunction(draft, "p1.1", "0V");
        Assert.Equal("manual", Cell(draft, "p2.1").Function); Assert.Contains("p2.1", draft.FunctionOverrides);
        var saved = _table.Apply(CableManufacturingEditorTests.Template(3), draft);
        var reopened = _table.Prepare(JsonSerializer.Deserialize<ArchivedCableTemplate>(JsonSerializer.Serialize(saved))!);
        _wire.SetPinFunction(reopened, "p1.1", "GND");
        Assert.Equal("manual", Cell(reopened, "p2.1").Function);
        Assert.Equal("0.9", ElectricalProjectMigrator.Migrate(new ElectricalProject { ProjectId = "functions", Cables = [ArchivedCableInstanceFactory.Create(saved, CableConstructionType.Custom)] }).SchemaVersion);
        var normalized = new SymbolArchiveRepository(Path.GetTempPath()).ValidateAndNormalize(new() { CableTemplates = [new() { Template = saved with { AssetSha256 = new('0', 64) }, Status = SymbolRevisionStatus.Candidate, WiringAssetPending = true, AssetPath = "", MillimetresPerUnit = 1 }] });
        Assert.Equal("ci-symbol-archive.v8", normalized.SchemaVersion);
    }

    [Fact]
    public void MirrorChangesOnlyGeometryAndKeepsPinOrderMappingAndApproval()
    {
        var draft = Draft(); _wire.Connect(draft, "p1.1", "p2.1");
        var mapping = JsonSerializer.Serialize(draft.ExplicitMapping); var ends = JsonSerializer.Serialize(draft.Ends);
        draft.ConfirmMapping = true;
        var before = draft.WiringDiagram!.PinPositions["p1.1"]; var bounds = draft.WiringDiagram.PortBounds["P1"];
        _wire.MirrorPort(draft, "P1");
        Assert.Equal(new SchematicPoint(bounds.X, before.Y), draft.WiringDiagram.PinPositions["p1.1"]);
        Assert.Equal(mapping, JsonSerializer.Serialize(draft.ExplicitMapping)); Assert.Equal(ends, JsonSerializer.Serialize(draft.Ends));
        Assert.True(draft.ConfirmMapping);
        var path = _wire.Build(draft).EdgePaths.Values.Single();
        Assert.True(path[1].X < path[0].X, "Mirrored contact must depart outward, not through its module.");
        _wire.MirrorPort(draft, "P1"); Assert.Equal(before, draft.WiringDiagram.PinPositions["p1.1"]);
    }

    [Fact]
    public void UnconnectedCrossingsUseArcsButTheExplicitBranchStillUsesADot()
    {
        var draft = Draft();
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p1.2", "p2.2"); _wire.Connect(draft, "p1.1", "p3.1");
        var positions = draft.WiringDiagram!.PinPositions;
        var a = positions["p1.1"]; var b = positions["p2.1"]; var c = positions["p3.1"];
        var mid = (a.X + b.X) / 2;
        _wire.SetRoute(draft, "p1.1", "p2.1", [a, b]);
        _wire.SetRoute(draft, "p1.1", "p3.1", [a, new(mid, a.Y), new(mid, c.Y), c]);
        _wire.SetRoute(draft, "p1.2", "p2.2", [positions["p1.2"], positions["p2.2"]]);
        var before = JsonSerializer.Serialize(draft); var diagram = _wire.Build(draft);
        Assert.Contains(diagram.Primitives, p => p.Kind == "ARC" && p.Start == new SchematicPoint(mid, positions["p1.2"].Y));
        Assert.Contains(diagram.Primitives, p => p.Kind == "CIRCLE" && p.Radius < 1 && p.Start == new SchematicPoint(mid, a.Y));
        Assert.DoesNotContain(diagram.Primitives, p => p.Kind == "ARC" && p.Start == new SchematicPoint(mid, a.Y));
        Assert.Equal(before, JsonSerializer.Serialize(draft));
    }

    [Fact]
    public void RemovingTheSourcePortClearsOrphanedLabelsAndProvenance()
    {
        var draft = Draft(); _wire.SetPinFunction(draft, "p1.1", "GND");
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p2.1", "p3.1");
        _table.RemoveEnd(draft, "P1", confirmRemoval: true);
        Assert.Null(Cell(draft, "p2.1").Function); Assert.Null(Cell(draft, "p3.1").Function);
        Assert.Empty(draft.FunctionLinks); Assert.Empty(draft.FunctionOverrides);
        Assert.Empty(_table.Validate(draft));
    }

    [Fact]
    public void LegacyMetadataCannotSmuggleFunctionAssociations()
    {
        var template = CableManufacturingEditorTests.Template(3) with
            { Manufacturing = new() { FunctionOverrides = ["missing"] } };
        Assert.Throws<InvalidOperationException>(() => ArchivedCableInstanceFactory.ValidateTemplate(template));
    }

    [Fact]
    public void ExternalMappingOverrideRecalculatesLabelsWithoutChangingTheArchiveSnapshot()
    {
        var template = CableManufacturingEditorTests.Template(3); var draft = Draft();
        _wire.SetPinFunction(draft, "p1.1", "GND"); _wire.Connect(draft, "p1.1", "p2.1");
        var cable = ArchivedCableInstanceFactory.Create(_table.Apply(template, draft), CableConstructionType.Custom);
        var before = JsonSerializer.Serialize(cable);
        var copy = ArchivedCableInstanceFactory.WithMappingOverride(cable, []);
        var reopened = _table.Prepare(copy);
        Assert.Null(Cell(reopened, "p2.1").Function); Assert.Empty(reopened.FunctionLinks);
        Assert.Equal(before, JsonSerializer.Serialize(cable));
        ArchivedCableInstanceFactory.ValidateProject(new() { ProjectId = "override", Cables = [copy] });
    }

    [Fact]
    public async Task ArchiveAndSqliteReopenPreserveAutoSourcesAndExplicitBlankOverrides()
    {
        var template = CableManufacturingEditorTests.Template(3); var draft = Draft();
        _wire.SetPinFunction(draft, "p1.1", "GND");
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p2.1", "p3.1"); _wire.Connect(draft, "p3.1", "p1.1");
        _wire.SetPinFunction(draft, "p3.1", null);
        var saved = _table.Apply(template, draft) with { AssetSha256 = new('0', 64) };
        var root = Path.Combine(Path.GetTempPath(), "ci-function-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = new SymbolArchiveRepository(root);
            archive.Save(new() { CableTemplates = [new() { Template = saved, Status = SymbolRevisionStatus.Candidate,
                WiringAssetPending = true, AssetPath = "", MillimetresPerUnit = 1 }] });
            var loaded = archive.Load(); Assert.Equal("ci-symbol-archive.v8", loaded.SchemaVersion);
            var project = new ElectricalProject { ProjectId = "functions", Cables = [ArchivedCableInstanceFactory.Create(loaded.CableTemplates[0].Template, CableConstructionType.Custom)] };
            var repo = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "synthetic.db"));
            await repo.SaveAsync(project); var reopened = (await repo.GetAsync(project.ProjectId))!;
            Assert.Equal("0.9", reopened.SchemaVersion);
            var current = _table.Prepare(reopened.Cables[0]); _wire.SetPinFunction(current, "p1.1", "0V");
            Assert.Equal("0V", Cell(current, "p2.1").Function); Assert.Null(Cell(current, "p3.1").Function);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UnrelatedNetBridgesAcrossAnExplicitJunctionRatherThanPassingThroughItsDot()
    {
        var draft = Draft();
        _wire.Connect(draft, "p1.1", "p2.1"); _wire.Connect(draft, "p1.1", "p3.1"); _wire.Connect(draft, "p1.2", "p3.2");
        var positions = draft.WiringDiagram!.PinPositions;
        var a = positions["p1.1"]; var b = positions["p2.1"]; var c = positions["p3.1"]; var x = (a.X + b.X) / 2;
        _wire.SetRoute(draft, "p1.1", "p2.1", [a, b]);
        _wire.SetRoute(draft, "p1.1", "p3.1", [a, new(x, a.Y), new(x, c.Y), c]);
        var start = positions["p1.2"]; var end = positions["p3.2"];
        _wire.SetRoute(draft, "p1.2", "p3.2", [start, new(start.X + 10, start.Y), new(start.X + 10, a.Y - 10),
            new(x, a.Y - 10), new(x, end.Y + 10), new(end.X + 10, end.Y + 10), new(end.X + 10, end.Y), end]);
        var diagram = _wire.Build(draft); var independent = diagram.EdgePrimitives[CableWiringEditorService.EdgeKey("p1.2", "p3.2")];
        Assert.Contains(independent, p => p.Kind == "ARC" && p.Start == new SchematicPoint(x, a.Y));
        Assert.DoesNotContain(independent, p => p.Kind == "LINE" && p.End is { } stop && p.Start.X == x && stop.X == x &&
            Math.Min(p.Start.Y, stop.Y) < a.Y && Math.Max(p.Start.Y, stop.Y) > a.Y);
        Assert.Contains(diagram.Primitives, p => p.Kind == "CIRCLE" && p.Radius < 1 && p.Start == new SchematicPoint(x, a.Y));
    }

    [Fact]
    public void PassiveLayoutAndMirrorSaveDoNotCreateAssociationsOrRevokeApproval()
    {
        var template = CableManufacturingEditorTests.Template(2) with { Mapping = [new("p1.1", "p2.1")],
            Manufacturing = new() { InventoryOnly = true }, MappingConfirmed = true, MappingRevision = "approved", MappingEvidence = "synthetic" };
        template.Ports[0].Pins[0].Function = "GND";
        var draft = _table.Prepare(template); _wire.EnterVisualMode(draft);
        _wire.MirrorPort(draft, "P1"); var view = _wire.Build(draft); var saved = _table.Apply(template, draft);
        Assert.Contains(view.Primitives, p => p.Text == "P2 / 1 / 待確認");
        Assert.Null(saved.Ports[1].Pins[0].Function); Assert.Empty(saved.Manufacturing!.FunctionLinks);
        Assert.True(saved.MappingConfirmed);
    }
}
