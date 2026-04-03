using SafeCommands.Commands;

namespace SafeCommands.Registry;

static class CommandRegistry
{
    private static readonly List<CommandDefinition> _commands = [];

    public static IReadOnlyList<CommandDefinition> Commands => _commands;

    public static void Initialize()
    {
        GitCommands.Register(_commands);
        FileCommands.Register(_commands);
        ProcessCommands.Register(_commands);
        DockerCommands.Register(_commands);
        NpmCommands.Register(_commands);
        PnpmCommands.Register(_commands);
        BunCommands.Register(_commands);
        DotnetCommands.Register(_commands);
        EnvCommands.Register(_commands);
        ProxyCommands.Register(_commands);
    }

    public static CommandDefinition? Find(string group, string name)
        => _commands.FirstOrDefault(c =>
            c.Group.Equals(group, StringComparison.OrdinalIgnoreCase) &&
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<CommandDefinition> FindByGroup(string group)
        => _commands.Where(c => c.Group.Equals(group, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<string> Groups
        => _commands.Select(c => c.Group).Distinct();
}
