using System.Security.Cryptography;
using System.Text;

namespace SafeCommands.Commands.Generate;

/// <summary>
/// Pure UUID construction. v4 is just <see cref="System.Guid.NewGuid"/> (no logic worth hiding);
/// the deepenable parts are the deterministic name-based (v3/v5) and the timestamp+random v7,
/// both of which take their entropy/clock as arguments so they are exactly reproducible in tests.
/// </summary>
static class Uuid
{
    private static readonly Dictionary<string, Guid> WellKnownNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dns"]  = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8"),
        ["url"]  = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8"),
        ["oid"]  = Guid.Parse("6ba7b812-9dad-11d1-80b4-00c04fd430c8"),
        ["x500"] = Guid.Parse("6ba7b814-9dad-11d1-80b4-00c04fd430c8"),
    };

    /// <summary>Resolve a namespace token: a well-known name (dns/url/oid/x500) or any parseable
    /// GUID. Returns null when the token is neither.</summary>
    public static Guid? ResolveNamespace(string token)
        => WellKnownNamespaces.TryGetValue(token, out var known) ? known
         : Guid.TryParse(token, out var parsed) ? parsed
         : null;

    public static string Format(Guid uuid, bool upper)
        => upper ? uuid.ToString("D").ToUpperInvariant() : uuid.ToString("D");

    /// <summary>Create a UUID v3 (MD5) or v5 (SHA1) from a namespace GUID and a name (RFC 4122).</summary>
    public static Guid NameBased(Guid namespaceId, string name, int version)
    {
        // Convert namespace GUID to big-endian bytes per RFC 4122
        var nsBytes = namespaceId.ToByteArray();
        SwapGuidEndianness(nsBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, input, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, nsBytes.Length, nameBytes.Length);

        var hash = version == 3 ? MD5.HashData(input) : SHA1.HashData(input);

        // Take first 16 bytes, set version and variant
        var result = new byte[16];
        Array.Copy(hash, result, 16);
        result[6] = (byte)((result[6] & 0x0F) | (version << 4)); // version
        result[8] = (byte)((result[8] & 0x3F) | 0x80);           // variant 10xx

        SwapGuidEndianness(result); // back to .NET mixed-endian
        return new Guid(result);
    }

    /// <summary>Create a UUID v7 (48-bit Unix-ms timestamp + random). <paramref name="random"/> must
    /// be at least 16 bytes; its first 6 are overwritten by the timestamp, leaving 10 random (two of
    /// whose nibbles are then fixed to the version/variant).</summary>
    public static Guid V7(long unixMs, ReadOnlySpan<byte> random)
    {
        var bytes = new byte[16];
        random[..16].CopyTo(bytes);

        // First 6 bytes = 48-bit timestamp (big-endian)
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70); // version 0111
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant 10xx

        SwapGuidEndianness(bytes); // big-endian (RFC) -> .NET mixed-endian
        return new Guid(bytes);
    }

    /// <summary>Swap between .NET's mixed-endian GUID layout and RFC 4122 big-endian.</summary>
    private static void SwapGuidEndianness(byte[] b)
    {
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }
}
