using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Sugar;

/// <summary>
/// Common-case helpers for command handlers. Each tool has a thin facade that spawns the tool
/// and renders the standard Result envelope. Safety is enforced centrally at dispatch.
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

    /// <summary><c>bun &lt;sub&gt; &lt;args&gt;</c>. Safety is enforced centrally at dispatch.</summary>
    public static int Bun(Ports p, string sub, string[] args) => Tool(p, "bun", [sub, .. args]);
}
