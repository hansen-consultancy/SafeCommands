using SafeCommands.Infrastructure;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

/// <summary>
/// Proxy commands for tools not in the main groups.
/// Each entry defines exactly which subcommands/flags are allowed; those entries are compiled
/// into a per-tool <see cref="Policy"/> (an <c>AllowSubcommands</c> chain) enforced centrally at
/// dispatch, so the handlers themselves do no subcommand/flag matching.
/// </summary>
static class ProxyCommands
{
    // curl write-method flags: blocked up front so the message stays curl-specific rather than
    // surfacing as a generic "flag not allowed under subcommand".
    private static readonly string[] CurlWriteFlags =
        ["-X", "--request", "-d", "--data", "--data-raw", "--data-binary", "-F", "--form", "--upload-file", "-T"];

    // Allowed proxy patterns: tool -> allowed subcommand prefixes
    private static readonly Dictionary<string, AllowedProxy[]> AllowedProxies = new(StringComparer.OrdinalIgnoreCase)
    {
        // GitHub CLI (gh)
        ["gh"] =
        [
            new("pr list", ["--state", "--base", "--head", "--label", "--author", "--limit", "--json", "--search"]),
            new("pr view", ["--json", "--web"]),
            new("pr status", []),
            new("pr checks", []),
            new("pr diff", []),
            new("issue list", ["--state", "--label", "--author", "--limit", "--json", "--search", "--assignee"]),
            new("issue view", ["--json", "--web"]),
            new("issue status", []),
            new("repo view", ["--json", "--web"]),
            new("repo list", ["--json", "--limit", "--language"]),
            new("run list", ["--limit", "--json", "--workflow", "--branch"]),
            new("run view", ["--json", "--log"]),
            // -f/-F send fields, -q/--jq filter output, -H headers, --paginate/--cache for reads.
            // -X/--method deliberately omitted: gh api auto-selects POST when fields are present and
            // GET otherwise, so reads and resource creation work while DELETE/PUT/PATCH stay blocked.
            new("api", ["-f", "--raw-field", "-F", "--field", "-q", "--jq", "--paginate", "-H", "--header", "--cache"]),
            new("auth status", []),
        ],

        // Azure CLI
        ["az"] =
        [
            new("account show", []),
            new("account list", []),
            new("group list", ["--output"]),
            new("resource list", ["--resource-group", "--output"]),
            new("webapp list", ["--resource-group", "--output"]),
            new("functionapp list", ["--resource-group", "--output"]),
            new("storage account list", ["--resource-group", "--output"]),
            new("aks list", ["--resource-group", "--output"]),
            new("acr list", ["--resource-group", "--output"]),
            new("keyvault list", ["--resource-group", "--output"]),
            new("monitor log-analytics workspace list", ["--resource-group", "--output"]),
        ],

        // kubectl
        ["kubectl"] =
        [
            new("get", ["--namespace", "-n", "--output", "-o", "--all-namespaces", "-A", "--selector", "-l", "--watch", "-w"]),
            new("describe", ["--namespace", "-n"]),
            new("logs", ["--namespace", "-n", "--tail", "--since", "-c", "--container", "--previous"]),
            new("top", ["--namespace", "-n"]),
            new("config current-context", []),
            new("config get-contexts", []),
            new("version", []),
            new("cluster-info", []),
            new("api-resources", []),
        ],

        // curl (GET only)
        ["curl"] =
        [
            new("", ["-s", "--silent", "-S", "--show-error", "-L", "--location", "-H", "--header",
                "-o", "--output", "-w", "--write-out", "--max-time", "--connect-timeout",
                "-I", "--head", "-v", "--verbose", "-k", "--insecure"]),
        ],

        // terraform - EXPLICITLY NO destroy/apply (wiped 2.5 years of production data in real incidents)
        ["terraform"] =
        [
            new("plan", ["-var", "-var-file", "-target", "-out", "-no-color"]),
            new("validate", ["-no-color"]),
            new("fmt", ["-check", "-diff", "-recursive"]),
            new("init", ["-backend=false", "-upgrade", "-no-color"]),
            new("show", ["-json", "-no-color"]),
            new("state list", []),
            new("state show", []),
            new("output", ["-json", "-no-color"]),
            new("version", []),
            new("providers", []),
            new("workspace list", []),
            new("workspace show", []),
            // BLOCKED: destroy, apply, import, taint, untaint -- too dangerous
        ],

        // python/pip (install runs setup.py - supply chain risk)
        ["pip"] =
        [
            new("install", ["-r", "--requirement", "--upgrade", "-U", "--no-build-isolation"]),
            new("list", ["--outdated", "--format"]),
            new("show", []),
            new("freeze", []),
            new("check", []),
        ],

        // cargo (Rust)
        ["cargo"] =
        [
            new("build", ["--release", "--target", "--features", "--all-features"]),
            new("test", ["--release", "--", "--test-threads"]),
            new("check", []),
            new("clippy", ["--", "-W", "-D", "-A"]),
            new("fmt", ["--check"]),
            new("run", ["--release", "--"]),
            new("doc", ["--open", "--no-deps"]),
            new("tree", ["--depth", "-d"]),
            new("update", []),
            new("clean", []),
        ],

        // make
        ["make"] =
        [
            new("", ["-j", "--jobs", "-C", "--directory"]),
        ],
    };

    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new("proxy", "run", "Run a command through the safety proxy",
            "safe proxy <tool> <args...>", SafetyLevel.CheckedWrite, RunProxy));

        // One command per allowlisted tool, each carrying the policy compiled from its entries.
        // The tool varies per command, so the handler closes over the tool name.
        commands.Add(new("proxy", "curl", "HTTP GET request via curl",
            "safe proxy curl <url> [-s] [-H <header>]", SafetyLevel.ReadOnly, (p, a) => RunTool(p, "curl", a))
            { Policy = PolicyFor("curl") });
        commands.Add(new("proxy", "gh", "GitHub CLI (read-only ops)",
            "safe proxy gh <command>", SafetyLevel.ReadOnly, (p, a) => RunTool(p, "gh", a))
            { Policy = PolicyFor("gh") });
        commands.Add(new("proxy", "az", "Azure CLI (read-only ops)",
            "safe proxy az <command>", SafetyLevel.ReadOnly, (p, a) => RunTool(p, "az", a))
            { Policy = PolicyFor("az") });
        commands.Add(new("proxy", "kubectl", "Kubernetes CLI (read-only ops)",
            "safe proxy kubectl <command>", SafetyLevel.ReadOnly, (p, a) => RunTool(p, "kubectl", a))
            { Policy = PolicyFor("kubectl") });
        commands.Add(new("proxy", "terraform", "Terraform (read/plan ops)",
            "safe proxy terraform <command>", SafetyLevel.ReadOnly, (p, a) => RunTool(p, "terraform", a))
            { Policy = PolicyFor("terraform") });
        commands.Add(new("proxy", "pip", "Python pip (install/list)",
            "safe proxy pip <command>", SafetyLevel.SafeWrite, (p, a) => RunTool(p, "pip", a))
            { Policy = PolicyFor("pip") });
        commands.Add(new("proxy", "cargo", "Rust cargo (build/test)",
            "safe proxy cargo <command>", SafetyLevel.SafeWrite, (p, a) => RunTool(p, "cargo", a))
            { Policy = PolicyFor("cargo") });
        commands.Add(new("proxy", "make", "Run make targets",
            "safe proxy make [target]", SafetyLevel.SafeWrite, (p, a) => RunTool(p, "make", a))
            { Policy = PolicyFor("make") });
    }

    /// <summary>
    /// Compiles a tool's <see cref="AllowedProxies"/> entries into its declared policy: an
    /// <c>AllowSubcommands</c> chain (prefix + per-subcommand flag allowlist). curl additionally
    /// blocks write-method flags first so the rejection message stays curl-specific.
    /// </summary>
    private static Policy PolicyFor(string tool)
    {
        var subs = AllowedProxies[tool]
            .Select(p => new Subcommand(p.SubcommandPrefix, p.AllowedFlags))
            .ToList();

        var policy = Policy.Default;
        if (tool.Equals("curl", StringComparison.OrdinalIgnoreCase))
            policy = policy.BlockFlags(CurlWriteFlags,
                "Only GET/HEAD requests are allowed through proxy",
                "Remove -X/-d/-F flags for read-only curl");
        return policy.AllowSubcommands(subs);
    }

    /// <summary>
    /// Thin re-dispatcher for the explicit <c>safe proxy run &lt;tool&gt; &lt;args...&gt;</c> form:
    /// resolves the per-tool command and routes it back through <see cref="CommandDispatcher"/> so
    /// that tool's policy is the single source of enforcement.
    /// </summary>
    private static int RunProxy(Ports p, string[] args)
    {
        if (args.Length == 0)
        {
            p.Render.Error("Usage: safe proxy <tool> <args...>");
            p.Render.Info($"Supported tools: {string.Join(", ", AllowedProxies.Keys.OrderBy(k => k))}");
            return 1;
        }

        var tool = args[0];
        var rest = args[1..];
        var found = CommandRegistry.Find("proxy", tool);
        if (found is null)
        {
            p.Render.Blocked($"proxy {tool}",
                $"Tool '{tool}' is not in the proxy allowlist",
                $"Supported: {string.Join(", ", AllowedProxies.Keys.OrderBy(k => k))}");
            return 1;
        }
        return CommandDispatcher.Execute(found, p, "proxy", tool, rest);
    }

    /// <summary>
    /// Executes an allowlisted tool. Subcommand/flag validation has already run as the command's
    /// policy; the only check left here is the usage/environment one: that the tool is installed.
    /// </summary>
    private static int RunTool(Ports p, string tool, string[] args)
    {
        if (!ProcessRunner.CommandExists(tool))
        {
            p.Render.Error($"Tool '{tool}' is not installed or not in PATH");
            return 1;
        }
        return Run.Tool(p, tool, args);
    }

    private record AllowedProxy(string SubcommandPrefix, string[] AllowedFlags);
}
