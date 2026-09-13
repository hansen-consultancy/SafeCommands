using System.Text;
using System.Text.Json;
using SafeCommands.Auditing;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class CommandAuditTests
{
    private const string Secret = "AUDIT_SECRET_9b5d7c";

    private sealed class Store : IAuditStore
    {
        public List<AuditRecord> Records { get; } = [];
        public bool Fail { get; init; }
        public void Append(AuditRecord record)
        {
            if (Fail) throw new IOException(Secret);
            Records.Add(record);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; }
        public bool Fail { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Fail ? throw new IOException(Secret) : Now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class BrokenWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException(Secret);
    }

    private sealed class BrokenExecutor : IExecutor
    {
        public ExecResult Run(string tool, IReadOnlyList<string> args, ExecOptions? opts = null)
            => throw new InvalidOperationException(Secret);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(-1)]
    public void RunsExactlyOnceAndRecordsExactResult(int result)
    {
        var store = new Store();
        var clock = new Clock();
        var audit = new CommandAudit(store, clock, TextWriter.Null);
        var calls = 0;
        Assert.Equal(result, audit.Run([], () => { calls++; clock.Now = 123; return result; }));
        Assert.Equal(1, calls);
        var record = Assert.Single(store.Records);
        Assert.Equal(result, record.ExitCode);
        Assert.Equal("returned", record.Outcome);
        Assert.Equal(DateTimeOffset.UnixEpoch, record.TimestampUtc);
        Assert.Equal(123, record.DurationMs);
        Assert.Equal(1, record.SchemaVersion);
        Assert.Equal(Environment.ProcessId, record.ProcessId);
        Assert.Equal(Environment.CurrentDirectory, record.WorkingDirectory);
        Assert.Equal(Environment.UserName, record.User);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesSameEscapingExceptionEvenWhenStorageFails(bool fail)
    {
        var store = new Store { Fail = fail };
        var error = new InvalidOperationException(Secret);
        var calls = 0;
        var audit = new CommandAudit(store, TimeProvider.System, new BrokenWriter());
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => audit.Run([], () =>
        {
            calls++;
            throw error;
        })));
        Assert.Equal(1, calls);
        if (!fail)
        {
            var record = Assert.Single(store.Records);
            Assert.Null(record.ExitCode);
            Assert.Equal("threw", record.Outcome);
            Assert.DoesNotContain(Secret, JsonSerializer.Serialize(record));
        }
    }

    [Theory]
    [InlineData("start-clock")]
    [InlineData("end-clock")]
    [InlineData("projection")]
    [InlineData("storage")]
    public void AuditFailureWarnsOnceAndNeverRetriesCommand(string failure)
    {
        var store = new Store { Fail = failure == "storage" };
        var clock = new Clock { Fail = failure == "start-clock" };
        var stderr = new StringWriter();
        var audit = new CommandAudit(store, clock, stderr);
        var calls = 0;
        Assert.Equal(17, audit.Run(failure == "projection" ? [null!] : [], () =>
        {
            calls++;
            clock.Fail = failure == "end-clock";
            return 17;
        }));
        Assert.Equal(1, calls);
        Assert.Empty(store.Records);
        Assert.Equal("SafeCommands audit logging failed; this invocation may not be recorded." + Environment.NewLine,
            stderr.ToString());
    }

    [Fact]
    public void NegativeElapsedTimeIsClamped()
    {
        var store = new Store();
        var clock = new Clock { Now = 100 };
        new CommandAudit(store, clock, TextWriter.Null).Run([], () => { clock.Now = 0; return 0; });
        Assert.Equal(0, Assert.Single(store.Records).DurationMs);
    }

    [Theory]
    [InlineData("start-clock")]
    [InlineData("end-clock")]
    [InlineData("projection")]
    public void PreparationAndTimingFailuresCannotReplaceEscapingException(string failure)
    {
        var clock = new Clock { Fail = failure == "start-clock" };
        var error = new ApplicationException("command exception");
        var audit = new CommandAudit(new Store(), clock, new BrokenWriter());
        var calls = 0;
        Assert.Same(error, Assert.Throws<ApplicationException>(() => audit.Run(failure == "projection" ? [null!] : [], () =>
        {
            calls++;
            clock.Fail = failure == "end-clock";
            throw error;
        })));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BrokenDiagnosticsCannotReplaceReturnValue()
    {
        Assert.Equal(41, new CommandAudit(new Store { Fail = true }, TimeProvider.System, new BrokenWriter()).Run([], () => 41));
    }

    [Fact]
    public void GlobalJsonSpliceDefinesRecordedArgumentCount()
    {
        CommandRegistry.Initialize();
        var (_, args) = Cli.StripJson(["--json", "proxy", "gh", "pr", "list", "--json", "number"]);
        var store = new Store();
        new CommandAudit(store, TimeProvider.System, TextWriter.Null).Run(args, () => 0);
        Assert.Equal(6, Assert.Single(store.Records).ArgumentCount);
        Assert.Equal("proxy gh", Assert.Single(store.Records).Command);
    }

    [Fact]
    public void CommandOutputAndEscapingExceptionNeverReachProductionStore()
    {
        CommandRegistry.Initialize();
        using var temp = new AuditDirectory();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var ports = new Ports(new FakeExecutor { NextResult = new(7, Secret, Secret) },
            new ConsoleRenderer(false, stdout, stderr), new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());
        var audit = new CommandAudit(new JsonlAuditStore(temp.Path), TimeProvider.System, TextWriter.Null);
        Assert.Equal(7, audit.Run(["docker", "ps"], () => Cli.Route(ports, ["docker", "ps"], false)));
        Assert.Contains(Secret, stdout.ToString());
        Assert.Contains(Secret, stderr.ToString());
        Assert.Throws<IOException>(() => audit.Run([], () => throw new IOException(Secret)));
        var text = File.ReadAllText(System.IO.Path.Combine(temp.Path, "audit.log"));
        Assert.DoesNotContain(Secret, text);
        Assert.Equal(2, File.ReadAllLines(System.IO.Path.Combine(temp.Path, "audit.log")).Length);
    }

    [Theory]
    [InlineData("", "help", 0)]
    [InlineData("help", "help", 0)]
    [InlineData("--version", "version", 0)]
    [InlineData("setup", "instructions", 0)]
    [InlineData("git", "help", 0)]
    [InlineData("git status --help", "git status", 0)]
    [InlineData("docker ps", "docker ps", 0)]
    [InlineData("git push --force", "git push", 1)]
    [InlineData("unknown-secret", "unknown", 1)]
    [InlineData("git unknown-secret", "unknown", 1)]
    [InlineData("proxy run curl -X POST", "proxy run", 1)]
    public void OuterBoundaryRecordsEachRouteOnce(string commandLine, string identity, int result)
    {
        CommandRegistry.Initialize();
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var store = new Store();
        var ports = new Ports(new FakeExecutor(), new FakeRenderer(), new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());
        var audit = new CommandAudit(store, TimeProvider.System, TextWriter.Null);
        Assert.Equal(result, audit.Run(args, () => Cli.Route(ports, args, false)));
        var record = Assert.Single(store.Records);
        Assert.Equal(identity, record.Command);
        Assert.Equal(args.Length, record.ArgumentCount);
        Assert.Equal(result, record.ExitCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AuditFailureLeavesCliOutputAndExitUnchanged(bool json, bool handlerThrows)
    {
        CommandRegistry.Initialize();
        (int Code, string Out, string Err) Run(bool audited)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            IExecutor exec = handlerThrows ? new BrokenExecutor() : new FakeExecutor { NextResult = new(19, Secret, "tool stderr") };
            var ports = new Ports(exec, new ConsoleRenderer(json, stdout, stderr), new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());
            var code = audited
                ? new CommandAudit(new Store { Fail = true }, TimeProvider.System, stderr).Run(["docker", "ps"], () => Cli.Route(ports, ["docker", "ps"], json))
                : Cli.Route(ports, ["docker", "ps"], json);
            return (code, stdout.ToString(), stderr.ToString());
        }
        var baseline = Run(false);
        var actual = Run(true);
        Assert.Equal(baseline.Code, actual.Code);
        Assert.Equal(baseline.Out, actual.Out);
        Assert.Equal(baseline.Err + "SafeCommands audit logging failed; this invocation may not be recorded." + Environment.NewLine, actual.Err);
    }

    [Fact]
    public void CaughtHandlerExceptionIsRecordedAsReturnedOne()
    {
        CommandRegistry.Initialize();
        var store = new Store();
        var ports = new Ports(new BrokenExecutor(), new FakeRenderer(), new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());
        Assert.Equal(1, new CommandAudit(store, TimeProvider.System, TextWriter.Null)
            .Run(["docker", "ps"], () => Cli.Route(ports, ["docker", "ps"], false)));
        var record = Assert.Single(store.Records);
        Assert.Equal("returned", record.Outcome);
        Assert.Equal(1, record.ExitCode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unknown-command")]
    [InlineData("args")]
    public void SecretsNeverReachPersistedRecords(string scenario)
    {
        CommandRegistry.Initialize();
        using var temp = new AuditDirectory();
        string[] args = scenario switch
        {
            "unknown" => [Secret, "status"],
            "unknown-command" => ["git", Secret],
            _ => ["proxy", "curl", "https://user:" + Secret + "@example.test/", "--header", "Authorization: " + Secret, Secret]
        };
        var ports = new Ports(new FakeExecutor { NextResult = new(0, Secret, Secret) }, new FakeRenderer(),
            new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());
        new CommandAudit(new JsonlAuditStore(temp.Path), TimeProvider.System, TextWriter.Null)
            .Run(args, () => Cli.Route(ports, args, false));
        var text = File.ReadAllText(System.IO.Path.Combine(temp.Path, "audit.log"));
        Assert.DoesNotContain(Secret, text);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(args.Length, document.RootElement.GetProperty("argumentCount").GetInt32());
        Assert.Equal(scenario == "args" ? "proxy curl" : "unknown", document.RootElement.GetProperty("command").GetString());
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("{}", false, false)]
    [InlineData("{\"audit\":false}", false, false)]
    [InlineData("{\"audit\":true,\"unrelated\":42}", true, false)]
    [InlineData("{", false, true)]
    [InlineData("{\"audit\":\"true\"}", false, true)]
    [InlineData("{\"audit\":1}", false, true)]
    [InlineData("{\"audit\":null}", false, true)]
    [InlineData("[]", false, true)]
    public void ConfigurationControlsArtifactsAndDiagnostics(string? config, bool enabled, bool warning)
    {
        using var temp = new AuditDirectory();
        var directory = System.IO.Path.Combine(temp.Path, "settings");
        if (config != null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, "config.json"), config);
        }
        var stderr = new StringWriter();
        var calls = 0;
        Assert.Equal(21, CommandAudit.FromDirectory(directory, stderr).Run([], () => { calls++; return 21; }));
        Assert.Equal(1, calls);
        Assert.Equal(enabled, File.Exists(System.IO.Path.Combine(directory, "audit.log")));
        Assert.Equal(enabled, File.Exists(System.IO.Path.Combine(directory, "audit.log.lock")));
        Assert.Equal(warning, stderr.ToString().Length > 0);
        if (config == null) Assert.False(Directory.Exists(directory));
        if (warning) Assert.Single(stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void UnreadableConfigurationDisablesAuditWithGenericWarning()
    {
        using var temp = new AuditDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(temp.Path, "config.json"));
        var stderr = new StringWriter();
        Assert.Equal(3, CommandAudit.FromDirectory(temp.Path, stderr).Run([], () => 3));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "audit.log")));
        Assert.Contains("audit logging failed", stderr.ToString());
        Assert.DoesNotContain(temp.Path, stderr.ToString());
    }
}

internal sealed class AuditDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-audit-tests-" + Guid.NewGuid().ToString("N"));
    public AuditDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
