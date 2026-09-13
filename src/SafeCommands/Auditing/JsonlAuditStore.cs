using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace SafeCommands.Auditing;

sealed class JsonlAuditStore(string directory) : IAuditStore
{
    internal const int MaximumFileBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Append(AuditRecord record)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        if (json.Length >= MaximumFileBytes)
            throw new IOException("Audit record exceeds size limit.");

        EnsurePrivateDirectory();
        var path = Path.Combine(directory, "audit.log");
        using var gate = AcquireLock(path + ".lock");
        using (var active = OpenPrivateFile(path, FileShare.None))
        {
            RecoverTail(active);
            if (active.Length + json.Length + 1 <= MaximumFileBytes)
            {
                WriteRecord(active, json);
                return;
            }
        }

        File.Delete(path + ".2");
        MoveIfPresent(path + ".1", path + ".2");
        MoveIfPresent(path, path + ".1");
        using var next = OpenPrivateFile(path, FileShare.None);
        WriteRecord(next, json);
    }

    private FileStream AcquireLock(string path)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return OpenPrivateFile(path, FileShare.None); }
            catch (IOException) when (timer.ElapsedMilliseconds < 250)
            {
                Thread.Sleep((int)Math.Clamp(250 - timer.ElapsedMilliseconds, 0, 10));
            }
        }
    }

    private static void MoveIfPresent(string source, string destination)
    {
        try { File.Move(source, destination); }
        catch (FileNotFoundException) { }
    }

    private static void WriteRecord(FileStream stream, byte[] json)
    {
        stream.Position = stream.Length;
        stream.Write(json);
        stream.WriteByte((byte)'\n');
        stream.Flush();
    }

    private static void RecoverTail(FileStream stream)
    {
        if (stream.Length == 0) return;
        var buffer = new byte[4096];
        var end = stream.Length;
        while (end > 0)
        {
            var start = Math.Max(0, end - buffer.Length);
            stream.Position = start;
            var length = (int)(end - start);
            stream.ReadExactly(buffer.AsSpan(0, length));
            for (var i = length - 1; i >= 0; i--)
            {
                if (buffer[i] != '\n') continue;
                stream.SetLength(start + i + 1);
                return;
            }
            end = start;
        }
        stream.SetLength(0);
    }

    private void EnsurePrivateDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Directory.Exists(directory))
            {
                var info = new DirectoryInfo(directory);
                var existing = info.GetAccessControl();
                RestrictAccess(existing);
                info.SetAccessControl(existing);
                return;
            }
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).Create(security);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(directory, File.GetUnixFileMode(directory) &
                (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        }
    }

    private static FileStream OpenPrivateFile(string path, FileShare share)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl, AccessControlType.Allow));
            var info = new FileInfo(path);
            var windowsStream = info.Create(FileMode.OpenOrCreate, FileSystemRights.FullControl,
                share, 4096, FileOptions.None, security);
            try
            {
                var existing = info.GetAccessControl();
                RestrictAccess(existing);
                info.SetAccessControl(existing);
                return windowsStream;
            }
            catch { windowsStream.Dispose(); throw; }
        }

        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = share,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        try
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) & (UnixFileMode.UserRead | UnixFileMode.UserWrite));
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictAccess(FileSystemSecurity security)
    {
        // Preserve the user's existing rights and all denies; only remove other users' grants.
        var user = WindowsIdentity.GetCurrent().User!;
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user))
                security.RemoveAccessRuleSpecific(rule);
    }
}
