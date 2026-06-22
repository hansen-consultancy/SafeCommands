using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Controllable fake <see cref="IProcessHost"/>. <see cref="Table"/> seeds what List/FindByName
/// return; <see cref="KillCalls"/> records every pid passed to Kill; pids in <see cref="FailKills"/>
/// report a failed kill (so warning paths are testable). No real process is ever touched.
/// </summary>
sealed class FakeProcessHost : IProcessHost
{
    public List<ProcessInfo> Table { get; } = [];
    public List<int> KillCalls { get; } = [];
    public HashSet<int> FailKills { get; } = [];

    public IReadOnlyList<ProcessInfo> List() => Table;

    public IReadOnlyList<ProcessInfo> FindByName(string name)
        => Table.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

    public KillOutcome Kill(int pid)
    {
        KillCalls.Add(pid);
        var name = Table.FirstOrDefault(p => p.Pid == pid).Name;
        return FailKills.Contains(pid)
            ? new KillOutcome(false, name, "simulated kill failure")
            : new KillOutcome(true, name, null);
    }
}
