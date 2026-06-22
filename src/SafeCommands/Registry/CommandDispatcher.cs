using SafeCommands.Infrastructure.Ports;
using SafeCommands.Safety;

namespace SafeCommands.Registry;

/// <summary>
/// Single enforcement seam: evaluates a command's <see cref="Policy"/> before invoking its
/// handler. A blocked decision renders the uniform Blocked envelope and never spawns the tool;
/// a rewrite passes the (possibly trimmed) safe args to the handler.
/// </summary>
static class CommandDispatcher
{
    public static int Execute(CommandDefinition cmd, Ports ports, string group, string command, string[] args)
    {
        var label = $"{group} {command} {string.Join(' ', args)}".TrimEnd();
        var ctx = new SafetyContext(label, ports.Repo, ports.Workspace);
        var decision = cmd.Policy.Evaluate(args, ctx);
        if (decision.IsBlocked)
        {
            ports.Render.Blocked(label, decision.Block!.Reason, decision.Block.Suggestion);
            return 1;
        }

        var safeArgs = decision.SafeArgs!;
        if (safeArgs.Length < cmd.MinArgs)
        {
            ports.Render.Error($"Usage: {cmd.Usage}");
            return 1;
        }
        return cmd.Handler(ports, safeArgs);
    }
}
