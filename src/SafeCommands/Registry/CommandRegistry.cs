using SafeCommands.Commands;

namespace SafeCommands.Registry;

/// <summary>
/// The Built-in Allowlist: the immutable, compiled-in collection of every
/// Command Definition SafeCommands will execute. Populated once at startup
/// by <see cref="Initialize"/> and read-only thereafter.
/// </summary>
static class CommandRegistry
{
    private static readonly List<CommandDefinition> _builtIn = [];
    private static readonly object _gate = new();
    private static volatile bool _initialized;

    public static IReadOnlyList<CommandDefinition> Commands => _builtIn;

    /// <summary>
    /// Builds the allowlist exactly once. Idempotent and thread-safe: repeated or concurrent
    /// calls (e.g. from xUnit's parallel test collections, which each Initialize before a Find)
    /// build once and then no-op, rather than racing on — or duplicating into — the shared list.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;
            GitCommands.Register(_builtIn);
            FileCommands.Register(_builtIn);
            ProcessCommands.Register(_builtIn);
            DockerCommands.Register(_builtIn);
            NpmCommands.Register(_builtIn);
            PnpmCommands.Register(_builtIn);
            BunCommands.Register(_builtIn);
            DotnetCommands.Register(_builtIn);
            DbCommands.Register(_builtIn);
            EnvCommands.Register(_builtIn);
            ProxyCommands.Register(_builtIn);
            GenerateCommands.Register(_builtIn);
            _initialized = true;
        }
    }

    public static CommandDefinition? Find(string group, string name)
        => _builtIn.FirstOrDefault(c =>
            c.Group.Equals(group, StringComparison.OrdinalIgnoreCase) &&
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<CommandDefinition> FindByGroup(string group)
        => _builtIn.Where(c => c.Group.Equals(group, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> Groups
        => _builtIn.Select(c => c.Group).Distinct();
}
