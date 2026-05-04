using SafeCommands.Infrastructure.Ports;

namespace SafeCommands.Tests.Fakes;

/// <summary>
/// Fluent-builder fake <see cref="IGitRepo"/>. Default state: in a clean repo with no
/// tracked files and a detached HEAD. Tests opt in via <c>WithTracked</c>,
/// <c>WithPushedHead</c>, etc.
/// </summary>
sealed class FakeGitRepo : IGitRepo
{
    bool _isRepo = true;
    bool _isClean = true;
    HeadStatus _head;
    readonly HashSet<string> _tracked = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

    public FakeGitRepo AsRepo()         { _isRepo = true; return this; }
    public FakeGitRepo AsNotRepo()      { _isRepo = false; return this; }
    public FakeGitRepo WithCleanTree()  { _isClean = true; return this; }
    public FakeGitRepo WithDirtyTree()  { _isClean = false; return this; }

    public FakeGitRepo WithTracked(params string[] files)
    {
        foreach (var f in files) _tracked.Add(f);
        return this;
    }

    public FakeGitRepo WithPendingChanges(params string[] files)
    {
        foreach (var f in files) _pending.Add(f);
        return this;
    }

    public FakeGitRepo WithPushedHead(string branch, string upstream)
    {
        _head = new HeadStatus(branch, upstream, true);
        return this;
    }

    public FakeGitRepo WithUnpushedHead(string branch)
    {
        _head = new HeadStatus(branch, null, false);
        return this;
    }

    public bool IsRepo() => _isRepo;
    public bool IsWorkingTreeClean() => _isClean;
    public bool IsFileTracked(string relativePath) => _tracked.Contains(relativePath);
    public bool HasPendingChanges(string relativePath) => _pending.Contains(relativePath);
    public HeadStatus GetHeadStatus() => _head;
}
