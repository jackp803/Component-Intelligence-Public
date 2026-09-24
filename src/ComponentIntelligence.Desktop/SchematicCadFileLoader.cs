using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public sealed class SchematicCadFileLoader
{
    private readonly IBlockInspectionProcessExecutor _executor;
    private readonly string _stagingRoot;
    private readonly string? _executable;

    public SchematicCadFileLoader(IBlockInspectionProcessExecutor? executor = null, string? stagingRoot = null, string? executable = null)
    {
        _executor = executor ?? new SystemBlockInspectionProcessExecutor();
        _stagingRoot = Path.GetFullPath(stagingRoot ?? Path.Combine(AutocadBlockDeepInspector.DefaultStagingRoot, "schematic-geometry"));
        _executable = executable;
    }

    public async Task<SchematicCadAsset> ReadAsync(string source, double millimetresPerUnit, CancellationToken cancellationToken = default)
    {
        source = Path.GetFullPath(source);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".dxf" or ".dwg" or ".dwt")) throw new InvalidDataException("Expected DWG, DWT or DXF.");
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
        var run = Path.Combine(_stagingRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var input = Path.Combine(run, "input" + (extension == ".dwt" ? ".dwg" : extension));
        await File.WriteAllBytesAsync(input, bytes, cancellationToken);
        var dxf = input;
        if (extension != ".dxf")
        {
            var executable = _executable ?? new AutocadCoreConsoleLocator().Resolve();
            if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("AutoCAD Core Console is required for DWG/DWT conversion; DXF can be read directly.");
            dxf = Path.Combine(run, "display.dxf");
            var script = Path.Combine(run, "convert.scr");
            // All paths are generated inside a new staging directory; no source drawing is opened.
            var escaped = dxf.Replace('\\', '/').Replace("\"", "\\\"");
            await File.WriteAllTextAsync(script,
                $"(setvar \"FILEDIA\" 0)\n(setvar \"CMDECHO\" 0)\n(command \"_.DXFOUT\" \"{escaped}\" \"_Version\" \"2013\" \"16\")\n_.QUIT\n_N\n", cancellationToken);
            var start = new ProcessStartInfo(executable) { WorkingDirectory = run, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/i"); start.ArgumentList.Add(input); start.ArgumentList.Add("/s"); start.ArgumentList.Add(script);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var result = await _executor.ExecuteAsync(start, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(run, "stdout.log"), result.StandardOutput, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(run, "stderr.log"), result.StandardError, cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(dxf)) throw new InvalidOperationException($"CAD conversion failed ({result.ExitCode}); evidence: {run}");
        }
        var geometry = await Task.Run(() => new SchematicCadImporter().ReadDxf(dxf, millimetresPerUnit), cancellationToken);
        var after = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source, cancellationToken)));
        if (after != sourceHash) throw new IOException("Source asset changed during import; no geometry was applied.");
        return geometry with { SourceSha256 = sourceHash };
    }
}
