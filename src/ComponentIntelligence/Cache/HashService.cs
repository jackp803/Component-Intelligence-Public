using System.Security.Cryptography;
using System.Text;

namespace ComponentIntelligence.Cache;

public static class HashService
{
    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));

    public static async Task<string> Sha256FileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
            .ToLowerInvariant();
    }
}
