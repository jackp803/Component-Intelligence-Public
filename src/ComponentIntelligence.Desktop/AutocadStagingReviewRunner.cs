using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ComponentIntelligence.Desktop;

public sealed class AutocadStagingReviewRunner
{
    public const string AutomationRootEnvironmentVariable = "COMPONENT_INTELLIGENCE_ACADE_AUTOMATION_ROOT";
    public const string SymbolAcceptanceRegistryEnvironmentVariable = "COMPONENT_INTELLIGENCE_ACADE_SYMBOL_ACCEPTANCE_REGISTRY";
    public const string EngineeringStagingScriptFileName = "Invoke-CMLrduEngineeringStaging.ps1";
    public const string SymbolAcceptanceRegistryParameter = "-SymbolAcceptanceRegistryPath";
    public const string SymbolAcceptanceRegistryFileName = "cm-lrdu-symbol-acceptance-registry.v1.json";
    public static string DefaultStagingRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentIntelligence",
        "autocad-staging");
    public static string DefaultSymbolAcceptanceRegistryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentIntelligence",
        SymbolAcceptanceRegistryFileName);

    private readonly IAutocadStagingProcessExecutor _processExecutor;
    private readonly string? _automationRoot;

    public AutocadStagingReviewRunner()
        : this(new SystemAutocadStagingProcessExecutor())
    {
    }

    public AutocadStagingReviewRunner(
        IAutocadStagingProcessExecutor processExecutor,
        string? automationRoot = null)
    {
        _processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
        _automationRoot = string.IsNullOrWhiteSpace(automationRoot) ? null : Path.GetFullPath(automationRoot);
    }

    public static string CreateRunRoot()
    {
        var root = Path.Combine(
            DefaultStagingRoot,
            $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    public async Task<AutocadStagingReviewRunResult> RunAsync(
        AutocadStagingReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var graphPath = RequireExistingGraph(request.GraphPath);
        var resolvedOutputRoot = Path.GetFullPath(request.OutputRoot);
        AssertContainedPath(DefaultStagingRoot, resolvedOutputRoot, "Staging output root");
        AssertDrawingMayContinue(graphPath);
        var registryPath = Path.GetFullPath(request.SymbolAcceptanceRegistryPath);
        if (!File.Exists(registryPath))
            throw new FileNotFoundException(
                "The engineer-approved LRDU symbol acceptance registry does not exist. No staging subprocess was started.",
                registryPath);
        var automationRoot = _automationRoot ?? ResolveAutomationRoot();
        var scriptPath = Path.Combine(automationRoot, EngineeringStagingScriptFileName);
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("The isolated LRDU AutoCAD staging writer is not installed.", scriptPath);

        Directory.CreateDirectory(resolvedOutputRoot);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add("-GraphPath");
        start.ArgumentList.Add(graphPath);
        start.ArgumentList.Add("-OutputRoot");
        start.ArgumentList.Add(resolvedOutputRoot);
        start.ArgumentList.Add("-ProjectName");
        start.ArgumentList.Add(request.ProjectName);
        start.ArgumentList.Add(SymbolAcceptanceRegistryParameter);
        start.ArgumentList.Add(registryPath);
        start.ArgumentList.Add("-PocMode");

        var processResult = await _processExecutor.ExecuteAsync(start, cancellationToken);
        if (processResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"The isolated AutoCAD staging writer failed with exit code {processResult.ExitCode}.{Environment.NewLine}{processResult.StandardError}{Environment.NewLine}{processResult.StandardOutput}".Trim());

        var manifestPath = processResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith(AutocadStagingReviewManifest.Marker, StringComparison.Ordinal));
        if (manifestPath is null)
            throw new InvalidOperationException($"The staging writer returned no manifest marker.{Environment.NewLine}{processResult.StandardOutput}");

        return AutocadStagingReviewManifest.Load(
            manifestPath[AutocadStagingReviewManifest.Marker.Length..],
            resolvedOutputRoot);
    }

    public static string ResolveAutomationRoot()
    {
        var configured = Environment.GetEnvironmentVariable(AutomationRootEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Autocad自動畫圖")
            : Path.GetFullPath(configured.Trim());
    }

    public static string ResolveSymbolAcceptanceRegistryPath()
    {
        var configured = Environment.GetEnvironmentVariable(SymbolAcceptanceRegistryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultSymbolAcceptanceRegistryPath
            : Path.GetFullPath(configured.Trim());
    }

    private static string RequireExistingGraph(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("GraphPath is required.", nameof(path));
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved)) throw new FileNotFoundException("The LRDU staging graph does not exist.", resolved);
        return resolved;
    }

    private static void AssertDrawingMayContinue(string graphPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(graphPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) ||
                !string.Equals(schema.GetString(), "lrdu-staging-route.v1", StringComparison.Ordinal) ||
                !root.TryGetProperty("interventions", out var interventions) ||
                interventions.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The staging graph must use schema 'lrdu-staging-route.v1' and include an interventions array.");

            var blockingIds = new List<string>();
            foreach (var intervention in interventions.EnumerateArray())
            {
                if (!intervention.TryGetProperty("drawingMayContinue", out var mayContinue) ||
                    mayContinue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Every staging graph intervention must explicitly declare drawingMayContinue.");
                if (mayContinue.GetBoolean()) continue;
                blockingIds.Add(intervention.TryGetProperty("interventionId", out var identity)
                    ? identity.GetString() ?? "<missing-intervention-id>"
                    : "<missing-intervention-id>");
            }

            if (blockingIds.Count > 0)
                throw new InvalidDataException(
                    $"The staging graph contains interventions that prohibit drawing; no subprocess was started: {string.Join(", ", blockingIds)}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The staging graph is not valid JSON: {exception.Message}", exception);
        }
    }

    internal static void AssertContainedPath(string root, string candidate, string description)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{description} must stay below the Component Intelligence staging root.");
    }
}

