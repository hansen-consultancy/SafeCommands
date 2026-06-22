namespace SafeCommands.Infrastructure.Ports;

/// <summary>A point-in-time snapshot of one OS process. Plain data so handlers never hold a
/// <c>System.Diagnostics.Process</c> (and tests never enumerate or kill a real one).</summary>
readonly record struct ProcessInfo(int Pid, string Name, long Memory);

/// <summary>
/// Outcome of a kill attempt. <see cref="Killed"/> is false when the process was already gone or
/// could not be terminated (access denied, etc.); <see cref="Error"/> then carries the reason.
/// <see cref="Name"/> is the process name resolved just before termination, when it could be read.
/// </summary>
readonly record struct KillOutcome(bool Killed, string? Name, string? Error);

/// <summary>
/// Port over the OS process table: enumerate, look up by name, and terminate by pid. Hides
/// <c>System.Diagnostics.Process</c> so the process commands are testable without enumerating or
/// killing real processes, and so the "it exited between enumeration and read" races live in one
/// adapter instead of every handler.
///
/// This port deliberately does NOT enforce which processes may be killed — that restriction is a
/// declared <c>Policy</c> (the dev-tools allowlist on <c>kill-name</c>) evaluated at dispatch. The
/// port faithfully kills whatever pid it is handed; the gate lives upstream.
/// </summary>
interface IProcessHost
{
    /// <summary>Best-effort snapshot of all running processes; entries that throw mid-read are skipped.</summary>
    IReadOnlyList<ProcessInfo> List();

    /// <summary>Snapshot of processes whose name matches <paramref name="name"/> (OS matching rules).</summary>
    IReadOnlyList<ProcessInfo> FindByName(string name);

    /// <summary>Terminate the process with <paramref name="pid"/>, resolving its name first when possible.</summary>
    KillOutcome Kill(int pid);
}
