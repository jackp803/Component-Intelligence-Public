using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ComponentIntelligence.Extraction;

public sealed record OcrTextResult(
    bool IsAvailable,
    bool Succeeded,
    string? Text,
    string? Engine,
    string? Error = null,
    IReadOnlyList<OcrTextBox>? Boxes = null,
    IReadOnlyList<string>? Diagnostics = null);

/// <summary>
/// Deterministic/local OCR boundary. The application never downloads an OCR model or sends an image
/// to a cloud service. A local Tesseract executable is used only when it is already available.
/// </summary>
public interface IOcrTextExtractor
{
    string EngineName { get; }
    bool IsAvailable { get; }
    Task<OcrTextResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes,
        string extension,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledOcrTextExtractor : IOcrTextExtractor
{
    public string EngineName => "disabled";
    public bool IsAvailable => false;

    public Task<OcrTextResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes,
        string extension,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OcrTextResult(false, false, null, EngineName, "LOCAL_OCR_ENGINE_NOT_AVAILABLE"));
}

/// <summary>
/// Optional local OCR implementation using an existing Tesseract CLI installation.
/// Detection order:
/// 1) COMPONENT_INTELLIGENCE_TESSERACT environment variable;
/// 2) PATH;
/// 3) conventional Windows Program Files locations.
/// OCR language can be overridden by COMPONENT_INTELLIGENCE_OCR_LANG, for example eng+chi_tra.
///
/// Precision policy: scanned engineering documents are OCR'd with two page segmentation modes.
/// PSM 6 is strong for dense specification blocks/tables, while PSM 11 is useful for sparse labels,
/// diagrams and separated callouts. The result with the stronger deterministic engineering-text score
/// is selected. The selected PSM is then run once in TSV mode to obtain word bounding boxes for
/// non-AI text-to-geometry matching.
/// </summary>
public sealed class TesseractCliOcrTextExtractor : IOcrTextExtractor
{
    private static readonly int[] PrecisionPsms = [6, 11];
    private static readonly string[] EngineeringTokens =
    [
        "voltage", "current", "power", "supply", "connector", "connection", "pin", "contact", "port",
        "input", "output", "m12", "m8", "rj45", "io-link", "iolink", "rs485", "rs-485", "ethernet",
        "ethercat", "profinet", "modbus", "24v", "0v", "ma", "mm", "awg", "ip", "接頭", "接头",
        "腳位", "脚位", "端子", "電壓", "电压", "電流", "电流", "輸入", "输入", "輸出", "输出"
    ];

    private readonly string _executablePath;
    private readonly string _language;
    private readonly TimeSpan _timeout;

    public TesseractCliOcrTextExtractor(
        string executablePath,
        string? language = null,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = executablePath;
        _language = string.IsNullOrWhiteSpace(language)
            ? Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_OCR_LANG")?.Trim() ?? "eng"
            : language.Trim();
        _timeout = timeout ?? TimeSpan.FromSeconds(45);
    }

    public string EngineName => $"tesseract-cli/{_language}";
    public bool IsAvailable => File.Exists(_executablePath);

    public static IOcrTextExtractor Detect()
    {
        var executable = FindExecutable();
        return executable is null
            ? new DisabledOcrTextExtractor()
            : new TesseractCliOcrTextExtractor(executable);
    }

    public async Task<OcrTextResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes,
        string extension,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new OcrTextResult(false, false, null, EngineName, "LOCAL_OCR_ENGINE_NOT_AVAILABLE");
        if (imageBytes.IsEmpty)
            return new OcrTextResult(true, false, null, EngineName, "OCR_IMAGE_EMPTY");

