using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;
using netDxf;
using netDxf.Entities;

internal static partial class Program
{
    private static int PlacementPreviewProof(string root, string? only)
    {
        Directory.CreateDirectory(root);
        var repository = new SymbolArchiveRepository(Path.Combine(root, "archive"));
        Directory.CreateDirectory(repository.ArchiveRoot);
        var path = Path.Combine(repository.ArchiveRoot, "cable.dxf");
        var dxf = new DxfDocument();
        dxf.Entities.Add(new Line(Vector2.Zero, new(60, 0))); dxf.Entities.Add(new Line(new Vector2(60, 0), new(60, 20)));
        dxf.Entities.Add(new Line(new Vector2(60, 20), new(0, 20))); dxf.Entities.Add(new Line(new Vector2(0, 20), Vector2.Zero));
        dxf.Save(path);
        var cad = new SchematicCadImporter().ReadDxf(path, 1) with { Diagnostics = ["CAD text requires visual review."] };
        var template = new ArchivedCableTemplate { TemplateId = "preview-cable", TemplateRevision = "r1", AssetSha256 = cad.SourceSha256,
            DisplayName = "Independent cable", Ports = new[] { "CA", "CB" }.Select(id => new ComponentIntelligence.Electrical.Domain.ComponentPort {
                PortId = id, Name = id, Pins = Enumerable.Range(1, 2).Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin {
                    PinId = id + n, PinNumber = n.ToString(), Function = n == 1 ? "24V" : "COM" }).ToList() }).ToList(),
            Mapping = [new("CA1", "CB1"), new("CA2", "CB2")] };
        var entry = new CableArchiveEntry { Template = template, AssetPath = "cable.dxf", MillimetresPerUnit = 1, WiringGeometry = cad };
        repository.Save(new() { CableTemplates = [entry] });
        var service = new SchematicAuthoringService();
        var project = new ElectricalProject { ProjectId = "placement", Components = new[] { "sensor", "io" }.Select(id => new ComponentInstance {
            ComponentInstanceId = id, ComponentDefinitionId = id, DisplayName = id, TypeKey = "SYNTHETIC", Ports = [new() { PortId = id + "-port", Name = "IO",
                Pins = Enumerable.Range(1, 2).Select(n => new ComponentIntelligence.Electrical.Domain.ComponentPin { PinId = id + n, PinNumber = n.ToString() }).ToList() }] }).ToList(),
            Schematic = new() { Pages = [new() { PageId = "P", Title = "Placement" }], Symbols = new[] { "sensor", "io" }.Select((id, index) => new SchematicSymbol {
                SymbolId = id, ComponentInstanceId = id, PageId = "P", Position = new(index == 0 ? 10 : 110, 20), Width = 10, Height = 30,
                Geometry = new() { SourceSha256 = new('b', 64), Width = 10, Height = 30, Primitives = [
                    new() { Kind = "LINE", Start = new(0, 0), End = new(10, 30) }] },
                Anchors = Enumerable.Range(1, 2).Select(n => new SchematicAnchor { EndpointId = id + n, Position = new(index == 0 ? 10 : 0, n * 10),
                    Direction = index == 0 ? "Right" : "Left", Confirmed = true }).ToList() }).ToList() } };
        project = service.SetPortFanout(project, "sensor", "sensor-port", true);
        project = service.DrawWire(project, "P", SchematicAttachment.Pin("sensor", "sensor1"), SchematicAttachment.Pin("io", "io1"), [new(30, 30), new(110, 30)]);
        project = service.AddArchivedCable(project, template, CableConstructionType.Custom, cad, new Dictionary<string, string>(), "P", new(220, 130));
        var source = project.Schematic!.Symbols.Last();
        var before = JsonSerializer.Serialize(project);
        var updates = 0;
        var workspace = new SchematicWorkspaceControl(() => project, (next, _) => { project = next; updates++; },
            () => Task.FromResult<IReadOnlyList<ComponentIR>>([]), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var type = typeof(SchematicWorkspaceControl); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Call(string method, params object?[] args)
        {
            try { return type.GetMethod(method, flags)!.Invoke(workspace, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
        }
        object? Field(string name) => type.GetField(name, flags)!.GetValue(workspace);
        void NoMutation() => Require(before == JsonSerializer.Serialize(project) && updates == 0, "Preview/cancel/failed drop changed the saved project or history.");
        void NamedCollision()
        {
            var message = ((TextBlock)Field("Status")!).Text;
            Require(message.Contains("sensor") && message.Contains("Independent cable") && message.Contains("重疊"),
                "Collision did not identify the blocking device and cable in a readable message.");
        }
        type.GetField("_pageId", flags)!.SetValue(workspace, "P"); Call("Render", project);
        if (only is null or "duplicate")
        {
            Call("BeginAnotherCableRepresentation", source);
            Require((bool)Field("_placeMode")! && Field("_placementPreview") is ElectricalProject, "Overlapping default position prevented independent-copy preview.");
            NoMutation();
            type.GetField("_pointer", flags)!.SetValue(workspace, new SchematicPoint(20, 20)); Call("Render", project); NoMutation();
            Call("CancelCommand"); NoMutation();
            Call("BeginAnotherCableRepresentation", source);
            Call("PlaceAnotherCableRepresentation", new SchematicPoint(20, 20));
            Require((bool)Field("_placeMode")! && Field("_pendingCableRepresentation") is not null, "A rejected drop discarded the pending copy."); NoMutation();
            NamedCollision();
            Call("PlaceAnotherCableRepresentation", new SchematicPoint(220, 180));
            Require(project.Cables.Count == 2 && updates == 1 && !(bool)Field("_placeMode")!, "Retrying a valid position did not create exactly one independent cable.");
            Require(project.Cables[0].CableInstanceId != project.Cables[1].CableInstanceId &&
                JsonSerializer.Serialize(project.Schematic!.Wires) == JsonSerializer.Serialize(JsonSerializer.Deserialize<ElectricalProject>(before)!.Schematic!.Wires),
                "Copy placement changed existing external wiring or reused a physical identity.");
            project = JsonSerializer.Deserialize<ElectricalProject>(before)!; updates = 0;
        }
        if (only is null or "library")
        {
            var pendingType = type.GetNestedType("PendingCable", BindingFlags.NonPublic)!;
            var resolved = new ResolvedCableArchive(entry, path);
            var pending = Activator.CreateInstance(pendingType, resolved, cad, CableConstructionType.Custom, new CableArchiveResolver(repository), null)!;
            Require(type.GetMethod("BeginCablePlacement", flags) is not null, "Library placement still constructs a validated ghost before entering placement mode.");
            Call("BeginCablePlacement", pending);
            Require((bool)Field("_placeMode")! && Field("_pendingCable") is not null, "Library preview was blocked by an unrelated existing lead."); NoMutation();
            Call("CancelCommand"); NoMutation();
            Call("BeginCablePlacement", pending);
            void Wait(Task task)
            {
                var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
                task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Wait((Task)Call("PlacePendingCableAsync", new SchematicPoint(20, 20))!); NoMutation();
            Require((bool)Field("_placeMode")! && Field("_pendingCable") is not null, "Rejected library drop lost its preview.");
            NamedCollision();
            Wait((Task)Call("PlacePendingCableAsync", new SchematicPoint(220, 180))!);
            SynchronizationContext.SetSynchronizationContext(null);
            Require(updates == 1 && project.Cables.Count == 2 && !(bool)Field("_placeMode")!, "Valid library drop did not commit exactly once.");
            Require(JsonSerializer.Serialize(project.Schematic!.Wires) == JsonSerializer.Serialize(JsonSerializer.Deserialize<ElectricalProject>(before)!.Schematic!.Wires),
                "Library placement altered existing external wire identities or routes.");
        }
        SchematicAuthoringService.Validate(project);
        Call("Render", project); Render(workspace, 1400, 900, Path.Combine(root, "placement-after-retry.png"));
        File.WriteAllText(Path.Combine(root, "placement-preview-pass.txt"), "PASS preview isolation, collisions retained, cancel, retry, wiring preserved");
        Console.WriteLine("PLACEMENT_PREVIEW_PASS"); return 0;
    }
}
