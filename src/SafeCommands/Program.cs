using SafeCommands;
using SafeCommands.Auditing;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;

CommandRegistry.Initialize();

// Pull the global --json flag out before wiring the renderer (it needs to know the mode up front).
var (jsonOutput, cliArgs) = Cli.StripJson(args);

var exec = new ProcessExecutor();
var ports = new Ports(exec, new ConsoleRenderer(jsonOutput), new GitRepoProbe(exec), new FileSystemWorkspace(), new ProcessHost());

var audit = CommandAudit.FromEnvironment(Console.Error);
return audit.Run(cliArgs, () => Cli.Route(ports, cliArgs, jsonOutput));
