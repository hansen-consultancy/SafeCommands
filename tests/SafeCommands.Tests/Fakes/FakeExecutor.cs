using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Recording fake <see cref="IExecutor"/>. Captures every Run invocation and returns
/// a configurable canned <see cref="ExecResult"/>. Defaults to (ExitCode=0, "", "").
/// </summary>
sealed class FakeExecutor : IExecutor
{
    public List<(string Tool, string[] Args)> Calls { get; } = [];
    public ExecResult NextResult { get; set; } = new(0, "", "");

    public ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null)
    {
        Calls.Add((tool, args.ToArray()));
        return NextResult;
    }
}
