namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Port for invoking external processes. Real adapter wraps ProcessRunner; tests use FakeExecutor.
/// </summary>
interface IExecutor
{
    ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null);
}

readonly record struct ExecResult(int ExitCode, string StdOut, string StdErr);

readonly record struct ExecOptions(string? Cwd = null);
