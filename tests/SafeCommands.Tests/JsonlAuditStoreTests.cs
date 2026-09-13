using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SafeCommands.Auditing;

namespace SafeCommands.Tests;

public class JsonlAuditStoreTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static byte[] Line(AuditRecord record) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, JsonOptions) + "\n");

    [Fact]
    public void CompactUtf8JsonlEscapesEmbeddedLinesAndPreservesFields()
    {
        using var temp = new AuditDirectory();
        var record = AuditProcess.Record("quotes\" slash\\ line\nreturn\r unicode Ω") with { WorkingDirectory = "a\nb", ExitCode = null, Outcome = "threw" };
        new JsonlAuditStore(temp.Path).Append(record);
        var bytes = File.ReadAllBytes(Path.Combine(temp.Path, "audit.log"));
        Assert.Equal(Line(record), bytes);
        var line = Assert.Single(File.ReadAllLines(Path.Combine(temp.Path, "audit.log")));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(record.Command, document.RootElement.GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("exitCode").ValueKind);
        Assert.Equal("1970-01-01T00:00:00+00:00", document.RootElement.GetProperty("timestampUtc").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RotatesOnlyWhenNextLineWouldExceedLimit(int extraByte)
    {
        using var temp = new AuditDirectory();
        var record = AuditProcess.Record();
        var path = Path.Combine(temp.Path, "audit.log");
        SeedCompleteLine(path, JsonlAuditStore.MaximumFileBytes - Line(record).Length + extraByte);
        new JsonlAuditStore(temp.Path).Append(record);
        Assert.Equal(extraByte == 1, File.Exists(path + ".1"));
        Assert.Equal(extraByte == 0 ? JsonlAuditStore.MaximumFileBytes : Line(record).Length, new FileInfo(path).Length);
    }

    [Fact]
    public void RotationRetainsExactlyActiveAndTwoNewestArchives()
    {
        using var temp = new AuditDirectory();
        var path = Path.Combine(temp.Path, "audit.log");
        SeedCompleteLine(path, JsonlAuditStore.MaximumFileBytes);
        File.WriteAllText(path + ".1", "previous\n");
        File.WriteAllText(path + ".2", "expired\n");
        new JsonlAuditStore(temp.Path).Append(AuditProcess.Record("new"));
        Assert.Equal(JsonlAuditStore.MaximumFileBytes, new FileInfo(path + ".1").Length);
        Assert.Equal("previous\n", File.ReadAllText(path + ".2"));
        Assert.Equal(3, Directory.GetFiles(temp.Path, "audit.log*").Count(p => !p.EndsWith(".lock")));
        Assert.Contains("new", File.ReadAllText(path));
    }

    [Fact]
    public void OversizedRecordFailsWithoutCreatingArtifacts()
    {
        using var temp = new AuditDirectory();
        var directory = Path.Combine(temp.Path, "not-created");
        Assert.Throws<IOException>(() => new JsonlAuditStore(directory)
            .Append(AuditProcess.Record(new string('x', JsonlAuditStore.MaximumFileBytes))));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RecordExactlyAtFileLimitIsAccepted()
    {
        using var temp = new AuditDirectory();
        var overhead = Line(AuditProcess.Record("")).Length;
        var record = AuditProcess.Record(new string('x', JsonlAuditStore.MaximumFileBytes - overhead));
        new JsonlAuditStore(temp.Path).Append(record);
        Assert.Equal(JsonlAuditStore.MaximumFileBytes, new FileInfo(Path.Combine(temp.Path, "audit.log")).Length);
    }

    [Fact]
    public void LockIsNotHeldWhileCommandExecutes()
    {
        using var temp = new AuditDirectory();
        var store = new JsonlAuditStore(temp.Path);
        new CommandAudit(store, TimeProvider.System, TextWriter.Null).Run([], () =>
        {
            store.Append(AuditProcess.Record("during-command"));
            return 0;
        });
        Assert.Equal(2, File.ReadAllLines(Path.Combine(temp.Path, "audit.log")).Length);
    }

    [Fact]
    public void ExistingOwnerPermissionsAreNeverBroadened()
    {
        using var temp = new AuditDirectory();
        var path = Path.Combine(temp.Path, "audit.log");
        File.WriteAllText(path, "");
        if (OperatingSystem.IsWindows())
        {
            var info = new FileInfo(path);
            var original = info.GetAccessControl();
            var restricted = new FileSecurity();
            restricted.SetAccessRuleProtection(true, false);
            restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.ReadPermissions | FileSystemRights.ChangePermissions,
                AccessControlType.Allow));
            info.SetAccessControl(restricted);
            try
            {
                var before = info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                new CommandAudit(new JsonlAuditStore(temp.Path), TimeProvider.System, TextWriter.Null).Run([], () => 0);
                Assert.Equal(before, info.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            }
            finally { info.SetAccessControl(original); }
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserWrite);
            try
            {
                new CommandAudit(new JsonlAuditStore(temp.Path), TimeProvider.System, TextWriter.Null).Run([], () => 0);
                Assert.Equal(UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
            finally { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9000)]
    public void RecoversIncompleteTailAndPreservesCompleteLines(int tailSize)
    {
        using var temp = new AuditDirectory();
        var path = Path.Combine(temp.Path, "audit.log");
        var previous = Line(AuditProcess.Record("previous"));
        File.WriteAllBytes(path, previous.Concat(Encoding.UTF8.GetBytes(new string('x', tailSize))).ToArray());
        var record = AuditProcess.Record("next");
        new JsonlAuditStore(temp.Path).Append(record);
        Assert.Equal(previous.Concat(Line(record)).ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void EntireIncompleteFileIsDiscardedBeforeAppend()
    {
        using var temp = new AuditDirectory();
        var path = Path.Combine(temp.Path, "audit.log");
        File.WriteAllText(path, "{incomplete");
        var record = AuditProcess.Record();
        new JsonlAuditStore(temp.Path).Append(record);
        Assert.Equal(Line(record), File.ReadAllBytes(path));
    }

    [Fact]
    public void RotationFailureStopsWithoutAppendingOrRollingBackEarlierStep()
    {
        using var temp = new AuditDirectory();
        var path = Path.Combine(temp.Path, "audit.log");
        SeedCompleteLine(path, JsonlAuditStore.MaximumFileBytes);
        Directory.CreateDirectory(path + ".1");
        File.WriteAllText(path + ".2", "expired\n");
        Assert.ThrowsAny<IOException>(() => new JsonlAuditStore(temp.Path).Append(AuditProcess.Record()));
        Assert.False(File.Exists(path + ".2"));
        Assert.True(Directory.Exists(path + ".1"));
        Assert.Equal(JsonlAuditStore.MaximumFileBytes, new FileInfo(path).Length);
    }

    [Fact]
    public void UnwritableDestinationPreservesInvocationResult()
    {
        using var temp = new AuditDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "audit.log"));
        var stderr = new StringWriter();
        Assert.Equal(11, new CommandAudit(new JsonlAuditStore(temp.Path), TimeProvider.System, stderr).Run([], () => 11));
        Assert.Contains("audit logging failed", stderr.ToString());
        Assert.DoesNotContain(temp.Path, stderr.ToString());
    }

    [Fact]
    public void CreatedFilesAndDirectoryHaveOnlyUserAccess()
    {
        using var temp = new AuditDirectory();
        var directory = Path.Combine(temp.Path, "private");
        new JsonlAuditStore(directory).Append(AuditProcess.Record());
        var files = Directory.GetFiles(directory);
        Assert.Equal(2, files.Length);
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User;
            var security = new DirectoryInfo(directory).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow) Assert.Equal(user, rule.IdentityReference);
            foreach (var file in files)
            {
                var acl = new FileInfo(file).GetAccessControl();
                Assert.True(acl.AreAccessRulesProtected);
                foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    if (rule.AccessControlType == AccessControlType.Allow) Assert.Equal(user, rule.IdentityReference);
            }
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
            foreach (var file in files)
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [Fact]
    public async Task CompetingProcessesAppendCompleteUniqueRecordsAndRotateOnce()
    {
        using var temp = new AuditDirectory();
        var path = Path.Combine(temp.Path, "audit.log");
        SeedCompleteLine(path, JsonlAuditStore.MaximumFileBytes);
        var processes = new List<Process>();
        try
        {
            for (var i = 0; i < 4; i++)
                processes.Add(Start("append", temp.Path, "worker" + i, "20"));
            foreach (var process in processes) await Ready(process);
            foreach (var process in processes) await process.StandardInput.WriteLineAsync("go");
            foreach (var process in processes) await Success(process);
            var records = File.ReadAllLines(path).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(80, records.Length);
                Assert.Equal(80, records.Select(d => d.RootElement.GetProperty("command").GetString()).Distinct().Count());
            }
            finally { foreach (var record in records) record.Dispose(); }
            Assert.Equal(JsonlAuditStore.MaximumFileBytes, new FileInfo(path + ".1").Length);
            Assert.False(File.Exists(path + ".2"));
        }
        finally { Stop(processes); }
    }

    [Fact]
    public async Task ContendedLockTimesOutAndOwnerExitReleasesPersistentLock()
    {
        using var temp = new AuditDirectory();
        var owner = Start("lock", temp.Path);
        try
        {
            await Ready(owner);
            var stderr = new StringWriter();
            var timer = Stopwatch.StartNew();
            Assert.Equal(8, new CommandAudit(new JsonlAuditStore(temp.Path), TimeProvider.System, stderr).Run([], () => 8));
            Assert.InRange(timer.ElapsedMilliseconds, 200, 3000);
            Assert.Contains("audit logging failed", stderr.ToString());
            Assert.False(File.Exists(Path.Combine(temp.Path, "audit.log")));
            owner.Kill();
            await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(File.Exists(Path.Combine(temp.Path, "audit.log.lock")));
            new JsonlAuditStore(temp.Path).Append(AuditProcess.Record("after-owner-exit"));
            Assert.Single(File.ReadAllLines(Path.Combine(temp.Path, "audit.log")));
        }
        finally { Stop([owner]); }
    }

    private static void SeedCompleteLine(string path, int bytes)
    {
        // A complete JSON string line allows exact byte-size boundaries without many allocations.
        using var stream = File.Create(path);
        stream.WriteByte((byte)'"');
        var chunk = Enumerable.Repeat((byte)'x', 8192).ToArray();
        var remaining = bytes - 3;
        while (remaining > 0)
        {
            var count = Math.Min(remaining, chunk.Length);
            stream.Write(chunk, 0, count);
            remaining -= count;
        }
        stream.WriteByte((byte)'"');
        stream.WriteByte((byte)'\n');
    }

    private static Process Start(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof(AuditProcess).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }

    private static async Task Ready(Process process)
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("ready", line);
    }

    private static async Task Success(Process process)
    {
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(process.ExitCode == 0, await error);
    }

    private static void Stop(IEnumerable<Process> processes)
    {
        List<Exception>? errors = null;
        foreach (var process in processes)
        {
            try
            {
                using (process)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        if (!process.WaitForExit(10000))
                            throw new TimeoutException("Audit test worker did not exit during cleanup.");
                    }
                }
            }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        if (errors != null) throw new AggregateException("Audit test worker cleanup failed.", errors);
    }
}
