using SafeCommands.Infrastructure.Adapters;

namespace SafeCommands.Tests;

/// <summary>
/// Exercises the REAL <see cref="FileSystemWorkspace"/> boundary — no fake. ProjectRoot is the
/// process CWD, so paths are built from <c>workspace.ProjectRoot</c> rather than hard-coded so
/// the test is cross-platform and CWD-independent. Pins the /proj-vs-/projEvil sibling-prefix trap.
/// </summary>
public class WorkspaceTests
{
    [Fact]
    public void IsWithinProject_AcceptsRootItself()
    {
        var ws = new FileSystemWorkspace();
        Assert.True(ws.IsWithinProject(ws.ProjectRoot));
    }

    [Fact]
    public void IsWithinProject_AcceptsChild()
    {
        var ws = new FileSystemWorkspace();
        var child = Path.Combine(ws.ProjectRoot, "src", "file.cs");
        Assert.True(ws.IsWithinProject(child));
    }

    [Fact]
    public void IsWithinProject_RejectsSiblingPrefixDir()
    {
        // The /proj vs /projEvil trap: a sibling whose name has ProjectRoot as a string prefix
        // must NOT be treated as inside the project.
        var ws = new FileSystemWorkspace();
        Assert.False(ws.IsWithinProject(ws.ProjectRoot + "Evil"));
    }

    [Fact]
    public void IsWithinProject_RejectsOutOfTreeAbsolutePath()
    {
        var ws = new FileSystemWorkspace();
        var parent = Directory.GetParent(ws.ProjectRoot)!.FullName;
        var sibling = Path.Combine(parent, "definitely-not-the-project-dir-xyz");
        Assert.False(ws.IsWithinProject(sibling));
    }

    [Fact]
    public void Resolve_ParentTraversal_EscapesProject()
    {
        // Resolve canonicalizes against the real CWD (== ProjectRoot); climbing two levels must
        // produce a path the boundary rejects.
        var ws = new FileSystemWorkspace();
        var resolved = ws.Resolve(Path.Combine("..", "..", "etc", "passwd"));
        Assert.False(ws.IsWithinProject(resolved));
    }
}
