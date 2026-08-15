using ComponentIntelligence.Cache;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Extraction;

public sealed record DocumentExtractionResult(
    ComponentDocument Document,
    IReadOnlyList<RawSpecification> Specifications,
    bool NeedsAiReview,
    string? Error = null,
    IReadOnlyList<string>? Diagnostics = null,
    DocumentIdentityStatus IdentityStatus = DocumentIdentityStatus.NotChecked,
    DiagramGeometryResult? Geometry = null,
    IReadOnlyList<DiagramLabelMatch>? GeometryLabelMatches = null,
    string? EngineeringMarkdownPath = null)
{
    public bool IsTrustedForTarget => IdentityStatus is DocumentIdentityStatus.NotChecked or DocumentIdentityStatus.Confirmed;
}

public sealed class DocumentPipeline
{
    private static readonly string[] TopologyVisualHints =
    [
        "pin", "pinout", "wiring", "wire", "connection", "connector", "contact", "terminal", "port",
        "m12", "m8", "rj45", "io-link", "iolink", "rs485", "rs-485", "ethernet", "ethercat",
        "接線", "接线", "腳位", "脚位", "接頭", "接头", "端子", "接口", "連接器", "连接器"
    ];

    private readonly CacheManager _cache;
    private readonly PdfTextExtractor _pdf;
    private readonly PdfTableExtractor _pdfTables = new();
    private readonly PdfPageImageExtractor _pdfImages;
    private readonly PdfFullPageRasterizer _fullPageRasterizer;
    private readonly PdfVectorDiagramExtractor _vectorGeometry;
    private readonly DiagramTextGeometryMatcher _textGeometryMatcher;
    private readonly EngineeringMarkdownBuilder _markdownBuilder;
    private readonly SpecificationParser _parser;
    private readonly OcrCandidateParser _ocrCandidates = new();
    private readonly IOcrTextExtractor _ocr;
    private readonly DocumentIdentityChecker _identityChecker;
    private readonly CrossChannelSpecificationReconciler _reconciler;

    public DocumentPipeline(
        CacheManager cache,
        PdfTextExtractor pdf,
        SpecificationParser parser,
        PdfPageImageExtractor? pdfImages = null,
        IOcrTextExtractor? ocr = null,
        PdfFullPageRasterizer? fullPageRasterizer = null,
        DocumentIdentityChecker? identityChecker = null,
        CrossChannelSpecificationReconciler? reconciler = null,
        PdfVectorDiagramExtractor? vectorGeometry = null,
        DiagramTextGeometryMatcher? textGeometryMatcher = null,
        EngineeringMarkdownBuilder? markdownBuilder = null)
    {
        _cache = cache;
        _pdf = pdf;
        _parser = parser;
        _pdfImages = pdfImages ?? new PdfPageImageExtractor();
        _ocr = ocr ?? TesseractCliOcrTextExtractor.Detect();
        _fullPageRasterizer = fullPageRasterizer ?? new PdfFullPageRasterizer();
        _identityChecker = identityChecker ?? new DocumentIdentityChecker();
        _reconciler = reconciler ?? new CrossChannelSpecificationReconciler();
        _vectorGeometry = vectorGeometry ?? new PdfVectorDiagramExtractor();
        _textGeometryMatcher = textGeometryMatcher ?? new DiagramTextGeometryMatcher();
        _markdownBuilder = markdownBuilder ?? new EngineeringMarkdownBuilder();
    }

    public Task<DocumentExtractionResult> ExtractAsync(
        ComponentDocument document,
        CancellationToken cancellationToken = default) =>
        ExtractCoreAsync(document, DocumentIdentityContext.Current, cancellationToken);

