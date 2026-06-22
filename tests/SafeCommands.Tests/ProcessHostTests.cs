using System.Diagnostics;
using SafeCommands.Infrastructure.Adapters;

namespace SafeCommands.Tests;

/// <summary>
/// Boundary tests over the REAL <see cref="ProcessHost"/> adapter (the part FakeProcessHost can't
/// exercise): enumeration must find this very process with a readable name, and Kill of a pid that
/// isn't running must degrade to <c>KillOutcome(false, …, error)</c> rather than throwing — the
/// consolidated try/catch the IProcessHost docstring promises. No real process is ever terminated.
/// </summary>
public class ProcessHostTests
{
    [Fact]
    public void List_IncludesCurrentProcess_WithReadableName()
    {
        var me = Process.GetCurrentProcess();
        var snapshot = new ProcessHost().List();
        var mine = snapshot.FirstOrDefault(p => p.Pid == me.Id);
        Assert.Equal(me.Id, mine.Pid);                 // default struct (Pid 0) would mean "not found"
        Assert.False(string.IsNullOrEmpty(mine.Name));
    }

    [Fact]
    public void FindByName_FindsCurrentProcess()
    {
        var me = Process.GetCurrentProcess();
        var found = new ProcessHost().FindByName(me.ProcessName);
        Assert.Contains(found, p => p.Pid == me.Id);
    }

    [Fact]
    public void Kill_UnknownPid_ReturnsNotKilledWithError_DoesNotThrow()
    {
        // int.MaxValue is not a live pid, so GetProcessById throws inside the adapter and the catch
        // maps it to a failed outcome — proving the adapter never lets a kill exception escape.
        var outcome = new ProcessHost().Kill(int.MaxValue);
        Assert.False(outcome.Killed);
        Assert.False(string.IsNullOrEmpty(outcome.Error));
    }
}
