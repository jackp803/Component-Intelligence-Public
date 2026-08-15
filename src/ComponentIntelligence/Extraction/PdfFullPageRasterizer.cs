using System.Runtime.Versioning;
using PDFtoImage;
using SkiaSharp;

namespace ComponentIntelligence.Extraction;

public sealed record PdfRasterPage(
    int PageNumber,
    byte[] Bytes,
    string Extension,
    int WidthPixels,
    int HeightPixels);

/// <summary>
/// Renders the complete visual appearance of selected PDF pages, including vector graphics, text,
/// paths and embedded images. This complements PdfPig image extraction: a wiring/pinout drawing made
/// of PDF vector paths is still visible here even when the PDF contains no large raster image object.
/// </summary>
public sealed class PdfFullPageRasterizer
{
    private readonly int _dpi;
    private readonly int _maxPages;

    public PdfFullPageRasterizer(int dpi = 300, int maxPages = 60)
    {
        _dpi = Math.Clamp(dpi, 150, 600);
        _maxPages = Math.Clamp(maxPages, 1, 200);
    }

    [SupportedOSPlatformGuard("windows")]
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("browser")]
    [SupportedOSPlatformGuard("android31.0")]
    [SupportedOSPlatformGuard("ios13.6")]
    [SupportedOSPlatformGuard("maccatalyst13.5")]
    internal static bool IsSupportedPlatform =>
        OperatingSystem.IsWindows() ||
        OperatingSystem.IsLinux() ||
        OperatingSystem.IsMacOS() ||
        OperatingSystem.IsBrowser() ||
        OperatingSystem.IsAndroidVersionAtLeast(31) ||
        OperatingSystem.IsIOSVersionAtLeast(13, 6) ||
        OperatingSystem.IsMacCatalystVersionAtLeast(13, 5);

    public IReadOnlyList<PdfRasterPage> Extract(string pdfPath, IReadOnlySet<int>? pageNumbers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        if (!IsSupportedPlatform)
            throw new PlatformNotSupportedException("Full-page PDF rasterization is not supported on this operating system by PDFtoImage.");

        var results = new List<PdfRasterPage>();
        using var stream = File.OpenRead(pdfPath);
        var availablePageCount = Conversion.GetPageCount(stream, leaveOpen: true);
        var pageIndexes = pageNumbers is null
            ? Enumerable.Range(0, Math.Min(availablePageCount, _maxPages)).ToArray()
            : pageNumbers
                .Where(pageNumber => pageNumber >= 1 && pageNumber <= availablePageCount)
                .Distinct()
                .OrderBy(pageNumber => pageNumber)
                .Take(_maxPages)
                .Select(pageNumber => pageNumber - 1)
                .ToArray();
        if (pageIndexes.Length == 0) return results;
        stream.Position = 0;

        var options = new RenderOptions(
            Dpi: _dpi,
            WithAnnotations: false,
            WithFormFill: false,
            UseTiling: true,
            Grayscale: false);

        var resultIndex = 0;
        foreach (var bitmap in Conversion.ToImages(stream, pageIndexes, leaveOpen: true, options: options))
        {
            using (bitmap)
            using (var image = SKImage.FromBitmap(bitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                results.Add(new PdfRasterPage(
                    pageIndexes[resultIndex] + 1,
                    encoded.ToArray(),
                    ".png",
                    bitmap.Width,
                    bitmap.Height));
            }
            resultIndex++;
        }

        return results;
    }
}
