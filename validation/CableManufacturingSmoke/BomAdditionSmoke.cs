using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.Repository;

internal static partial class Program
{
    private static int BomAdditionProof(string root)
    {
        Directory.CreateDirectory(root);
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var author = new SchematicAuthoringService();
        var definition = new ComponentIR { Identity = new() { ComponentId = "synthetic-sensor", Manufacturer = "Synthetic", Model = "Sensor" },
            Classification = new() { Category = "Sensor" }, Ports = [new() { PortId = "P1" }],
            Pins = [new() { PortId = "P1", PinNumber = "1", Function = "24V" }, new() { PortId = "P1", PinNumber = "2", Function = "COM" }] };
        var project = author.AddPage(new() { ProjectId = "existing", Name = "Existing project" }, "Sheet");
        project.Components.Add(new ComponentProjectBridge().CreateInstance(definition, "existing-sensor", "S1"));
        project = author.PlaceProjectComponent(project, "existing-sensor", project.Schematic!.Pages[0].PageId, new(40, 60));
        project = author.DrawWire(project, project.Schematic!.Pages[0].PageId, SchematicAttachment.Free(), SchematicAttachment.Free(), [new(90, 80), new(140, 80)]);
        var originalScene = JsonSerializer.Serialize(project.Schematic); var original = JsonSerializer.Serialize(project);
        var history = new ProjectMutationHistory(); var updates = 0;
        Func<Task<IReadOnlyList<ComponentIR>>> provider = () => Task.FromResult<IReadOnlyList<ComponentIR>>([definition]);
        var workspace = new SchematicWorkspaceControl(() => project, (next, label) => { history.RecordBeforeMutation(project, label); project = next; updates++; },
            () => provider(), _ => Task.FromResult<Uri?>(null), () => { }, () => { });
        var host = new Window { Content = workspace, Width = 1280, Height = 840, Opacity = 0, ShowInTaskbar = false };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        object? Get(object target, string name) => target.GetType().GetField(name, flags)!.GetValue(target);
        object? Call(object target, string name, params object?[] args)
        {
            try { return target.GetType().GetMethod(name, flags)!.Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
        }
        void Wait(Task task)
        {
            if (!task.IsCompleted) { var frame = new DispatcherFrame();
                task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.PushFrame(frame); }
            task.GetAwaiter().GetResult();
        }
        void Dialog(Action<ProjectComponentAddDialog> action)
        {
            Exception? failure = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                var dialog = Application.Current.Windows.OfType<ProjectComponentAddDialog>().Single();
                try { action(dialog); } catch (Exception error) { failure = error; dialog.DialogResult = false; }
            }));
            Wait((Task)Call(workspace, "AddProjectComponentAsync")!);
            if (failure is not null) throw failure;
        }
        void CaptureDialog(ProjectComponentAddDialog dialog, double width, double height, string name)
        {
            dialog.Width = width; dialog.Height = height; dialog.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            var content = (FrameworkElement)dialog.Content;
            foreach (var button in Descendants(content).OfType<Button>().Where(b => b.Content is "加入專案" or "取消"))
            {
                var top = button.TranslatePoint(new Point(), content);
                Require(top.Y >= 0 && top.Y + button.ActualHeight <= content.ActualHeight + 1 &&
                    top.X >= 0 && top.X + button.ActualWidth <= content.ActualWidth + 1, "Dialog action is clipped at a supported window size.");
            }
            Render(content, content.ActualWidth + content.Margin.Left + content.Margin.Right,
                content.ActualHeight + content.Margin.Top + content.Margin.Bottom, Path.Combine(root, name));
        }
        host.Show();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            Require(((Button)Get(workspace, "AddBomComponentButton")!).IsVisible, "Project BOM has no add button.");
            Dialog(dialog => {
                ((TextBox)Get(dialog, "_search")!).Text = "Synthetic";
                var list = (ListBox)Get(dialog, "_list")!; Require(list.Items.Count == 1, "Library search did not find the component."); list.SelectedIndex = 0;
                ((TextBox)Get(dialog, "_quantity")!).Text = "1.5"; Call(dialog, "Accept");
                Require(dialog.IsVisible && ((TextBlock)Get(dialog, "_error")!).Text.Length > 0, "Non-integer quantity was accepted.");
                dialog.DialogResult = false;
            });
            Require(updates == 0 && original == JsonSerializer.Serialize(project), "Cancel/invalid input changed the project.");
            Dialog(dialog => {
                ((ListBox)Get(dialog, "_list")!).SelectedIndex = 0; ((TextBox)Get(dialog, "_quantity")!).Text = "2";
                CaptureDialog(dialog, 760, 620, "add-library.png"); Call(dialog, "Accept");
            });
            Require(updates == 1 && project.Components.Count == 3 && originalScene == JsonSerializer.Serialize(project.Schematic), "Library add changed old scene or failed to add independent instances.");
            Dialog(dialog => {
                ((TabControl)Get(dialog, "_tabs")!).SelectedIndex = 1; ((TextBox)Get(dialog, "_model")!).Text = "Field sensor";
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                    var editor = Application.Current!.Windows.OfType<ComponentManualEditorDialog>().Single();
                    try {
                        var ports = (IList)Get(editor, "_ports")!; var port = Activator.CreateInstance(ports.GetType().GetGenericArguments()[0])!;
                        port.GetType().GetProperty("PortId")!.SetValue(port, "X1"); ports.Add(port);
                        var pins = (IList)Get(editor, "_pins")!;
                        foreach (var n in new[] { "1", "2" }) { var pin = Activator.CreateInstance(pins.GetType().GetGenericArguments()[0])!;
                            pin.GetType().GetProperty("PortId")!.SetValue(pin, "X1"); pin.GetType().GetProperty("PinNumber")!.SetValue(pin, n);
                            pin.GetType().GetProperty("Function")!.SetValue(pin, n == "1" ? "24V" : "COM"); pins.Add(pin); }
                        Call(editor, "Save_Click", null, new RoutedEventArgs());
                    } catch { editor.DialogResult = false; throw; }
                }));
                Call(dialog, "EditContacts");
                Require(((TextBlock)Get(dialog, "_contacts")!).Text == "Port 1 / Pin 2", "Custom editor lost explicit contacts.");
                CaptureDialog(dialog, 560, 430, "add-custom-compact.png"); Call(dialog, "Accept");
            });
            Require(updates == 2 && project.Components.Count == 4 && originalScene == JsonSerializer.Serialize(project.Schematic), "Custom addition changed the old scene.");
            var custom = project.Components.Last(); Require(custom.Ports[0].Pins.Count == 2 && custom.Ports[0].Pins[0].Function == "24V", "Custom ports/pins missing.");
            Require(history.TryUndo(project, out var undone, out _) && undone.Components.Count == 3, "Addition is not a single undo operation.");
            Require(history.TryRedo(undone, out var redone, out _) && JsonSerializer.Serialize(project) == JsonSerializer.Serialize(redone), "Redo changed added identities.");
            var repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), Path.Combine(root, "test-project.db"));
            Wait(repository.SaveAsync(project)); var reload = repository.GetAsync(project.ProjectId); Wait(reload);
            project = reload.Result!; workspace.RefreshWorkspace();
            var customRow = SchematicBomPalette.Build(project, []).Single(b => b.DefinitionId == custom.ComponentDefinitionId);
            Require(customRow.RemainingCount == 1, "Custom addition disappeared without a central catalog.");
            var pageId = project.Schematic!.Pages[0].PageId;
            project = author.PlaceProjectComponent(project, customRow.InstanceIds[0], pageId, new(180, 100)); workspace.RefreshWorkspace();
            Render(workspace, 1280, 840, Path.Combine(root, "bom-after-add.png"));

            provider = () => Task.FromException<IReadOnlyList<ComponentIR>>(new IOException("Synthetic library unavailable"));
            Dialog(dialog => { ((TabControl)Get(dialog, "_tabs")!).SelectedIndex = 1;
                ((TextBox)Get(dialog, "_model")!).Text = "Offline custom"; Call(dialog, "Accept"); });
            Require(updates == 3 && project.Components.Count == 5, "Unavailable library prevented a project-local addition.");

            var pending = new TaskCompletionSource<IReadOnlyList<ComponentIR>>(); provider = () => pending.Task;
            var loading = (Task)Call(workspace, "AddProjectComponentAsync")!;
            project = author.AddPage(new() { ProjectId = "switched" }, "Other project"); workspace.RefreshWorkspace();
            pending.SetResult([definition]); Wait(loading);
            Require(updates == 3 && project.Components.Count == 0 && !Application.Current!.Windows.OfType<ProjectComponentAddDialog>().Any(), "Stale catalog loading modified the newly active project.");
            File.WriteAllText(Path.Combine(root, "bom-addition-pass.txt"), "PASS existing library, local custom pins, validation/cancel, independent IDs, scene preservation, undo/redo, SQLite reload, placement and async project switching.");
            Console.WriteLine("PROJECT_BOM_ADDITION_PASS"); return 0;
        }
        finally { host.Close(); SynchronizationContext.SetSynchronizationContext(null); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
    }
}
