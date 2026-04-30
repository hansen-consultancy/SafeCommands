using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Sugar;

namespace SafeCommands.Commands;

static class DotnetCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.AddRange([
            // Read-only
            new("dotnet", "list-package", "List NuGet packages", "safe dotnet list-package [<project>]", SafetyLevel.ReadOnly, RunListPackage),
            new("dotnet", "list-reference", "List project references", "safe dotnet list-reference [<project>]", SafetyLevel.ReadOnly, RunListReference),
            new("dotnet", "tool-list", "List installed tools", "safe dotnet tool-list", SafetyLevel.ReadOnly, RunToolList),
            new("dotnet", "info", "Show .NET SDK info", "safe dotnet info", SafetyLevel.ReadOnly, RunInfo),
            new("dotnet", "sln-list", "List solution projects", "safe dotnet sln-list [<solution>]", SafetyLevel.ReadOnly, RunSlnList),

            // Safe writes
            new("dotnet", "build", "Build project", "safe dotnet build [<project>] [-c <config>]", SafetyLevel.SafeWrite, RunBuild),
            new("dotnet", "test", "Run tests", "safe dotnet test [<project>] [--filter <expr>]", SafetyLevel.SafeWrite, RunTest),
            new("dotnet", "restore", "Restore NuGet packages", "safe dotnet restore [<project>]", SafetyLevel.SafeWrite, RunRestore),
            new("dotnet", "run", "Run project", "safe dotnet run [<project>]", SafetyLevel.SafeWrite, RunRun),
            new("dotnet", "clean", "Clean build output", "safe dotnet clean [<project>]", SafetyLevel.SafeWrite, RunClean),
            new("dotnet", "publish", "Publish project", "safe dotnet publish [<project>]", SafetyLevel.SafeWrite, RunPublish),
            new("dotnet", "format", "Format code", "safe dotnet format [<project>]", SafetyLevel.SafeWrite, RunFormat),
            new("dotnet", "watch", "Watch mode", "safe dotnet watch [<command>]", SafetyLevel.SafeWrite, RunWatch),
            new("dotnet", "tool-install", "Install a global tool (runs arbitrary code!)", "safe dotnet tool-install <tool>", SafetyLevel.CheckedWrite, RunToolInstall),
            new("dotnet", "add-package", "Add NuGet package (may run MSBuild targets)", "safe dotnet add-package <package> [--version <ver>]", SafetyLevel.CheckedWrite, RunAddPackage),
            new("dotnet", "add-reference", "Add project reference", "safe dotnet add-reference <project>", SafetyLevel.SafeWrite, RunAddReference),
            new("dotnet", "new", "Create from template", "safe dotnet new <template> [args...]", SafetyLevel.SafeWrite, RunNew),
            new("dotnet", "pack", "Create NuGet package", "safe dotnet pack [<project>]", SafetyLevel.SafeWrite, RunPack),
        ]);
    }

    // Read-only
    internal static int RunListPackage(Ports p, string[] args)
        => Run.Tool(p, "dotnet", args.Length > 0 ? ["list", args[0], "package"] : ["list", "package"]);

    internal static int RunListReference(Ports p, string[] args)
        => Run.Tool(p, "dotnet", args.Length > 0 ? ["list", args[0], "reference"] : ["list", "reference"]);

    internal static int RunToolList(Ports p, string[] args) => Run.Tool(p, "dotnet", ["tool", "list", "-g"]);
    internal static int RunInfo(Ports p, string[] args)     => Run.Tool(p, "dotnet", ["--info"]);

    internal static int RunSlnList(Ports p, string[] args)
        => Run.Tool(p, "dotnet", args.Length > 0 ? ["sln", args[0], "list"] : ["sln", "list"]);

    // Safe writes
    internal static int RunBuild(Ports p, string[] args)   => Run.Tool(p, "dotnet", ["build", .. args]);
    internal static int RunTest(Ports p, string[] args)    => Run.Tool(p, "dotnet", ["test", .. args]);
    internal static int RunRestore(Ports p, string[] args) => Run.Tool(p, "dotnet", ["restore", .. args]);
    internal static int RunRun(Ports p, string[] args)     => Run.Tool(p, "dotnet", ["run", .. args]);
    internal static int RunClean(Ports p, string[] args)   => Run.Tool(p, "dotnet", ["clean", .. args]);
    internal static int RunPublish(Ports p, string[] args) => Run.Tool(p, "dotnet", ["publish", .. args]);
    internal static int RunFormat(Ports p, string[] args)  => Run.Tool(p, "dotnet", ["format", .. args]);
    internal static int RunWatch(Ports p, string[] args)   => Run.Tool(p, "dotnet", ["watch", .. args]);

    internal static int RunToolInstall(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe dotnet tool-install <tool>"); return 1; }
        return Run.Tool(p, "dotnet", ["tool", "install", "-g", .. args]);
    }

    internal static int RunAddPackage(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe dotnet add-package <package>"); return 1; }
        return Run.Tool(p, "dotnet", ["add", "package", .. args]);
    }

    internal static int RunAddReference(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe dotnet add-reference <project>"); return 1; }
        return Run.Tool(p, "dotnet", ["add", "reference", .. args]);
    }

    internal static int RunNew(Ports p, string[] args)
    {
        if (args.Length == 0) { p.Render.Error("Usage: safe dotnet new <template>"); return 1; }
        return Run.Tool(p, "dotnet", ["new", .. args]);
    }

    internal static int RunPack(Ports p, string[] args) => Run.Tool(p, "dotnet", ["pack", .. args]);
}
