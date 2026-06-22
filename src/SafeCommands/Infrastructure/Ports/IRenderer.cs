namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Port for emitting output to the user. Real adapter writes to Console / Spectre / JSON;
/// tests use FakeRenderer to accumulate emissions.
/// </summary>
interface IRenderer
{
    /// <summary>True when the user passed --json; affects Result and Blocked routing.</summary>
    bool JsonMode { get; }

    /// <summary>
    /// Standard success / passthrough rendering for an external process result.
    /// Under JsonMode emits {exitCode, output, error}; otherwise streams stdout/stderr.
    /// </summary>
    void Result(ExecResult r);

    /// <summary>
    /// Custom-shape JSON payload. No-op when not in JsonMode (callers that want both
    /// modes should branch on JsonMode and call Result/Passthrough as appropriate).
    /// </summary>
    void Json(object payload);

    /// <summary>
    /// Policy rejection. Under JsonMode emits {blocked, command, reason, suggestion};
    /// otherwise emits Spectre markup.
    /// </summary>
    void Blocked(string command, string reason, string? suggestion);

    /// <summary>Informational message. Suppressed under JsonMode to avoid corrupting JSON output.</summary>
    void Info(string message);

    /// <summary>
    /// Verbatim stdout write — no added newline, no markup, no escaping. The faithful primitive
    /// for commands whose contract is byte-exact content passthrough (e.g. <c>file read</c> in
    /// human mode). Unlike <see cref="Info"/> it adds nothing and is NOT suppressed under JsonMode,
    /// so callers that have a JSON shape must branch on <see cref="JsonMode"/> and not call Raw there.
    /// </summary>
    void Raw(string text);

    /// <summary>Warning. Suppressed under JsonMode to avoid corrupting JSON output.</summary>
    void Warning(string message);

    /// <summary>Error message. Always emitted (to stderr under JsonMode as plain text).</summary>
    void Error(string message);
}