    public Task<DocumentExtractionResult> ExtractAsync(
        ComponentDocument document,
        ComponentIdentity expectedIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedIdentity);
        return ExtractCoreAsync(document, expectedIdentity, cancellationToken);
    }

    private async Task<DocumentExtractionResult> ExtractCoreAsync(
        ComponentDocument document,
        ComponentIdentity? expectedIdentity,
        CancellationToken cancellationToken)
    {
        try
        {
            var diagnostics = new List<string>();
            var cached = await _cache.GetOrDownloadAsync(document.Url, "documents", cancellationToken);
            if (cached is null)
                return new DocumentExtractionResult(document, Array.Empty<RawSpecification>(), false, "Document download failed.", ["DOCUMENT_DOWNLOAD_FAILED"]);

            var isPdf = string.Equals(cached.Metadata.ContentType?.Split(';')[0].Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(cached.Metadata.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                        document.Url.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            var enrichedDocument = document with { LocalPath = cached.Metadata.LocalPath, Sha256 = cached.Metadata.Sha256 };
            if (!isPdf)
                return new DocumentExtractionResult(enrichedDocument, Array.Empty<RawSpecification>(), false, Diagnostics: ["DOCUMENT_NON_PDF_STORED"]);

            // Native PDF vectors are the primary diagram geometry channel. No OpenCV/raster-CV stage exists
            // in the core pipeline: vector lines remain exact PDF evidence rather than reconstructed pixels.
            var geometry = _vectorGeometry.Extract(cached.Metadata.LocalPath);
            diagnostics.Add("PDF_NATIVE_VECTOR_GEOMETRY_PRIMARY");
            diagnostics.AddRange(geometry.Diagnostics);

            // PDF content is first normalized into Engineering Markdown. Downstream text parsing therefore
            // consumes one stable, cacheable document representation instead of reading the PDF ad hoc.
            var pages = _pdf.Extract(cached.Metadata.LocalPath);
            var tableRows = _pdfTables.Extract(cached.Metadata.LocalPath);
            var nativeMarkdown = _markdownBuilder.Build(pages, tableRows);
            diagnostics.AddRange(nativeMarkdown.Diagnostics);

            var identityTexts = pages.Select(page => page.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            var specs = nativeMarkdown.Pages
                .SelectMany(page => _parser.ParseText(
                    page.Markdown,
                    document.Url,
                    page.PageNumber,
                    cached.Metadata.Sha256,
                    document.SourceType))
                .ToList();
            specs.AddRange(_parser.ParseTableRows(tableRows, document.Url, cached.Metadata.Sha256, document.SourceType));

            var nativeCharacters = pages.Sum(page => page.Text.Count(character => !char.IsWhiteSpace(character)));
            var sparseDigitalPdf = nativeCharacters < 80;
            diagnostics.Add(sparseDigitalPdf
                ? $"PDF_NATIVE_TEXT_SPARSE:{nativeCharacters}"
                : $"PDF_NATIVE_TEXT_OK:{nativeCharacters}");

            var embeddedImagePages = new HashSet<int>();
            try
            {
                foreach (var image in _pdfImages.Extract(cached.Metadata.LocalPath))
                    embeddedImagePages.Add(image.PageNumber);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"PDF_EMBEDDED_IMAGE_ENUMERATION_FAILED:{exception.GetType().Name}:{exception.Message}");
            }

            // Rasterization is used only to feed local OCR. Diagram geometry is never inferred from raster
            // pixels by the core pipeline. Candidate pages remain bounded by text/image/topology hints.
            var candidatePages = SelectVisualScanPageNumbers(pages, embeddedImagePages, sparseDigitalPdf);
            diagnostics.Add($"PDF_OCR_CANDIDATE_PAGES:{candidatePages.Count}");

            IReadOnlyList<PdfRasterPage> rasterPages = Array.Empty<PdfRasterPage>();
            if (_ocr.IsAvailable && candidatePages.Count > 0)
            {
                try
                {
                    rasterPages = _fullPageRasterizer.Extract(cached.Metadata.LocalPath, candidatePages);
                    diagnostics.Add($"PDF_OCR_PAGES_RASTERIZED:{rasterPages.Count}");
                }
                catch (Exception exception)
                {
                    diagnostics.Add($"PDF_OCR_RASTER_FAILED:{exception.GetType().Name}:{exception.Message}");
                }
            }

            var ocrRecognizedPages = 0;
            var ocrSpecificationsAdded = 0;
            var ocrBoxesByPage = new Dictionary<int, IReadOnlyList<OcrTextBox>>();
            var ocrTextByPage = new Dictionary<int, string>();
            var visualScanAttempted = rasterPages.Count > 0;

            if (_ocr.IsAvailable)
            {
                diagnostics.Add($"OCR_FULL_PAGE_CANDIDATES:{rasterPages.Count}");
                foreach (var image in rasterPages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ocr = await _ocr.ExtractAsync(image.Bytes, image.Extension, cancellationToken);
                    if (ocr.Diagnostics is not null) diagnostics.AddRange(ocr.Diagnostics);
                    if (!ocr.Succeeded || string.IsNullOrWhiteSpace(ocr.Text))
                    {
                        diagnostics.Add($"OCR_FULL_PAGE_FAILED:{image.PageNumber}:{ocr.Error ?? "UNKNOWN"}");
                        continue;
                    }

                    ocrRecognizedPages++;
                    identityTexts.Add(ocr.Text);
                    ocrTextByPage[image.PageNumber] = ocr.Text;
                    if (ocr.Boxes is { Count: > 0 })
                        ocrBoxesByPage[image.PageNumber] = ocr.Boxes;

                    // OCR text also enters through the same Markdown representation before engineering parsing.
                    var ocrMarkdown = _markdownBuilder.BuildOcrPageMarkdown(image.PageNumber, ocr.Text);
                    var before = specs.Count;
                    specs.AddRange(_parser
                        .ParseText(
                            ocrMarkdown,
                            document.Url,
                            image.PageNumber,
                            cached.Metadata.Sha256,
                            document.SourceType,
                            ExtractionMethod.OcrText)
                        .Select(MarkOcrAsInferred));
                    specs.AddRange(_ocrCandidates.Parse(
                        ocr.Text,
                        document.Url,
                        image.PageNumber,
                        cached.Metadata.Sha256,
                        document.SourceType));
                    ocrSpecificationsAdded += specs.Count - before;
                    diagnostics.Add($"OCR_FULL_PAGE_RECOGNIZED:{image.PageNumber}:{image.WidthPixels}x{image.HeightPixels}:{ocr.Engine}");
                }
            }
            else if (candidatePages.Count > 0)
            {
                diagnostics.Add("LOCAL_OCR_ENGINE_NOT_AVAILABLE");
            }

            var identityStatus = DocumentIdentityStatus.NotChecked;
            if (expectedIdentity is not null)
            {
                var identityCheck = _identityChecker.Check(expectedIdentity, enrichedDocument, identityTexts);
                identityStatus = identityCheck.Status;
                diagnostics.AddRange(identityCheck.Diagnostics);
                if (!identityCheck.IsAccepted)
                {
                    diagnostics.Add("DOCUMENT_ENGINEERING_EVIDENCE_REJECTED_IDENTITY_GATE");
                    var rejectedDocument = enrichedDocument with
                    {
                        Type = "identity-rejected",
                        SourceType = ComponentSourceType.GenericWeb
                    };
                    return new DocumentExtractionResult(
                        rejectedDocument,
                        Array.Empty<RawSpecification>(),
                        false,
                        Diagnostics: diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        IdentityStatus: identityStatus,
                        Geometry: null,
                        GeometryLabelMatches: null,
                        EngineeringMarkdownPath: null);
                }
            }

            // OCR boxes are allowed to anchor only against exact native-PDF vector geometry. This produces
            // candidate relationships, never electrical semantics, and avoids any raster line detector.
            var geometryLabelMatches = new List<DiagramLabelMatch>();
            foreach (var pair in ocrBoxesByPage)
            {
                var geometryPage = geometry.Pages.FirstOrDefault(page => page.PageNumber == pair.Key);
                if (geometryPage is null) continue;
                var matches = _textGeometryMatcher.Match(pair.Key, pair.Value, geometryPage);
                geometryLabelMatches.AddRange(matches);
                diagnostics.Add($"OCR_NATIVE_VECTOR_LABEL_MATCHES:{pair.Key}:{matches.Count}");
            }

            var reconciled = _reconciler.Reconcile(specs);
            diagnostics.AddRange(reconciled.Diagnostics);
            var deduped = reconciled.Specifications
                .GroupBy(spec => $"{spec.ProposedKey}\u001f{spec.RawName}\u001f{spec.RawValue}\u001f{spec.Evidence.FirstOrDefault()?.PageNumber}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First() with { Evidence = group.SelectMany(item => item.Evidence).Distinct().ToArray() })
                .ToArray();

            if (ocrRecognizedPages > 0 && ocrSpecificationsAdded == 0)
                diagnostics.Add("OCR_TEXT_RECOGNIZED_NO_ENGINEERING_FIELDS");

            var finalMarkdown = _markdownBuilder.Build(pages, tableRows, ocrTextByPage);
            diagnostics.AddRange(finalMarkdown.Diagnostics);
            var markdownPath = TryPersistEngineeringMarkdown(cached.Metadata.LocalPath, finalMarkdown.Markdown, diagnostics);

            // Optional local vision/AI remains a future last fallback only for sparse image-like PDFs whose
            // native vectors and OCR still cannot produce useful engineering fields.
            var needsAi = visualScanAttempted &&
                          sparseDigitalPdf &&
                          ocrSpecificationsAdded == 0 &&
                          !geometry.HasGeometry;
            return new DocumentExtractionResult(
                enrichedDocument,
                deduped,
                needsAi,
                Diagnostics: diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                IdentityStatus: identityStatus,
                Geometry: geometry,
                GeometryLabelMatches: geometryLabelMatches,
                EngineeringMarkdownPath: markdownPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DocumentExtractionResult(document, Array.Empty<RawSpecification>(), false, $"{exception.GetType().Name}: {exception.Message}", ["DOCUMENT_PIPELINE_EXCEPTION"]);
        }
    }

    internal static IReadOnlySet<int> SelectVisualScanPageNumbers(
        IReadOnlyList<PdfPageText> pages,
        IReadOnlySet<int> embeddedImagePages,
        bool sparseDigitalPdf)
    {
        var candidatePages = pages
            .Where(page => ShouldVisuallyScan(page, embeddedImagePages))
            .Select(page => page.PageNumber)
            .ToHashSet();

        if (sparseDigitalPdf)
            foreach (var page in pages) candidatePages.Add(page.PageNumber);

        return candidatePages;
    }

    private static bool ShouldVisuallyScan(PdfPageText page, IReadOnlySet<int> embeddedImagePages)
    {
        var significantCharacters = page.Text.Count(character => !char.IsWhiteSpace(character));
        if (significantCharacters < 350) return true;
        if (embeddedImagePages.Contains(page.PageNumber)) return true;
        return ContainsTopologyVisualHint(page.Text);
    }

    private static bool ContainsTopologyVisualHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return TopologyVisualHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryPersistEngineeringMarkdown(string pdfPath, string markdown, ICollection<string> diagnostics)
    {
        try
        {
            var path = pdfPath + ".engineering.md";
            File.WriteAllText(path, markdown);
            diagnostics.Add($"ENGINEERING_MARKDOWN_WRITTEN:{Path.GetFileName(path)}");
            return path;
        }
        catch (Exception exception)
        {
            diagnostics.Add($"ENGINEERING_MARKDOWN_WRITE_FAILED:{exception.GetType().Name}:{exception.Message}");
            return null;
        }
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
}
