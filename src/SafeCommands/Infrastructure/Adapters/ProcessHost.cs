using System.Diagnostics;
using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IProcessHost"/> over <c>System.Diagnostics.Process</c>. Each snapshot swallows the
/// per-process exceptions that occur when a process exits between enumeration and a property read —
/// the same try/catch the handlers used to carry inline, now in one place.
/// </summary>
sealed class ProcessHost : IProcessHost
{
    public IReadOnlyList<ProcessInfo> List() => Snapshot(Process.GetProcesses());

    public IReadOnlyList<ProcessInfo> FindByName(string name) => Snapshot(Process.GetProcessesByName(name));

    public KillOutcome Kill(int pid)
    {
        string? name = null;
        try
        {
            var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
            proc.Kill();
            return new KillOutcome(true, name, null);
        }
        catch (Exception ex)
        {
            return new KillOutcome(false, name, ex.Message);
        }
    }

    private static IReadOnlyList<ProcessInfo> Snapshot(Process[] procs)
    {
        var result = new List<ProcessInfo>(procs.Length);
        foreach (var p in procs)
        {
            try
            {
                var name = p.ProcessName;                       // unreadable -> process died; skip it
                long mem;
                try { mem = p.WorkingSet64; } catch { mem = 0L; }
                result.Add(new ProcessInfo(p.Id, name, mem));
            }
            catch { /* exited between enumeration and read */ }
        }
        return result;
    }
}
