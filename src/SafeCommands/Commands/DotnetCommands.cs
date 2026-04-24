using SafeCommands.Infrastructure;
using SafeCommands.Registry;

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

    private static int RunDotnet(string[] args, bool json)
    {
        var (code, output, error) = ProcessRunner.Run("dotnet", args);
        if (json)
            OutputFormatter.WriteJson(new { exitCode = code, output, error });
        else
        {
            OutputFormatter.WritePassthrough(output);
            OutputFormatter.WritePassthroughError(error);
        }
        return code;
    }

    // Read-only
    private static int RunListPackage(string[] args, bool json)
    {
        var dotnetArgs = args.Length > 0 ? ["list", args[0], "package"] : (string[])["list", "package"];
        return RunDotnet(dotnetArgs, json);
    }

    private static int RunListReference(string[] args, bool json)
    {
        var dotnetArgs = args.Length > 0 ? ["list", args[0], "reference"] : (string[])["list", "reference"];
        return RunDotnet(dotnetArgs, json);
    }

    private static int RunToolList(string[] args, bool json) => RunDotnet(["tool", "list", "-g"], json);
    private static int RunInfo(string[] args, bool json) => RunDotnet(["--info"], json);
    private static int RunSlnList(string[] args, bool json)
    {
        var dotnetArgs = args.Length > 0 ? ["sln", args[0], "list"] : (string[])["sln", "list"];
        return RunDotnet(dotnetArgs, json);
    }

    // Safe writes
    private static int RunBuild(string[] args, bool json) => RunDotnet(["build", ..args], json);
    private static int RunTest(string[] args, bool json) => RunDotnet(["test", ..args], json);
    private static int RunRestore(string[] args, bool json) => RunDotnet(["restore", ..args], json);
    private static int RunRun(string[] args, bool json) => RunDotnet(["run", ..args], json);
    private static int RunClean(string[] args, bool json) => RunDotnet(["clean", ..args], json);
    private static int RunPublish(string[] args, bool json) => RunDotnet(["publish", ..args], json);
    private static int RunFormat(string[] args, bool json) => RunDotnet(["format", ..args], json);
    private static int RunWatch(string[] args, bool json) => RunDotnet(["watch", ..args], json);

    private static int RunToolInstall(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe dotnet tool-install <tool>"); return 1; }
        return RunDotnet(["tool", "install", "-g", ..args], json);
    }

    private static int RunAddPackage(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe dotnet add-package <package>"); return 1; }
        return RunDotnet(["add", "package", ..args], json);
    }

    private static int RunAddReference(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe dotnet add-reference <project>"); return 1; }
        return RunDotnet(["add", "reference", ..args], json);
    }

    private static int RunNew(string[] args, bool json)
    {
        if (args.Length == 0) { OutputFormatter.WriteError("Usage: safe dotnet new <template>"); return 1; }
        return RunDotnet(["new", ..args], json);
    }

    private static int RunPack(string[] args, bool json) => RunDotnet(["pack", ..args], json);
}
