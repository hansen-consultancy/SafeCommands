using System.Text.Json;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests;

/// <summary>
/// Lock the JSON envelope shape that --json consumers rely on.
/// Asserts the migrated success envelope and the new (Q4) Blocked envelope.
/// </summary>
public class RendererEnvelopeTests
{
    private static (ConsoleRenderer renderer, StringWriter stdout, StringWriter stderr) Make(bool jsonMode)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        return (new ConsoleRenderer(jsonMode, stdout, stderr), stdout, stderr);
    }

    [Fact]
    public void Result_UnderJsonMode_EmitsExitCodeOutputErrorEnvelope()
    {
        var (r, stdout, _) = Make(jsonMode: true);

        r.Result(new ExecResult(0, "ok", ""));

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("ok", doc.RootElement.GetProperty("output").GetString());
        // 'error' is omitted when null/empty (DefaultIgnoreCondition.WhenWritingNull),
        // but empty string is still serialized — assert it's empty.
        Assert.Equal("", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Result_UnderHumanMode_PassesStdoutThroughWriter()
    {
        var (r, stdout, stderr) = Make(jsonMode: false);

        r.Result(new ExecResult(0, "the output", "the error"));

        Assert.Contains("the output", stdout.ToString());
        Assert.Contains("the error", stderr.ToString());
    }

    [Fact]
    public void Result_UnderHumanMode_DoesNotEmitEmptyLines()
    {
        var (r, stdout, stderr) = Make(jsonMode: false);

        r.Result(new ExecResult(0, "", ""));

        Assert.Equal("", stdout.ToString());
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Blocked_UnderJsonMode_EmitsStructuredEnvelope()
    {
        // Q4 contract: blocked commands emit JSON when --json is passed.
        var (r, stdout, _) = Make(jsonMode: true);

        r.Blocked("git push --force", "Force push is not allowed", "Use --force-with-lease");

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal("git push --force", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("Force push is not allowed", doc.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Use --force-with-lease", doc.RootElement.GetProperty("suggestion").GetString());
    }

    [Fact]
    public void Blocked_UnderJsonMode_OmitsNullSuggestion()
    {
        var (r, stdout, _) = Make(jsonMode: true);

        r.Blocked("some cmd", "some reason", suggestion: null);

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("suggestion", out _));
    }

    [Fact]
    public void Json_UnderJsonMode_EmitsCustomPayload()
    {
        var (r, stdout, _) = Make(jsonMode: true);

        r.Json(new { branch = "main", clean = true });

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("main", doc.RootElement.GetProperty("branch").GetString());
        Assert.True(doc.RootElement.GetProperty("clean").GetBoolean());
    }

    [Fact]
    public void Json_UnderHumanMode_IsNoOp()
    {
        // Custom JSON payloads must not corrupt human output. Handlers that want both
        // human and JSON modes branch explicitly on JsonMode.
        var (r, stdout, _) = Make(jsonMode: false);

        r.Json(new { foo = "bar" });

        Assert.Equal("", stdout.ToString());
    }

    [Fact]
    public void Error_UnderJsonMode_GoesToStderrAsPlainText()
    {
        var (r, stdout, stderr) = Make(jsonMode: true);

        r.Error("boom");

        Assert.Equal("", stdout.ToString());
        Assert.Contains("boom", stderr.ToString());
    }

    [Fact]
    public void Info_UnderJsonMode_IsSuppressed()
    {
        var (r, stdout, stderr) = Make(jsonMode: true);

        r.Info("just FYI");

        Assert.Equal("", stdout.ToString());
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Warning_UnderJsonMode_IsSuppressed()
    {
        var (r, stdout, stderr) = Make(jsonMode: true);

        r.Warning("watch out");

        Assert.Equal("", stdout.ToString());
        Assert.Equal("", stderr.ToString());
    }

    // ---- Human-mode routing (Spectre AnsiConsole bound to injected writers) ----

    [Fact]
    public void Error_UnderHumanMode_GoesToStderr_NotStdout()
    {
        // CLI convention: errors must reach stderr even in human mode so piping stdout
        // to a file doesn't swallow the diagnostic.
        var (r, stdout, stderr) = Make(jsonMode: false);

        r.Error("something failed");

        Assert.Equal("", stdout.ToString());
        Assert.Contains("something failed", stderr.ToString());
    }

    [Fact]
    public void Warning_UnderHumanMode_GoesToInjectedStdout()
    {
        // Verifies the AnsiConsole.Create(...) path is wired to the injected writer
        // rather than the global static AnsiConsole.
        var (r, stdout, _) = Make(jsonMode: false);

        r.Warning("be careful");

        Assert.Contains("be careful", stdout.ToString());
    }

    [Fact]
    public void Blocked_UnderHumanMode_GoesToInjectedStdout()
    {
        var (r, stdout, stderr) = Make(jsonMode: false);

        r.Blocked("bun run nope", "not allowed", "try X");

        Assert.Contains("Blocked", stdout.ToString());
        Assert.Contains("bun run nope", stdout.ToString());
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Info_UnderHumanMode_GoesToInjectedStdout()
    {
        var (r, stdout, _) = Make(jsonMode: false);

        r.Info("hint");

        Assert.Contains("hint", stdout.ToString());
    }

    // ---- Raw: byte-faithful content passthrough (the `file read` contract) ----

    [Fact]
    public void Raw_WritesVerbatim_AddsNoTrailingNewline()
    {
        // The whole reason Raw exists: unlike Info/Result it must NOT append a newline, so
        // `file read` reproduces file bytes exactly. A regression to _stdout.WriteLine would
        // be invisible to the FakeRenderer (which stores the string), so it is pinned here.
        var (r, stdout, stderr) = Make(jsonMode: false);

        r.Raw("ab");

        Assert.Equal("ab", stdout.ToString()); // exact — no '\n' appended
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Raw_UnderJsonMode_StillWritesVerbatim_NotSuppressed()
    {
        // Unlike Info/Warning (suppressed under JsonMode), Raw is unconditional: suppressing it
        // would silently swallow content. Callers with a JSON shape branch on JsonMode and simply
        // don't call Raw there; the primitive itself always emits.
        var (r, stdout, _) = Make(jsonMode: true);

        r.Raw("xy");

        Assert.Equal("xy", stdout.ToString());
    }
}
