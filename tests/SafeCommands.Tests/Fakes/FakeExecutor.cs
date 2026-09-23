using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Recording fake <see cref="IExecutor"/>. Captures every Run invocation and returns
/// a configurable canned <see cref="ExecResult"/>. Defaults to (ExitCode=0, "", "").
/// Multi-step handlers set <see cref="Respond"/> to answer per call; a null answer falls back to NextResult.
/// </summary>
sealed class FakeExecutor : IExecutor
{
    public List<(string Tool, string[] Args)> Calls { get; } = [];
    public ExecResult NextResult { get; set; } = new(0, "", "");
    public Func<string[], ExecResult?>? Respond { get; set; }

    public ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null)
    {
        var a = args.ToArray();
        Calls.Add((tool, a));
        return Respond?.Invoke(a) ?? NextResult;
    }
}