        var safeExtension = NormalizeExtension(extension);
        var tempPath = Path.Combine(Path.GetTempPath(), $"component-intelligence-ocr-{Guid.NewGuid():N}{safeExtension}");
        try
        {
            await File.WriteAllBytesAsync(tempPath, imageBytes.ToArray(), cancellationToken);

            var passes = new List<OcrPassResult>();
            foreach (var psm in PrecisionPsms)
                passes.Add(await RunPassAsync(tempPath, psm, cancellationToken));

            var successful = passes
                .Where(pass => pass.Succeeded && !string.IsNullOrWhiteSpace(pass.Text))
                .OrderByDescending(pass => ScoreEngineeringText(pass.Text!))
                .ThenByDescending(pass => pass.Text!.Length)
                .FirstOrDefault();

            if (successful is not null)
            {
                var diagnostics = new List<string> { $"OCR_SELECTED_PSM:{successful.Psm}" };
                IReadOnlyList<OcrTextBox> boxes = Array.Empty<OcrTextBox>();
                var tsv = await RunTsvPassAsync(tempPath, successful.Psm, cancellationToken);
                if (tsv.Succeeded && !string.IsNullOrWhiteSpace(tsv.Text))
                {
                    var parsed = TesseractTsvParser.Parse(tsv.Text);
                    boxes = parsed.Boxes;
                    diagnostics.AddRange(parsed.Diagnostics);
                }
                else
                {
                    diagnostics.Add(tsv.Error ?? "OCR_TSV_UNAVAILABLE");
                }

                return new OcrTextResult(
                    true,
                    true,
                    successful.Text,
                    $"{EngineName}/psm{successful.Psm}",
                    Boxes: boxes,
                    Diagnostics: diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }

            var errors = passes.Select(pass => pass.Error).Where(error => !string.IsNullOrWhiteSpace(error)).Distinct().ToArray();
            return new OcrTextResult(true, false, null, EngineName,
                errors.Length == 0 ? "OCR_NO_TEXT" : string.Join(" | ", errors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new OcrTextResult(true, false, null, EngineName, $"OCR_ERROR:{exception.GetType().Name}:{exception.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Temporary OCR files are non-authoritative; cleanup is best effort.
            }
        }
    }

    private Task<OcrPassResult> RunPassAsync(string tempPath, int psm, CancellationToken cancellationToken) =>
        RunProcessAsync(tempPath, psm, tsv: false, cancellationToken);

    private Task<OcrPassResult> RunTsvPassAsync(string tempPath, int psm, CancellationToken cancellationToken) =>
        RunProcessAsync(tempPath, psm, tsv: true, cancellationToken);

    private async Task<OcrPassResult> RunProcessAsync(
        string tempPath,
        int psm,
        bool tsv,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(tempPath);
        start.ArgumentList.Add("stdout");
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add(_language);
        start.ArgumentList.Add("--psm");
        start.ArgumentList.Add(psm.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("preserve_interword_spaces=1");
        if (tsv) start.ArgumentList.Add("tsv");

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            return new OcrPassResult(psm, false, null, tsv ? "OCR_TSV_PROCESS_START_FAILED" : "OCR_PROCESS_START_FAILED");

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cancellation only.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(_timeout, cancellationToken);
        var completed = await Task.WhenAny(exitTask, timeoutTask);
        cancellationToken.ThrowIfCancellationRequested();

        if (completed == timeoutTask)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort timeout cleanup only.
            }
            return new OcrPassResult(psm, false, null, tsv ? $"OCR_TSV_PSM{psm}_TIMEOUT" : $"OCR_PSM{psm}_TIMEOUT");
        }

        await exitTask;
        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();
        if (process.ExitCode != 0)
            return new OcrPassResult(psm, false, null,
                $"{(tsv ? "OCR_TSV" : "OCR")}_PSM{psm}_EXIT_{process.ExitCode}:{stderr}");
        if (string.IsNullOrWhiteSpace(stdout))
            return new OcrPassResult(psm, false, null,
                $"{(tsv ? "OCR_TSV" : "OCR")}_PSM{psm}_NO_TEXT");

        return new OcrPassResult(psm, true, stdout, null);
    }

    internal static int ScoreEngineeringText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var normalized = Regex.Replace(text, @"\s+", " ").ToLowerInvariant();
        var score = EngineeringTokens.Count(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase)) * 30;

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var hasLetter = line.Any(char.IsLetter);
            var hasDigit = line.Any(char.IsDigit);
            if (hasLetter && hasDigit) score += 5;
            if (line.Contains(':') || line.Contains('：') || Regex.IsMatch(line, @"\s{2,}")) score += 4;
            if (Regex.IsMatch(line, @"\b\d+(?:[.,]\d+)?\s*(?:V|mA|A|W|mm|bar|Hz|°C)\b", RegexOptions.IgnoreCase)) score += 10;
        }

        score += Math.Min(100, normalized.Count(char.IsLetterOrDigit) / 20);
        return score;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return ".png";
        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        return normalized.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff"
            ? normalized.ToLowerInvariant()
            : ".png";
    }

    private static string? FindExecutable()
    {
        var explicitPath = Environment.GetEnvironmentVariable("COMPONENT_INTELLIGENCE_TESSERACT")?.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        var executableName = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var candidate = Path.Combine(segment.Trim('"'), executableName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Ignore malformed PATH segments.
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     }.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var candidate = Path.Combine(root, "Tesseract-OCR", "tesseract.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private sealed record OcrPassResult(int Psm, bool Succeeded, string? Text, string? Error);
}