public sealed record AutocadStagingReviewRequest
{
    public required string GraphPath { get; init; }
    public required string OutputRoot { get; init; }
    public required string ProjectName { get; init; }
    public required string SymbolAcceptanceRegistryPath { get; init; }
}

public interface IAutocadStagingProcessExecutor
{
    Task<AutocadStagingProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken);
}

public sealed record AutocadStagingProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class SystemAutocadStagingProcessExecutor : IAutocadStagingProcessExecutor
{
    public async Task<AutocadStagingProcessResult> ExecuteAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("The isolated AutoCAD staging writer could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return new AutocadStagingProcessResult(process.ExitCode, await standardOutput, await standardError);
    }
}

public static class AutocadStagingReviewManifest
{
    public const string Marker = "CM_LRDU_STAGING_WRITER_MANIFEST=";

    public static AutocadStagingReviewRunResult Load(string path, string allowedRoot)
    {
        var manifestPath = Path.GetFullPath(path.Trim());
        AutocadStagingReviewRunner.AssertContainedPath(allowedRoot, manifestPath, "Staging manifest");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("The staging review manifest does not exist.", manifestPath);
        var document = JsonSerializer.Deserialize<ManifestDocument>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The staging review manifest is empty.");

        if (!document.WriterExecuted)
            throw new InvalidDataException("The staging manifest does not confirm writer execution.");
        if (!string.Equals(document.FormalDwgModified, "NO", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staging manifest does not confirm FormalDwgModified=NO.");

        var projectPath = RequireExistingFile(document.ProjectPath, "AutoCAD Electrical WDP", allowedRoot, ".wdp");
        var drawingPaths = RequireExistingFiles(document.DrawingPaths, "AutoCAD drawing", allowedRoot, ".dwg");
        var pdfPaths = document.PdfPaths is null || document.PdfPaths.Count == 0
            ? Array.Empty<string>()
            : RequireExistingFiles(document.PdfPaths, "AutoCAD review PDF", allowedRoot, ".pdf");
        return new AutocadStagingReviewRunResult(manifestPath, projectPath, drawingPaths, pdfPaths, document.FormalDwgModified!);
    }

    private static string RequireExistingFile(string? path, string description, string allowedRoot, string extension)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException($"The staging manifest has no {description} path.");
        var fullPath = Path.GetFullPath(path);
        AutocadStagingReviewRunner.AssertContainedPath(allowedRoot, fullPath, description);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The staging manifest {description} must use the '{extension}' extension.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"The staging manifest {description} does not exist.", fullPath);
        return fullPath;
    }

    private static IReadOnlyList<string> RequireExistingFiles(
        IReadOnlyList<string>? paths,
        string description,
        string allowedRoot,
        string extension)
    {
        if (paths is null || paths.Count == 0) throw new InvalidDataException($"The staging manifest has no {description} paths.");
        return paths.Select(path => RequireExistingFile(path, description, allowedRoot, extension)).ToArray();
    }

    private sealed record ManifestDocument(
        string? ProjectPath,
        IReadOnlyList<string>? DrawingPaths,
        IReadOnlyList<string>? PdfPaths,
        bool WriterExecuted,
        string? FormalDwgModified);
}

public sealed record AutocadStagingReviewRunResult(
    string ManifestPath,
    string ProjectPath,
    IReadOnlyList<string> DrawingPaths,
    IReadOnlyList<string> PdfPaths,
    string FormalDwgModified);
