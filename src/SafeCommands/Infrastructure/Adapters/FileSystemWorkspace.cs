using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Infrastructure.Adapters;

/// <summary>
/// Real <see cref="IWorkspace"/> adapter. The only place <see cref="Directory.GetCurrentDirectory"/>
/// and <see cref="Path.GetFullPath(string)"/> are read for the safety decision. Owns the
/// trailing-separator boundary trick that defends <c>/proj</c> against <c>/projEvil</c>.
/// </summary>
sealed class FileSystemWorkspace : IWorkspace
{
    public string ProjectRoot { get; } = Path.GetFullPath(Directory.GetCurrentDirectory());

    public string Resolve(string path) => Path.GetFullPath(path);

    public bool IsWithinProject(string canonicalPath)
    {
        var rootWithSep = ProjectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? ProjectRoot
            : ProjectRoot + Path.DirectorySeparatorChar;

        return canonicalPath.Equals(rootWithSep.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || canonicalPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }
}
