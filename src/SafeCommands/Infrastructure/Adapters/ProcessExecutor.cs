using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IExecutor"/> adapter — delegates to <see cref="ProcessRunner.Run"/>.
/// Behavioural no-op around the existing implementation; exists purely to introduce a seam.
/// </summary>
sealed class ProcessExecutor : IExecutor
{
    public ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null)
    {
        var (code, output, error) = ProcessRunner.Run(
            tool,
            args is string[] arr ? arr : args.ToArray(),
            workingDir: opts?.Cwd);
        return new ExecResult(code, output, error);
    }
}
