using UglyToad.PdfPig;

namespace ComponentIntelligence.Extraction;

public sealed record PdfPageImage(
    int PageNumber,
    int ImageIndex,
    byte[] Bytes,
    string Extension,
    int WidthInSamples,
    int HeightInSamples);

/// <summary>
/// Extracts the dominant raster image from each PDF page so image-only/scanned datasheets can be
/// offered to a local OCR engine without rasterizing every vector PDF page.
/// This deliberately favors large page-sized images and ignores icons/logos.
/// </summary>
public sealed class PdfPageImageExtractor
{
    private readonly int _minimumDimension;
    private readonly int _maxImagesPerPage;

    public PdfPageImageExtractor(int minimumDimension = 500, int maxImagesPerPage = 1)
    {
        _minimumDimension = Math.Max(100, minimumDimension);
        _maxImagesPerPage = Math.Clamp(maxImagesPerPage, 1, 4);
    }

    public IReadOnlyList<PdfPageImage> Extract(string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        var results = new List<PdfPageImage>();

        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            var images = page.GetImages()
                .Select((image, index) => new { Image = image, Index = index })
                .Where(item => item.Image.WidthInSamples >= _minimumDimension || item.Image.HeightInSamples >= _minimumDimension)
                .OrderByDescending(item => (long)item.Image.WidthInSamples * item.Image.HeightInSamples)
                .Take(_maxImagesPerPage)
                .ToArray();

            foreach (var item in images)
            {
                if (item.Image.TryGetPng(out var png) && png is { Length: > 0 })
                {
                    results.Add(new PdfPageImage(
                        page.Number,
                        item.Index,
                        png,
                        ".png",
                        item.Image.WidthInSamples,
                        item.Image.HeightInSamples));
                    continue;
                }

                var raw = item.Image.RawMemory.ToArray();
                if (LooksLikeJpeg(raw))
                {
                    results.Add(new PdfPageImage(
                        page.Number,
                        item.Index,
                        raw,
                        ".jpg",
                        item.Image.WidthInSamples,
                        item.Image.HeightInSamples));
                }
            }
        }

        return results;
    }

    private static bool LooksLikeJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
