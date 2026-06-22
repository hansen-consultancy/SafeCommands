using SafeCommands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;

CommandRegistry.Initialize();

// Pull the global --json flag out before wiring the renderer (it needs to know the mode up front).
var (jsonOutput, cliArgs) = Cli.StripJson(args);

var exec = new ProcessExecutor();
var ports = new Ports(exec, new ConsoleRenderer(jsonOutput), new GitRepoProbe(exec), new FileSystemWorkspace(), new ProcessHost());

return Cli.Route(ports, cliArgs, jsonOutput);
