using System.Security.Cryptography;

namespace ComponentIntelligence.Repository;

/// <summary>
/// Copies central-workbook product images into a stable application cache. This is a display-only
/// asset cache and never changes component engineering facts or workbook data.
/// </summary>
public sealed class ComponentImageFileCache
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private readonly string _root;

    public ComponentImageFileCache(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ComponentIntelligence",
            "component-images");
        Directory.CreateDirectory(_root);
    }

    public async Task<Uri?> CacheAsync(Uri? source, CancellationToken cancellationToken = default)
    {
        if (source is null) return null;
        if (!source.IsFile) return source;

        var sourcePath = source.LocalPath;
        if (!File.Exists(sourcePath)) return null;
        var info = new FileInfo(sourcePath);
        if (info.Length is <= 0 or > MaximumBytes) return null;
        var extension = SupportedExtension(Path.GetExtension(sourcePath));
        if (extension is null) return null;

        string digest;
        await using (var input = new FileStream(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete,
                         81920,
                         useAsync: true))
        {
            digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
                .ToLowerInvariant();
        }

        var destination = Path.Combine(_root, digest + extension);
        if (!File.Exists(destination))
        {
            var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var input = new FileStream(
                                 sourcePath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.ReadWrite | FileShare.Delete,
                                 81920,
                                 useAsync: true))
                await using (var output = new FileStream(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 useAsync: true))
                {
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }
                File.Move(temporary, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another view or sync operation completed the same content-addressed copy.
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        return File.Exists(destination) && new FileInfo(destination).Length > 0
            ? new Uri(destination, UriKind.Absolute)
            : null;
    }

    private static string? SupportedExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => extension.ToLowerInvariant(),
        _ => null
    };
}
