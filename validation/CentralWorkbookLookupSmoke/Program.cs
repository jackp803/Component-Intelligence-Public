using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Desktop;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Runtime;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(Path.GetFullPath(args.Single())); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh smoke directory.");
        Directory.CreateDirectory(root); SQLitePCL.Batteries_V2.Init();
        var database = Path.Combine(root, "data", "project.db");
        var seed = Path.Combine(root, "empty.xlsx"); var selected = Path.Combine(root, "selected.xlsx");
        Workbook(seed, false); Workbook(selected, true);
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComponentIntelligence", "central-workbook.txt");
        string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
        var originalShared = Hash(shared); var originalWorkbook = Hash(selected);
        var priorDb = Environment.GetEnvironmentVariable(DesktopDatabasePathResolver.EnvironmentVariableName);
        var priorWorkbook = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_WORKBOOK");
        try
        {
            Environment.SetEnvironmentVariable(DesktopDatabasePathResolver.EnvironmentVariableName, database);
            Environment.SetEnvironmentVariable("COMPONENT_INTELLIGENCE_WORKBOOK", seed);
            var window = new MainWindow();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var load = typeof(MainWindow).GetMethod("LoadCentralWorkbookPath", flags)!;
            var save = typeof(MainWindow).GetMethod("SaveCentralWorkbookPath", flags)!;
            Require(save is not null && !save.IsStatic, "Selection must use the isolated instance's settings.");
            Require((string?)load.Invoke(window, null) == seed, "Initial isolated seed did not apply.");
            save!.Invoke(window, [selected]);
            var effective = (string?)load.Invoke(window, null);
            Require(effective == selected, "Explicit selection lost to the launch override.");
            typeof(MainWindow).GetMethod("UpdateCentralLibraryPathUi", flags)!.Invoke(window, null);
            var button = (System.Windows.Controls.Button)window.FindName("CentralLibraryButton");
            Require(button.ToolTip.ToString()!.Contains(selected), "Path tooltip differs from lookup path.");
            var result = ComponentRuntimeFactory.CreateCentralWorkbookLookupService(database, effective!)
                .LookupAsync(new BomRow
                {
                    RowId = "test",
                    Manufacturer = "IFM",
                    ModelOrPartNumber = "AL1342",
                    RawManufacturer = "IFM",
                    RawModelOrPartNumber = "AL1342"
                }).GetAwaiter().GetResult();
            Require(result.ResolutionStatus == ResolutionStatus.Resolved, "BOM lookup did not use the selected workbook.");
            Require((string?)load.Invoke(new MainWindow(), null) == selected, "Reopened window lost selection.");
            var otherDb = Path.Combine(root, "other", "project.db");
            Environment.SetEnvironmentVariable(DesktopDatabasePathResolver.EnvironmentVariableName, otherDb);
            Require((string?)load.Invoke(new MainWindow(), null) == seed, "Other isolated data inherited selection.");
            Require(Hash(shared) == originalShared, "Shared preferences changed.");
            Require(Hash(selected) == originalWorkbook, "Central workbook was modified.");
            File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
            {
                InitialSeed = seed,
                SelectedWorkbook = effective,
                Result = result.ResolutionStatus.ToString(),
                ReopenRetainsSelection = true,
                IsolatedContextsIndependent = true,
                CentralWorkbookUnchanged = true,
                SharedPreferencesUnchanged = true,
                WindowsShown = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: actual MainWindow selection and tooltip; BOM found; reopen retains choice; isolated settings; central workbook and shared preferences unchanged; no windows shown.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(DesktopDatabasePathResolver.EnvironmentVariableName, priorDb);
            Environment.SetEnvironmentVariable("COMPONENT_INTELLIGENCE_WORKBOOK", priorWorkbook);
        }
    }

    private static void Workbook(string path, bool populated)
    {
        using var workbook = new XLWorkbook();
        var components = workbook.Worksheets.Add("Components");
        foreach (var (column, header) in new[] { (1, "ComponentID"), (2, "Manufacturer"), (3, "Model"), (4, "Category") })
            components.Cell(1, column).Value = header;
        workbook.Worksheets.Add("Ports").Cell(1, 1).Value = "PortID";
        workbook.Worksheets.Add("Pins").Cell(1, 1).Value = "PinID";
        if (populated)
        {
            components.Cell(2, 1).Value = "TEST-ONLY-AL1342";
            components.Cell(2, 2).Value = "IFM"; components.Cell(2, 3).Value = "AL1342";
            components.Cell(2, 4).Value = "TEST";
        }
        workbook.SaveAs(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
