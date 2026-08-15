using System.Security.Cryptography;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Knowledge;

public sealed record OcrReviewCandidate(
    string Id,
    string RowId,
    string? Manufacturer,
    string? Model,
    string SourcePath,
    string SourceSha256,
    int PageNumber,
    string? ProposedKey,
    string RawName,
    string? RawValue,
    string Engine,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record OcrReviewAnalysisResult(
    bool Attempted,
    bool EngineAvailable,
    int RecognizedPages,
    int CandidateCount,
    IReadOnlyList<OcrReviewCandidate> Candidates,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Local-only OCR review queue for image files and image-only/scanned PDFs.
/// OCR output is persisted as review candidates and is intentionally NOT written directly into
/// ComponentIR. This prevents a recognition error in a pin number/function from becoming a formal
/// wiring fact without stronger evidence or explicit confirmation.
/// </summary>
public sealed class OcrReviewQueueService
{
    private const int SparsePdfCharacterThreshold = 80;
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _connectionFactory = new();
    private readonly PdfTextExtractor _pdfText = new();
    private readonly PdfPageImageExtractor _pdfImages = new();
    private readonly SpecificationParser _parser = new();
    private readonly OcrCandidateParser _ocrCandidates = new();
    private readonly IOcrTextExtractor _ocr;

    public OcrReviewQueueService(string databasePath, IOcrTextExtractor? ocr = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _ocr = ocr ?? TesseractCliOcrTextExtractor.Detect();
    }

    public async Task<OcrReviewAnalysisResult> AnalyzeAsync(
        string rowId,
        string? manufacturer,
        string? model,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("OCR review source file was not found.", sourceFilePath);

        var diagnostics = new List<string>();
        var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        var sha256 = await ComputeSha256Async(sourceFilePath, cancellationToken);
        var sourceUri = FileUri(sourceFilePath);
        var inputs = new List<(int Page, byte[] Bytes, string Extension)>();

        if (extension == ".pdf")
        {
            var pages = _pdfText.Extract(sourceFilePath);
            var nativeCharacters = pages.Sum(page => page.Text.Count(character => !char.IsWhiteSpace(character)));
            if (nativeCharacters >= SparsePdfCharacterThreshold)
                return new OcrReviewAnalysisResult(false, _ocr.IsAvailable, 0, 0, Array.Empty<OcrReviewCandidate>(), [$"OCR_NOT_REQUIRED:PDF_NATIVE_TEXT:{nativeCharacters}"]);

            diagnostics.Add($"OCR_REQUIRED:PDF_NATIVE_TEXT_SPARSE:{nativeCharacters}");
            try
            {
                foreach (var image in _pdfImages.Extract(sourceFilePath))
                    inputs.Add((image.PageNumber, image.Bytes, image.Extension));
            }
            catch (Exception exception)
            {
                diagnostics.Add($"PDF_IMAGE_EXTRACTION_FAILED:{exception.GetType().Name}:{exception.Message}");
            }
        }
        else if (IsImageFile(extension))
        {
            inputs.Add((1, await File.ReadAllBytesAsync(sourceFilePath, cancellationToken), extension));
            diagnostics.Add("OCR_REQUIRED:IMAGE_FILE");
        }
        else
        {
            return new OcrReviewAnalysisResult(false, _ocr.IsAvailable, 0, 0, Array.Empty<OcrReviewCandidate>(), ["OCR_NOT_APPLICABLE"]);
        }

        if (inputs.Count == 0)
        {
            diagnostics.Add("OCR_NO_IMAGE_INPUT_AVAILABLE");
            return new OcrReviewAnalysisResult(true, _ocr.IsAvailable, 0, 0, Array.Empty<OcrReviewCandidate>(), diagnostics);
        }

        if (!_ocr.IsAvailable)
        {
            diagnostics.Add("LOCAL_OCR_ENGINE_NOT_AVAILABLE");
            return new OcrReviewAnalysisResult(true, false, 0, 0, Array.Empty<OcrReviewCandidate>(), diagnostics);
        }

        var rawCandidates = new List<RawSpecification>();
        var recognizedPages = 0;
        var engineName = _ocr.EngineName;
        foreach (var input in inputs)
        {
            var result = await _ocr.ExtractAsync(input.Bytes, input.Extension, cancellationToken);
            engineName = result.Engine ?? engineName;
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Text))
            {
                diagnostics.Add($"OCR_PAGE_FAILED:{input.Page}:{result.Error ?? "UNKNOWN"}");
                continue;
            }

            recognizedPages++;
            rawCandidates.AddRange(_parser
                .ParseText(
                    result.Text,
                    sourceUri,
                    input.Page,
                    sha256,
                    ComponentSourceType.User,
                    ExtractionMethod.OcrText)
                .Select(MarkOcrAsInferred));
            rawCandidates.AddRange(_ocrCandidates.Parse(
                result.Text,
                sourceUri,
                input.Page,
                sha256,
                ComponentSourceType.User));
            diagnostics.Add($"OCR_PAGE_RECOGNIZED:{input.Page}:{engineName}");
        }

        var deduped = rawCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.RawName) && !string.IsNullOrWhiteSpace(candidate.RawValue))
            .GroupBy(candidate => $"{candidate.Evidence.FirstOrDefault()?.PageNumber}\u001f{candidate.ProposedKey}\u001f{candidate.RawName}\u001f{candidate.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with
            {
                Evidence = group.SelectMany(candidate => candidate.Evidence).Distinct().ToArray()
            })
            .ToArray();

        if (recognizedPages > 0 && deduped.Length == 0)
            diagnostics.Add("OCR_TEXT_RECOGNIZED_NO_ENGINEERING_CANDIDATES");

        var persisted = await PersistAsync(
            rowId,
            manufacturer,
            model,
            sourceFilePath,
            sha256,
            engineName,
            deduped,
            cancellationToken);

        return new OcrReviewAnalysisResult(true, true, recognizedPages, persisted.Count, persisted, diagnostics);
    }

    public async Task<IReadOnlyList<OcrReviewCandidate>> GetPendingAsync(
        string? manufacturer,
        string? model,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
            return Array.Empty<OcrReviewCandidate>();

        using var connection = _connectionFactory.Open(_databasePath);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, row_id, manufacturer, model, source_path, source_sha256, page_number,
                   proposed_key, raw_name, raw_value, engine, status, created_at
            FROM component_ocr_review_candidates
            WHERE manufacturer = $manufacturer COLLATE NOCASE
              AND model = $model COLLATE NOCASE
              AND status = 'Pending'
            ORDER BY created_at DESC, page_number, raw_name;
            """;
        command.Parameters.AddWithValue("$manufacturer", manufacturer.Trim());
        command.Parameters.AddWithValue("$model", model.Trim());

        var results = new List<OcrReviewCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new OcrReviewCandidate(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                DateTimeOffset.Parse(reader.GetString(12))));
        }
        return results;
    }

    private async Task<IReadOnlyList<OcrReviewCandidate>> PersistAsync(
        string rowId,
        string? manufacturer,
        string? model,
        string sourcePath,
        string sourceSha256,
        string engine,
        IReadOnlyList<RawSpecification> candidates,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.Open(_databasePath);
        await EnsureSchemaAsync(connection, cancellationToken);
        var persisted = new List<OcrReviewCandidate>();

        foreach (var candidate in candidates)
        {
            var page = candidate.Evidence.Select(evidence => evidence.PageNumber).FirstOrDefault(value => value is not null) ?? 1;
            var rawValue = candidate.RawValue;
            var stable = $"{rowId}|{sourceSha256}|{page}|{candidate.ProposedKey}|{candidate.RawName}|{rawValue}";
            var id = $"OCR-{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stable)).AsSpan(0, 8))}";
            var createdAt = DateTimeOffset.UtcNow;

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO component_ocr_review_candidates
                    (id, row_id, manufacturer, model, source_path, source_sha256, page_number,
                     proposed_key, raw_name, raw_value, engine, status, created_at)
                VALUES
                    ($id, $row, $manufacturer, $model, $path, $sha, $page,
                     $key, $name, $value, $engine, 'Pending', $created)
                ON CONFLICT(id) DO UPDATE SET
                    manufacturer = excluded.manufacturer,
                    model = excluded.model,
                    source_path = excluded.source_path,
                    engine = excluded.engine;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$row", rowId);
            command.Parameters.AddWithValue("$manufacturer", (object?)manufacturer?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("$model", (object?)model?.Trim() ?? DBNull.Value);
            command.Parameters.AddWithValue("$path", Path.GetFullPath(sourcePath));
            command.Parameters.AddWithValue("$sha", sourceSha256);
            command.Parameters.AddWithValue("$page", page);
            command.Parameters.AddWithValue("$key", (object?)candidate.ProposedKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$name", candidate.RawName);
            command.Parameters.AddWithValue("$value", (object?)rawValue ?? DBNull.Value);
            command.Parameters.AddWithValue("$engine", engine);
            command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);

            persisted.Add(new OcrReviewCandidate(
                id,
                rowId,
                manufacturer?.Trim(),
                model?.Trim(),
                Path.GetFullPath(sourcePath),
                sourceSha256,
                page,
                candidate.ProposedKey,
                candidate.RawName,
                rawValue,
                engine,
                "Pending",
                createdAt));
        }

        return persisted;
    }

    private static async Task EnsureSchemaAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS component_ocr_review_candidates (
                id TEXT NOT NULL PRIMARY KEY,
                row_id TEXT NOT NULL,
                manufacturer TEXT NULL COLLATE NOCASE,
                model TEXT NULL COLLATE NOCASE,
                source_path TEXT NOT NULL,
                source_sha256 TEXT NOT NULL,
                page_number INTEGER NOT NULL,
                proposed_key TEXT NULL,
                raw_name TEXT NOT NULL,
                raw_value TEXT NULL,
                engine TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_component_ocr_review_identity
                ON component_ocr_review_candidates(manufacturer, model, status);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static RawSpecification MarkOcrAsInferred(RawSpecification specification) => specification with
    {
        Status = VerificationStatus.Inferred,
        Evidence = specification.Evidence.Select(evidence => evidence with
        {
            ExtractionMethod = ExtractionMethod.OcrText,
            VerificationStatus = VerificationStatus.Inferred
        }).ToArray()
    };

    private static bool IsImageFile(string extension) => extension is
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff";

    private static Uri FileUri(string path) =>
        new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = Path.GetFullPath(path) }.Uri;

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
