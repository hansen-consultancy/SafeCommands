using SafeCommands.Auditing;

namespace SafeCommands.Tests;

// Runs the production store in separate processes without widening its visibility.
internal static class AuditProcess
{
    public static int Main(string[] args)
    {
        if (args.Length < 2) return 2;
        if (args[0] == "lock")
        {
            using var gate = new FileStream(Path.Combine(args[1], "audit.log.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Console.WriteLine("ready");
            Console.ReadLine();
            return 0;
        }
        if (args[0] != "append") return 2;
        Console.WriteLine("ready");
        Console.ReadLine();
        var store = new JsonlAuditStore(args[1]);
        for (var i = 0; i < int.Parse(args[3]); i++)
            store.Append(Record(args[2] + ":" + i));
        return 0;
    }

    internal static AuditRecord Record(string command = "docker ps") =>
        new(1, DateTimeOffset.UnixEpoch, command, 2, null, null, Environment.ProcessId, 0, 0, "returned");
}
