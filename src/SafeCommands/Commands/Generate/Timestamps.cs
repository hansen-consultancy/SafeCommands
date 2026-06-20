namespace SafeCommands.Commands.Generate;

/// <summary>Pure timestamp formatting. The instant is passed in (the handler reads the clock), so
/// every format is reproducible in tests.</summary>
static class Timestamps
{
    /// <summary>ISO 8601 / RFC 3339 with millisecond precision, UTC ("...Z").</summary>
    public static string Iso8601(DateTimeOffset now) => now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    public static long Unix(DateTimeOffset now) => now.ToUnixTimeSeconds();

    public static long UnixMs(DateTimeOffset now) => now.ToUnixTimeMilliseconds();
}
