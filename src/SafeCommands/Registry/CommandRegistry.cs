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

    public static IReadOnlyList<CommandDefinition> Commands => _builtIn;

    public static void Initialize()
    {
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
