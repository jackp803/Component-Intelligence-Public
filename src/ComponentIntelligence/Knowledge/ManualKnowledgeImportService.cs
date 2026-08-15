using System.Security.Cryptography;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Extraction;
using ComponentIntelligence.Normalization;
using ComponentIntelligence.Repository;
using ComponentIntelligence.Resolution;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Knowledge;

public enum ManualKnowledgeImportStatus
{
    ImportedToComponentIr,
    StoredForIdentityReview,
    StoredAsEvidenceOnly
}

public sealed record ManualKnowledgeImportResult(
    ManualKnowledgeImportStatus Status,
    string StoredPath,
    string Sha256,
    int ExtractedSpecificationCount,
    bool NeedsAiReview,
    ComponentIR? Component,
    VerificationSummary? Verification,
    RawComponentProfile? Raw,
    IReadOnlyList<string> Issues);

/// <summary>
/// Imports user-supplied engineering documents into the local Component Intelligence knowledge base.
/// No AI call is performed. PDF/text content is parsed deterministically when possible; every file is
/// still preserved as evidence so future parsers or AI review can re-process it without asking the user
/// to upload the document again.
/// </summary>
public sealed class ManualKnowledgeImportService
{
    private readonly string _databasePath;
    private readonly string _knowledgeRoot;
    private readonly SqliteConnectionFactory _connectionFactory = new();
    private readonly SqliteComponentIrRepository _componentRepository;
    private readonly SpecificationParser _parser = new();
    private readonly PdfTextExtractor _pdf = new();
    private readonly PdfTableExtractor _pdfTables = new();
    private readonly PinoutExtractor _pinout = new();
    private readonly ComponentNormalizer _normalizer = new();
    private readonly VerificationEngine _verification = new();

