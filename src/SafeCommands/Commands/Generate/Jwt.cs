namespace SafeCommands.Commands.Generate;

/// <summary>Pure JWT decoding (no signature verification) — splits the token and base64url-decodes
/// the header and payload segments.</summary>
static class Jwt
{
    public enum Error { None, TooFewSegments, BadBase64 }

    /// <summary>Decode a JWT's header and payload JSON. On failure <c>error</c> says why and the
    /// strings are null.</summary>
    public static (string? Header, string? Payload, Error Error) Decode(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
            return (null, null, Error.TooFewSegments);

        try
        {
            return (Codec.Base64UrlDecode(parts[0]), Codec.Base64UrlDecode(parts[1]), Error.None);
        }
        catch (Exception)
        {
            return (null, null, Error.BadBase64);
        }
    }
}
