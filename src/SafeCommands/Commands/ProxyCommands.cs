using SafeCommands.Infrastructure;
using SafeCommands.Registry;

namespace SafeCommands.Commands;

/// <summary>
/// Proxy commands for tools not in the main groups.
/// Each entry defines exactly which subcommands/flags are allowed.
/// </summary>
static class ProxyCommands
{
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
            new("api", []),
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

        // Register convenience aliases for common proxy patterns
        commands.Add(new("proxy", "curl", "HTTP GET request via curl",
            "safe proxy curl <url> [-s] [-H <header>]", SafetyLevel.ReadOnly, (args, json) => RunProxyFor("curl", args, json)));
        commands.Add(new("proxy", "gh", "GitHub CLI (read-only ops)",
            "safe proxy gh <command>", SafetyLevel.ReadOnly, (args, json) => RunProxyFor("gh", args, json)));
        commands.Add(new("proxy", "az", "Azure CLI (read-only ops)",
            "safe proxy az <command>", SafetyLevel.ReadOnly, (args, json) => RunProxyFor("az", args, json)));
        commands.Add(new("proxy", "kubectl", "Kubernetes CLI (read-only ops)",
            "safe proxy kubectl <command>", SafetyLevel.ReadOnly, (args, json) => RunProxyFor("kubectl", args, json)));
        commands.Add(new("proxy", "terraform", "Terraform (read/plan ops)",
            "safe proxy terraform <command>", SafetyLevel.ReadOnly, (args, json) => RunProxyFor("terraform", args, json)));
        commands.Add(new("proxy", "pip", "Python pip (install/list)",
            "safe proxy pip <command>", SafetyLevel.SafeWrite, (args, json) => RunProxyFor("pip", args, json)));
        commands.Add(new("proxy", "cargo", "Rust cargo (build/test)",
            "safe proxy cargo <command>", SafetyLevel.SafeWrite, (args, json) => RunProxyFor("cargo", args, json)));
        commands.Add(new("proxy", "make", "Run make targets",
            "safe proxy make [target]", SafetyLevel.SafeWrite, (args, json) => RunProxyFor("make", args, json)));
    }

    private static int RunProxy(string[] args, bool json)
    {
        if (args.Length == 0)
        {
            OutputFormatter.WriteError("Usage: safe proxy <tool> <args...>");
            Console.WriteLine($"\nSupported tools: {string.Join(", ", AllowedProxies.Keys.OrderBy(k => k))}");
            return 1;
        }

        var tool = args[0];
        var toolArgs = args.Skip(1).ToArray();
        return RunProxyFor(tool, toolArgs, json);
    }

    private static int RunProxyFor(string tool, string[] args, bool json)
    {
        if (!AllowedProxies.TryGetValue(tool, out var allowed))
        {
            OutputFormatter.WriteBlocked($"proxy {tool}",
                $"Tool '{tool}' is not in the proxy allowlist",
                $"Supported: {string.Join(", ", AllowedProxies.Keys.OrderBy(k => k))}");
            return 1;
        }

        if (!ProcessRunner.CommandExists(tool))
        {
            OutputFormatter.WriteError($"Tool '{tool}' is not installed or not in PATH");
            return 1;
        }

        // For curl, block POST/PUT/DELETE methods
        if (tool.Equals("curl", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var arg in args)
            {
                if (arg is "-X" or "--request" or "-d" or "--data" or "--data-raw" or "--data-binary"
                    or "-F" or "--form" or "--upload-file" or "-T")
                {
                    OutputFormatter.WriteBlocked($"curl {arg}",
                        "Only GET/HEAD requests are allowed through proxy",
                        "Remove -X/-d/-F flags for read-only curl");
                    return 1;
                }
            }
        }

        // Validate subcommand against allowed patterns
        var argsStr = string.Join(' ', args);
        var matched = false;

        foreach (var proxy in allowed)
        {
            if (string.IsNullOrEmpty(proxy.SubcommandPrefix) || argsStr.StartsWith(proxy.SubcommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
        {
            var allowedSubs = allowed.Where(a => !string.IsNullOrEmpty(a.SubcommandPrefix))
                .Select(a => a.SubcommandPrefix).ToArray();
            OutputFormatter.WriteBlocked($"{tool} {argsStr}",
                $"Subcommand not in allowed list for '{tool}'",
                $"Allowed: {string.Join(", ", allowedSubs)}");
            return 1;
        }

        var (code, output, error) = ProcessRunner.Run(tool, args);
        if (json)
            OutputFormatter.WriteJson(new { tool, exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    private record AllowedProxy(string SubcommandPrefix, string[] AllowedFlags);
}
