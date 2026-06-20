using System.Security.Cryptography;
using System.Text;

namespace SafeCommands.Commands.Generate;

/// <summary>
/// Pure content hashing. One algorithm switch (previously duplicated between the <c>hash</c> and
/// <c>hash-file</c> handlers) lives here. Returns lowercase hex, or null for an unknown algorithm —
/// the caller turns null into a usage error, so "unknown algorithm" never throws.
/// </summary>
static class Hashing
{
    public static readonly string[] Supported = ["sha256", "sha384", "sha512", "md5"];

    public static string? HashText(string input, string algorithm)
        => Hash(new MemoryStream(Encoding.UTF8.GetBytes(input)), algorithm);

    public static string? Hash(Stream input, string algorithm)
    {
        byte[] digest;
        switch (algorithm.ToLowerInvariant())
        {
            case "sha256": digest = SHA256.HashData(input); break;
            case "sha384": digest = SHA384.HashData(input); break;
            case "sha512": digest = SHA512.HashData(input); break;
            case "md5":    digest = MD5.HashData(input); break;
            default: return null;
        }
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
