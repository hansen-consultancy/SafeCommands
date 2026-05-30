namespace SafeCommands.Infrastructure.Ports;

/// <summary>
/// Read-only window onto the workspace filesystem, for path containment. Resolution and the
/// project-root boundary are decided here, so the security decision is a pure string
/// comparison; the rule never calls <c>Path.GetFullPath</c> or <c>Directory.GetCurrentDirectory</c>.
/// </summary>
interface IWorkspace
{
    /// <summary>Absolute, canonical project root.</summary>
    string ProjectRoot { get; }

    /// <summary>Resolves a (possibly relative) path to a canonical absolute path.</summary>
    string Resolve(string path);

    /// <summary>True when <paramref name="canonicalPath"/> is the root or a descendant of it.</summary>
    bool IsWithinProject(string canonicalPath);
}