    public ManualKnowledgeImportService(string databasePath, string? knowledgeRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _componentRepository = new SqliteComponentIrRepository(databasePath, _connectionFactory);
        _knowledgeRoot = knowledgeRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComponentIntelligence",
            "knowledge");
    }

    public async Task<ManualKnowledgeImportResult> ImportAsync(
        string rowId,
        string? manufacturer,
        string? model,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Knowledge source file was not found.", sourceFilePath);

        var manufacturerText = manufacturer?.Trim();
        var modelText = model?.Trim();
        var normalizedManufacturer = ManufacturerNormalizer.NormalizeKey(manufacturerText) ?? manufacturerText;
        var normalizedModel = ModelNormalizer.Normalize(modelText)?.Canonical ?? modelText;

        var sha256 = await ComputeSha256Async(sourceFilePath, cancellationToken);
        var storedPath = CopyIntoKnowledgeStore(rowId, normalizedManufacturer, normalizedModel, sourceFilePath, sha256);
        var documentUri = FileUri(storedPath);
        var extension = Path.GetExtension(storedPath).ToLowerInvariant();
        var documentType = InferDocumentType(Path.GetFileName(sourceFilePath));
        var issues = new List<string>();
        var specs = new List<RawSpecification>();
        var needsAiReview = false;

        try
        {
            if (extension == ".pdf")
            {
                var pages = _pdf.Extract(storedPath);
                foreach (var page in pages)
                    specs.AddRange(_parser.ParseText(page.Text, documentUri, page.PageNumber, sha256, ComponentSourceType.User));

                var tableRows = _pdfTables.Extract(storedPath);
                specs.AddRange(_parser.ParseTableRows(tableRows, documentUri, sha256, ComponentSourceType.User));

                needsAiReview = pages.Count > 0 && pages.All(page => string.IsNullOrWhiteSpace(page.Text));
                if (needsAiReview) issues.Add("NEEDS_AI_REVIEW:PDF_HAS_NO_EXTRACTABLE_TEXT");
            }
            else if (IsTextFile(extension))
            {
                var text = await File.ReadAllTextAsync(storedPath, cancellationToken);
                specs.AddRange(_parser.ParseText(text, documentUri, 1, sha256, ComponentSourceType.User, ExtractionMethod.UserInput));
            }
            else
            {
                needsAiReview = IsImageFile(extension);
                issues.Add(needsAiReview
                    ? "NEEDS_AI_REVIEW:IMAGE_OR_SCANNED_DOCUMENT"
                    : $"STORED_AS_EVIDENCE_ONLY:{extension}");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            issues.Add($"DOCUMENT_PARSE_ERROR:{exception.GetType().Name}:{exception.Message}");
        }

        var dedupedSpecs = specs
            .Where(spec => !string.IsNullOrWhiteSpace(spec.RawName) && !string.IsNullOrWhiteSpace(spec.RawValue))
            .GroupBy(spec => $"{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
            .ToArray();

        await PersistDocumentAsync(
            rowId,
            normalizedManufacturer,
            normalizedModel,
            sourceFilePath,
            storedPath,
            sha256,
            documentType,
            dedupedSpecs,
            cancellationToken);

        if (IsUnresolvedIdentity(normalizedManufacturer, normalizedModel))
        {
            issues.Add("IDENTITY_REQUIRED_BEFORE_COMPONENT_IR");
            return new ManualKnowledgeImportResult(
                ManualKnowledgeImportStatus.StoredForIdentityReview,
                storedPath,
                sha256,
                dedupedSpecs.Length,
                needsAiReview,
                null,
                null,
                null,
                issues);
        }

        var identity = new ComponentIdentity
        {
            OfficialManufacturer = normalizedManufacturer!,
            OfficialModel = normalizedModel!,
            Mpn = normalizedModel,
            OfficialProductUrl = null
        };
        var document = new ComponentDocument
        {
            Type = documentType,
            Url = documentUri,
            LocalPath = storedPath,
            Sha256 = sha256,
            SourceType = ComponentSourceType.User
        };
        var fileEvidence = new Evidence
        {
            SourceType = ComponentSourceType.User,
            SourceUrl = documentUri,
            DocumentUrl = documentUri,
            DocumentHashSha256 = sha256,
            ExtractionMethod = ExtractionMethod.UserInput,
            RawValue = Path.GetFileName(sourceFilePath),
            RetrievedAt = DateTimeOffset.UtcNow,
            VerificationStatus = VerificationStatus.SingleSource
        };
        var extractedPins = _pinout.Extract(dedupedSpecs);
        var raw = new RawComponentProfile
        {
            Identity = identity,
            Specifications = dedupedSpecs,
            Pins = extractedPins,
            Documents = [document],
            Evidence = [fileEvidence, .. dedupedSpecs.SelectMany(spec => spec.Evidence).Distinct()],
            MissingData = issues
        };

        var imported = await _normalizer.NormalizeAsync(raw, cancellationToken);
        if (extension == ".pdf")
            imported = imported with { Assets = imported.Assets with { DatasheetUrl = documentUri } };

        var existing = await _componentRepository.FindByIdentityAsync(normalizedManufacturer!, normalizedModel!, cancellationToken);
        var merged = Merge(existing, imported);
        var verificationRaw = SnapshotForVerification(merged, raw);

        // Re-normalize the merged evidence set through the shared SourceTrustPolicy. A newly uploaded
        // user document may fill an unknown field but must not win merely because it arrived later than
        // higher-trust manufacturer evidence. Conflicting raw values remain present for verification.
        var trustedNormalized = await _normalizer.NormalizeAsync(verificationRaw, cancellationToken);
        merged = merged with
        {
            Power = new ComponentPower
            {
                OperatingVoltage = trustedNormalized.Power.OperatingVoltage ?? merged.Power.OperatingVoltage,
                CurrentConsumptionAmp = trustedNormalized.Power.CurrentConsumptionAmp ?? merged.Power.CurrentConsumptionAmp,
                MaximumCurrentAmp = trustedNormalized.Power.MaximumCurrentAmp ?? merged.Power.MaximumCurrentAmp,
                PowerConsumptionWatt = trustedNormalized.Power.PowerConsumptionWatt ?? merged.Power.PowerConsumptionWatt
            },
            Io = new ComponentIo
            {
                OutputType = trustedNormalized.Io.OutputType ?? merged.Io.OutputType
            },
            Connector = new ComponentConnector
            {
                Family = trustedNormalized.Connector.Family ?? merged.Connector.Family,
                Coding = trustedNormalized.Connector.Coding ?? merged.Connector.Coding,
                Pins = trustedNormalized.Connector.Pins ?? merged.Connector.Pins
            }
        };

        var verification = await _verification.VerifyAsync(merged, verificationRaw, cancellationToken);
        merged = merged with { Readiness = verification.Readiness };
        await _componentRepository.SaveAsync(merged, cancellationToken);

        return new ManualKnowledgeImportResult(
            dedupedSpecs.Length > 0 ? ManualKnowledgeImportStatus.ImportedToComponentIr : ManualKnowledgeImportStatus.StoredAsEvidenceOnly,
            storedPath,
            sha256,
            dedupedSpecs.Length,
            needsAiReview,
            merged,
            verification,
            verificationRaw,
            issues.Concat(verification.Issues).Distinct().ToArray());
    }

    private async Task PersistDocumentAsync(
        string rowId,
        string? manufacturer,
        string? model,
        string originalPath,
        string storedPath,
        string sha256,
        string documentType,
        IReadOnlyList<RawSpecification> specs,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.Open(_databasePath);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS component_knowledge_documents (
                    id TEXT NOT NULL PRIMARY KEY,
                    row_id TEXT NOT NULL,
                    manufacturer TEXT NULL COLLATE NOCASE,
                    model TEXT NULL COLLATE NOCASE,
                    original_name TEXT NOT NULL,
                    local_path TEXT NOT NULL,
                    sha256 TEXT NOT NULL,
                    document_type TEXT NOT NULL,
                    added_at TEXT NOT NULL,
                    UNIQUE(row_id, sha256)
                );
                CREATE TABLE IF NOT EXISTS component_knowledge_specs (
                    document_id TEXT NOT NULL,
                    proposed_key TEXT NULL,
                    raw_name TEXT NOT NULL,
                    raw_value TEXT NULL,
                    page_number INTEGER NULL,
                    verification_status TEXT NOT NULL,
                    PRIMARY KEY(document_id, proposed_key, raw_name, raw_value),
                    FOREIGN KEY(document_id) REFERENCES component_knowledge_documents(id) ON DELETE CASCADE
                );
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        var documentId = $"DOC-{sha256[..16]}-{SanitizeKey(rowId)}";
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO component_knowledge_documents
                    (id, row_id, manufacturer, model, original_name, local_path, sha256, document_type, added_at)
                VALUES
                    ($id, $row, $manufacturer, $model, $name, $path, $sha, $type, $added)
                ON CONFLICT(row_id, sha256) DO UPDATE SET
                    manufacturer = excluded.manufacturer,
                    model = excluded.model,
                    original_name = excluded.original_name,
                    local_path = excluded.local_path,
                    document_type = excluded.document_type,
                    added_at = excluded.added_at;
                """;
            command.Parameters.AddWithValue("$id", documentId);
            command.Parameters.AddWithValue("$row", rowId);
            command.Parameters.AddWithValue("$manufacturer", (object?)manufacturer ?? DBNull.Value);
            command.Parameters.AddWithValue("$model", (object?)model ?? DBNull.Value);
            command.Parameters.AddWithValue("$name", Path.GetFileName(originalPath));
            command.Parameters.AddWithValue("$path", storedPath);
            command.Parameters.AddWithValue("$sha", sha256);
            command.Parameters.AddWithValue("$type", documentType);
            command.Parameters.AddWithValue("$added", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteSpecs = connection.CreateCommand())
        {
            deleteSpecs.CommandText = "DELETE FROM component_knowledge_specs WHERE document_id = $id;";
            deleteSpecs.Parameters.AddWithValue("$id", documentId);
            await deleteSpecs.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var spec in specs)
        {
            var page = spec.Evidence.Select(item => item.PageNumber).FirstOrDefault(value => value is not null);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO component_knowledge_specs
                    (document_id, proposed_key, raw_name, raw_value, page_number, verification_status)
                VALUES
                    ($document, $key, $name, $value, $page, $status);
                """;
            command.Parameters.AddWithValue("$document", documentId);
            command.Parameters.AddWithValue("$key", (object?)spec.ProposedKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$name", spec.RawName);
            command.Parameters.AddWithValue("$value", (object?)spec.RawValue ?? DBNull.Value);
            command.Parameters.AddWithValue("$page", (object?)page ?? DBNull.Value);
            command.Parameters.AddWithValue("$status", spec.Status.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private string CopyIntoKnowledgeStore(string rowId, string? manufacturer, string? model, string sourcePath, string sha256)
    {
        var folder = Path.Combine(
            _knowledgeRoot,
            SanitizeKey(manufacturer ?? "UNRESOLVED"),
            SanitizeKey(model ?? rowId));
        Directory.CreateDirectory(folder);
        var fileName = $"{sha256[..12]}_{SanitizeFileName(Path.GetFileName(sourcePath))}";
        var target = Path.Combine(folder, fileName);
        if (!File.Exists(target)) File.Copy(sourcePath, target, overwrite: false);
        return target;
    }

    private static ComponentIR Merge(ComponentIR? existing, ComponentIR imported)
    {
        if (existing is null) return imported;

        var specifications = existing.Specifications
            .Concat(imported.Specifications)
            .GroupBy(spec => $"{spec.Key}\u001f{spec.Name}\u001f{spec.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
            .ToArray();
        var documents = existing.Documents
            .Concat(imported.Documents)
            .GroupBy(document => document.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(document => SourceTrustPolicy.Score(document.SourceType))
                .ThenByDescending(document => !string.IsNullOrWhiteSpace(document.Sha256))
                .First())
            .ToArray();
        var pins = existing.Pins
            .Concat(imported.Pins)
            .GroupBy(pin => pin.PinNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group
                    .OrderByDescending(pin => !string.IsNullOrWhiteSpace(pin.Function))
                    .ThenByDescending(pin => SourceTrustPolicy.Score(pin.Evidence))
                    .First();
                return preferred with { Evidence = group.SelectMany(pin => pin.Evidence).Distinct().ToArray() };
            })
            .ToArray();
        var ports = existing.Ports
            .Concat(imported.Ports)
            .GroupBy(port => port.PortId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return imported with
        {
            Identity = existing.Identity,
            Classification = new ComponentClassification
            {
                Category = existing.Classification.Category ?? imported.Classification.Category,
                Subcategory = existing.Classification.Subcategory ?? imported.Classification.Subcategory
            },
            Power = new ComponentPower
            {
                OperatingVoltage = existing.Power.OperatingVoltage ?? imported.Power.OperatingVoltage,
                CurrentConsumptionAmp = existing.Power.CurrentConsumptionAmp ?? imported.Power.CurrentConsumptionAmp,
                MaximumCurrentAmp = existing.Power.MaximumCurrentAmp ?? imported.Power.MaximumCurrentAmp,
                PowerConsumptionWatt = existing.Power.PowerConsumptionWatt ?? imported.Power.PowerConsumptionWatt
            },
            Io = new ComponentIo { OutputType = existing.Io.OutputType ?? imported.Io.OutputType },
            Connector = new ComponentConnector
            {
                Family = existing.Connector.Family ?? imported.Connector.Family,
                Coding = existing.Connector.Coding ?? imported.Connector.Coding,
                Pins = existing.Connector.Pins ?? imported.Connector.Pins
            },
            Ports = ports,
            Pins = pins,
            Specifications = specifications,
            Documents = documents,
            Assets = new ComponentAssets
            {
                ProductPageUrl = existing.Assets.ProductPageUrl ?? imported.Assets.ProductPageUrl,
                DatasheetUrl = existing.Assets.DatasheetUrl ?? imported.Assets.DatasheetUrl,
                ImageUrl = existing.Assets.ImageUrl ?? imported.Assets.ImageUrl,
                CadUrl = existing.Assets.CadUrl ?? imported.Assets.CadUrl
            }
        };
    }

    private static RawComponentProfile SnapshotForVerification(ComponentIR component, RawComponentProfile latest)
    {
        var specs = component.Specifications.Select(spec => new RawSpecification
        {
            RawName = spec.Name,
            Section = spec.Section,
            RawValue = spec.Value,
            ProposedKey = spec.Key,
            Status = spec.Status,
            Evidence = spec.Evidence
        }).ToArray();
        return latest with
        {
            Identity = latest.Identity with { OfficialProductUrl = component.Assets.ProductPageUrl ?? latest.Identity.OfficialProductUrl },
            Specifications = specs,
            Pins = component.Pins,
            Ports = component.Ports,
            Documents = component.Documents,
            Assets = BuildSnapshotAssets(component, latest.Assets),
            Evidence = latest.Evidence.Concat(specs.SelectMany(spec => spec.Evidence)).Distinct().ToArray()
        };
    }

    private static IReadOnlyList<ComponentAsset> BuildSnapshotAssets(ComponentIR component, IReadOnlyList<ComponentAsset> latest)
    {
        var assets = latest.ToList();
        if (component.Assets.ProductPageUrl is not null)
            assets.Add(new ComponentAsset { Type = "product-page", Url = component.Assets.ProductPageUrl });
        if (component.Assets.DatasheetUrl is not null)
            assets.Add(new ComponentAsset { Type = "datasheet", Url = component.Assets.DatasheetUrl });
        if (component.Assets.ImageUrl is not null)
            assets.Add(new ComponentAsset { Type = "image", Url = component.Assets.ImageUrl });
        if (component.Assets.CadUrl is not null)
            assets.Add(new ComponentAsset { Type = "cad", Url = component.Assets.CadUrl });
        return assets
            .GroupBy(asset => $"{asset.Type}\u001f{asset.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsUnresolvedIdentity(string? manufacturer, string? model)
    {
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model)) return true;
        if (manufacturer.Equals("TBD", StringComparison.OrdinalIgnoreCase) ||
            manufacturer.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
            model.Equals("TBD", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("TBD (", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsTextFile(string extension) => extension is ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".log";
    private static bool IsImageFile(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff";

    private static string InferDocumentType(string fileName)
    {
        if (fileName.Contains("datasheet", StringComparison.OrdinalIgnoreCase) || fileName.Contains("data sheet", StringComparison.OrdinalIgnoreCase)) return "datasheet";
        if (fileName.Contains("manual", StringComparison.OrdinalIgnoreCase) || fileName.Contains("instruction", StringComparison.OrdinalIgnoreCase)) return "manual";
        if (Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return "user-pdf";
        return "user-document";
    }

    private static Uri FileUri(string path) => new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = Path.GetFullPath(path) }.Uri;

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string SanitizeKey(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Trim().Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray();
        var result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? "UNKNOWN" : result;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}
