using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Accumulating fake <see cref="IRenderer"/>. Tests assert on the captured lists
/// rather than parsing console output.
/// </summary>
sealed class FakeRenderer : IRenderer
{
    public bool JsonMode { get; set; }

    public List<ExecResult> Results { get; } = [];
    public List<object> JsonPayloads { get; } = [];
    public List<BlockedRecord> Blocks { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Raws { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public void Result(ExecResult r) => Results.Add(r);
    public void Json(object payload) => JsonPayloads.Add(payload);
    public void Blocked(string command, string reason, string? suggestion)
        => Blocks.Add(new(command, reason, suggestion));
    public void Info(string message) => Infos.Add(message);
    public void Raw(string text) => Raws.Add(text);
    public void Warning(string message) => Warnings.Add(message);
    public void Error(string message) => Errors.Add(message);

    public sealed record BlockedRecord(string Command, string Reason, string? Suggestion);
}
