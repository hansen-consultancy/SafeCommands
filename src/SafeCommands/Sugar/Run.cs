using SafeCommands.Infrastructure.Ports;
using SafeCommands.Safety;

namespace SafeCommands.Sugar;

/// <summary>
/// Common-case helpers for command handlers. Each tool has a thin facade that:
///   1. evaluates an optional <see cref="Policy"/> against the user's args,
///   2. emits a structured <c>Blocked</c> envelope and returns 1 on rejection,
///   3. otherwise spawns the tool and renders the standard Result envelope.
/// Outliers (multi-step probes, custom JSON shapes) drop down to <see cref="IExecutor"/>
/// and <see cref="IRenderer"/> directly.
/// </summary>
static class Run
{
    /// <summary>Bare execution + rendering. No policy. Args are passed verbatim to the tool.</summary>
    public static int Tool(Ports p, string tool, string[] args)
    {
        var r = p.Exec.Run(tool, args);
        p.Render.Result(r);
        return r.ExitCode;
    }

    /// <summary>
    /// <c>bun &lt;sub&gt; &lt;args&gt;</c>. Policy is evaluated against the handler's
    /// user args (i.e. <paramref name="args"/>), not the prepended subcommand.
    /// </summary>
    public static int Bun(Ports p, string sub, string[] args, Policy? policy = null)
    {
        if (policy is not null && policy.Evaluate(args) is PolicyResult.Block b)
        {
            p.Render.Blocked(
                command: $"bun {sub} {string.Join(' ', args)}".TrimEnd(),
                reason: b.Reason,
                suggestion: b.Suggestion);
            return 1;
        }
        return Tool(p, "bun", [sub, .. args]);
    }
}
