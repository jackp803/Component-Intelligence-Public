using System.Text.Json;
using System.Text.Json.Serialization;
using ComponentIntelligence.Bom;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "demo";
        var dbPath = GetOption(args, "--db") ?? Path.Combine("artifacts", "component-intelligence-mvp.db");
        var cachePath = GetOption(args, "--cache");
        try
        {
            return command switch
            {
                "demo" => await RunDemoAsync(dbPath),
                "run" => await RunWorkbookAsync(args, dbPath, cachePath),
                "template" => GenerateTemplate(args),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => Unknown(command)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(FormatException(exception));
            return 1;
        }
    }

    private static async Task<int> RunDemoAsync(string dbPath)
    {
        var pipeline = ComponentRuntimeFactory.CreateOfflineDemo(dbPath);
        var row = new BomRow { RowId = "1", RawManufacturer = "IFM", RawModelOrPartNumber = "O5D100", Manufacturer = "IFM", ModelOrPartNumber = "O5D100", UsedQuantity = 4, TotalQuantity = 5, SpareQuantity = 1, Notes = "主機光電感測器", ImportStatus = BomImportStatus.Imported };
        var first = await pipeline.ProcessAsync(row);
        var second = await pipeline.ProcessAsync(row);

        // A cached record that is not yet topology-ready is intentionally reused and then enriched again.
        // The diagnostic says EXISTING_KNOWLEDGE because the normal online pipeline may now source that
        // existing knowledge from Notion first or from Local SQLite as its fallback.
        var secondReusedExistingKnowledge = second.LocalRepositoryHit ||
                                            second.Issues.Contains("EXISTING_KNOWLEDGE_ENRICHMENT_ATTEMPTED", StringComparer.OrdinalIgnoreCase);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode = "offline-deterministic-demo",
            database = Path.GetFullPath(dbPath),
            first_run = first,
            second_run = second,
            second_run_local_repository_hit = second.LocalRepositoryHit,
            second_run_existing_knowledge_reused = secondReusedExistingKnowledge
        }, JsonOptions));

        return first.Component is not null && second.Component is not null && secondReusedExistingKnowledge ? 0 : 2;
    }

    private static async Task<int> RunWorkbookAsync(string[] args, string dbPath, string? cachePath)
    {
        var workbook = args.Skip(1).FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(workbook)) { Console.Error.WriteLine("Usage: run <bom.xlsx> [--db <path>] [--cache <path>] [--offline]"); return 64; }
        var import = await new BomImporter().ImportAsync(workbook);
        var pipeline = args.Contains("--offline", StringComparer.OrdinalIgnoreCase) ? ComponentRuntimeFactory.CreateOfflineDemo(dbPath) : ComponentRuntimeFactory.CreateOnline(dbPath, cachePath);
        var results = new List<object>();
        foreach (var row in import.Rows) results.Add(new { row.RowId, row.ImportStatus, row.ValidationFlags, Result = await pipeline.ProcessAsync(row) });
        Console.WriteLine(JsonSerializer.Serialize(new { import.Errors, Results = results }, JsonOptions));
        return results.Count > 0 ? 0 : 3;
    }

    private static int GenerateTemplate(string[] args)
    {
        var path = args.Skip(1).FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "BOM.xlsx";
        new BomTemplateGenerator().Generate(path);
        Console.WriteLine(Path.GetFullPath(path));
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("Component Intelligence v0.1\n  demo [--db path]                     deterministic offline acceptance demo\n  template [path]\n  run <bom.xlsx> [--db path] [--cache path] [--offline]\n\nNormal 'run' uses Notion central knowledge when configured, then Local SQLite, then live deterministic manufacturer sources. --offline enables the O5D100 seed fixture only.");
        return 0;
    }

    private static int Unknown(string command) { Console.Error.WriteLine($"Unknown command: {command}"); PrintHelp(); return 64; }
    private static string FormatException(Exception exception) { var list = new List<string>(); for (Exception? current = exception; current is not null; current = current.InnerException) list.Add($"{current.GetType().FullName}: {current.Message}"); return string.Join(Environment.NewLine + "→ ", list); }
}
