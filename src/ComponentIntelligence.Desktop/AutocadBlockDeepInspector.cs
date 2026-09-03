using System.Diagnostics;
using System.Globalization;
using System.IO;
using ComponentIntelligence.Cache;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Desktop;

public interface IBlockInspectionProcessExecutor
{
    Task<BlockInspectionProcessResult> ExecuteAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken);
}

public sealed record BlockInspectionProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class AutocadBlockDeepInspector : IBlockDeepInspector
{
    public static string DefaultStagingRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ComponentIntelligence",
        "cp3a-inspection");

    private readonly AutocadCoreConsoleLocator _locator;
    private readonly IBlockInspectionProcessExecutor _executor;
    private readonly string _stagingRoot;
    private readonly string _lispSourcePath;

    public AutocadBlockDeepInspector(
        AutocadCoreConsoleLocator? locator = null,
        IBlockInspectionProcessExecutor? executor = null,
        string? stagingRoot = null,
        string? lispSourcePath = null)
    {
        _locator = locator ?? new AutocadCoreConsoleLocator();
        _executor = executor ?? new SystemBlockInspectionProcessExecutor();
        _stagingRoot = Path.GetFullPath(stagingRoot ?? DefaultStagingRoot);
        _lispSourcePath = Path.GetFullPath(lispSourcePath ?? Path.Combine(AppContext.BaseDirectory, "Resources", "cp3a-inspect.lsp"));
    }

    public async Task<BlockDeepInspectionResult> InspectAsync(
        BlockArchiveCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var sourcePath = Path.GetFullPath(candidate.SourcePath);
        if (!File.Exists(sourcePath))
            return Failed(["SOURCE_ASSET_MISSING"], candidate.Sha256, null, null);

        var sourceHashBefore = await HashService.Sha256FileAsync(sourcePath, cancellationToken);
        string? executable;
        try
        {
            executable = _locator.Resolve();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed([$"ACCORECONSOLE_LOCATOR_FAILED:{exception.GetType().Name}:{exception.Message}"], sourceHashBefore,
                await SafeHashAsync(sourcePath, cancellationToken), null);
        }
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new BlockDeepInspectionResult
            {
                Status = DeepInspectionStatus.Unavailable,
                Diagnostics = ["ACCORECONSOLE_UNAVAILABLE"],
                SourceHashBefore = sourceHashBefore,
                SourceHashAfter = await SafeHashAsync(sourcePath, cancellationToken)
            };
        }
        if (!File.Exists(_lispSourcePath))
            return Failed(["CP3A_INSPECT_LISP_MISSING"], sourceHashBefore, await SafeHashAsync(sourcePath, cancellationToken), null);

        var runRoot = Path.Combine(_stagingRoot, $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runRoot);
        var copiedPath = Path.Combine(runRoot, "input" + Path.GetExtension(sourcePath).ToLowerInvariant());
        var lispPath = Path.Combine(runRoot, "cp3a-inspect.lsp");
        var outputPath = Path.Combine(runRoot, "metadata.tsv");
        var scriptPath = Path.Combine(runRoot, "inspect.scr");
        File.Copy(sourcePath, copiedPath, overwrite: false);
        File.Copy(_lispSourcePath, lispPath, overwrite: false);
        File.WriteAllText(scriptPath,
            $"(load \"{LispPath(lispPath)}\")\n(CI_CP3A_INSPECT \"{LispPath(outputPath)}\")\n_.QUIT\n");

        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = runRoot
        };
        start.ArgumentList.Add("/i");
        start.ArgumentList.Add(copiedPath);
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add(scriptPath);

        BlockInspectionProcessResult process;
        try
        {
            process = await _executor.ExecuteAsync(start, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed([$"ACCORECONSOLE_EXECUTION_FAILED:{exception.GetType().Name}:{exception.Message}"], sourceHashBefore,
                await SafeHashAsync(sourcePath, cancellationToken), copiedPath);
        }

        var sourceHashAfter = await SafeHashAsync(sourcePath, cancellationToken);
        if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
            return Failed(["SOURCE_HASH_CHANGED_DURING_DEEP_INSPECTION"], sourceHashBefore, sourceHashAfter, copiedPath);
        if (process.ExitCode != 0)
            return Failed([$"ACCORECONSOLE_EXIT_CODE:{process.ExitCode}", process.StandardError, process.StandardOutput], sourceHashBefore, sourceHashAfter, copiedPath);
        if (!File.Exists(outputPath))
            return Failed(["DEEP_INSPECTION_OUTPUT_MISSING"], sourceHashBefore, sourceHashAfter, copiedPath);

        try
        {
            var metadata = ParseProtocol(File.ReadAllLines(outputPath));
            return new BlockDeepInspectionResult
            {
                Status = DeepInspectionStatus.Succeeded,
                Metadata = metadata,
                Diagnostics = ["DEEP_INSPECTION_SUCCEEDED", $"DISPOSABLE_COPY:{copiedPath}"],
                SourceHashBefore = sourceHashBefore,
                SourceHashAfter = sourceHashAfter,
                InspectedCopyPath = copiedPath
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException)
        {
            return Failed([$"DEEP_INSPECTION_OUTPUT_INVALID:{exception.Message}"], sourceHashBefore, sourceHashAfter, copiedPath);
        }
    }

    public static BlockDeepInspectionMetadata ParseProtocol(IEnumerable<string> lines)
    {
        var blocks = new List<string>();
        var attributes = new List<BlockAttributeMetadata>();
        var texts = new List<string>();
        SymbolBoundingBox? boundingBox = null;
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split('\t');
            switch (parts[0])
            {
                case "BLOCK" when parts.Length == 2:
                    blocks.Add(Unescape(parts[1]));
                    break;
                case "ATTR" when parts.Length == 3:
                    attributes.Add(new BlockAttributeMetadata(Unescape(parts[1]), Unescape(parts[2])));
                    break;
                case "TEXT" when parts.Length == 2:
                    texts.Add(Unescape(parts[1]));
                    break;
                case "BBOX" when parts.Length == 5:
                    boundingBox = new SymbolBoundingBox(
                        Parse(parts[1]), Parse(parts[2]), 0,
                        Parse(parts[3]), Parse(parts[4]), 0);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported CP3-A deep-inspection protocol line: {raw}");
            }
        }
        return new BlockDeepInspectionMetadata
        {
            BlockNames = blocks.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Attributes = attributes.Distinct().OrderBy(value => value.Name, StringComparer.Ordinal).ThenBy(value => value.Value, StringComparer.Ordinal).ToArray(),
            TextLabels = texts.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            BoundingBox = boundingBox
        };
    }

    private static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    internal static string Unescape(string value)
    {
        var builder = new System.Text.StringBuilder();
        var escaping = false;
        foreach (var character in value)
        {
            if (!escaping)
            {
                if (character == '\\') escaping = true;
                else builder.Append(character);
                continue;
            }
            builder.Append(character switch { 't' => '\t', 'r' => '\r', 'n' => '\n', '\\' => '\\', _ => character });
            escaping = false;
        }
        if (escaping) throw new InvalidDataException("Trailing escape in deep-inspection protocol.");
        return builder.ToString();
    }

    private static string LispPath(string path) => Path.GetFullPath(path).Replace('\\', '/').Replace("\"", "\\\"");

    private static async Task<string?> SafeHashAsync(string path, CancellationToken cancellationToken)
    {
        try { return await HashService.Sha256FileAsync(path, cancellationToken); }
        catch { return null; }
    }

    private static BlockDeepInspectionResult Failed(
        IReadOnlyList<string> diagnostics,
        string? before,
        string? after,
        string? copy) => new()
        {
            Status = DeepInspectionStatus.Failed,
            Diagnostics = diagnostics.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            SourceHashBefore = before,
            SourceHashAfter = after,
            InspectedCopyPath = copy
        };
}

internal sealed class SystemBlockInspectionProcessExecutor : IBlockInspectionProcessExecutor
{
    public async Task<BlockInspectionProcessResult> ExecuteAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("accoreconsole could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return new BlockInspectionProcessResult(process.ExitCode, await stdout, await stderr);
    }
}
