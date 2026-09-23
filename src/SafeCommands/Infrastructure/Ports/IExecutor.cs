namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Port for invoking external processes. Real adapter wraps ProcessRunner; tests use FakeExecutor.
/// </summary>
interface IExecutor
{
    ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null);
}

readonly record struct ExecResult(int ExitCode, string StdOut, string StdErr)
{
    /// <summary>Non-empty stdout lines; tolerates both CRLF and LF line endings.</summary>
    public string[] StdOutLines => StdOut.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
}

readonly record struct ExecOptions(string? Cwd = null);
