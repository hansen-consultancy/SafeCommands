using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Minimal controllable fake <see cref="IWorkspace"/>. <see cref="Resolve"/> is the identity
/// (tests pass already-canonical strings); the containment decision is an injectable predicate
/// so a test can pin the exact boundary it wants without filesystem dependence.
/// </summary>
sealed class FakeWorkspace : IWorkspace
{
    public string ProjectRoot { get; set; } = "/proj";

    public Func<string, bool>? WithinPredicate { get; set; }

    public string Resolve(string path) => path;

    public bool IsWithinProject(string canonicalPath)
        => (WithinPredicate ?? DefaultWithin)(canonicalPath);

    private bool DefaultWithin(string p)
        => p == ProjectRoot || p.StartsWith(ProjectRoot + "/");
}
