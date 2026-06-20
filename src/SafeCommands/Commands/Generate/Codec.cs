using System.Text;

namespace SafeCommands.Commands.Generate;

/// <summary>Pure string codecs: base64, base64url, and URL percent-encoding.</summary>
static class Codec
{
    public static string Base64Encode(string input)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(input));

    /// <summary>Decode standard base64 to a UTF-8 string. Returns null on malformed input,
    /// so the caller never has to catch <see cref="FormatException"/>.</summary>
    public static string? Base64Decode(string input)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(input)); }
        catch (FormatException) { return null; }
    }

    public static string UrlEncode(string input) => Uri.EscapeDataString(input);

    public static string UrlDecode(string input) => Uri.UnescapeDataString(input);

    /// <summary>Decode a base64url segment (JWT-style: '-'/'_' alphabet, padding optional) to a
    /// UTF-8 string. Throws <see cref="FormatException"/> on malformed input.</summary>
    public static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
