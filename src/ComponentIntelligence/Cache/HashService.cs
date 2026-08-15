using System.Security.Cryptography;
using System.Text;

namespace ComponentIntelligence.Cache;

public static class HashService
{
    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));
}
