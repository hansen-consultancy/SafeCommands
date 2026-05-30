using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Pins the policies wired onto the six legacy-signature command groups (git, db, docker,
/// process, npm, pnpm) after their inline validation was migrated to declared Policy chains.
///
/// SPAWN HAZARD: these handlers still call the static ProcessRunner.Run directly (not ports.Exec),
/// so an ALLOWED/clean or REWRITE input pushed through CommandDispatcher.Execute would spawn a
/// real git/docker/php/python/npm process. Therefore every allow/clean/rewrite case here asserts
/// on the policy DIRECTLY (Find(...)!.Policy.Evaluate — pure, no spawn). Only blocked cases may go
/// through the dispatcher, and only where the central render path is the thing under test.
/// </summary>
public class MigratedCommandPolicyTests
{
    private static SafetyContext Ctx(IRepoProbe? repo = null, IWorkspace? ws = null)
        => new("label", repo ?? new FakeRepoProbe(), ws ?? new FakeWorkspace());

    private static Policy P(string group, string cmd)
    {
        CommandRegistry.Initialize();
        var def = CommandRegistry.Find(group, cmd);
        Assert.NotNull(def);
        return def.Policy;
    }

    // ============================================================ git

    [Theory]
    [InlineData("--force")]
    [InlineData("--delete")]
    public void Git_Push_BlocksForceAndDelete(string flag)
        => Assert.True(P("git", "push").Evaluate([flag], Ctx()).IsBlocked);

    [Fact]
    public void Git_Push_BlocksForceEqualsTrue_Defect1()
    {
        // DEFECT #1: "--force=true" normalizes to base "--force" and must block.
        Assert.True(P("git", "push").Evaluate(["--force=true"], Ctx()).IsBlocked);
    }

    [Fact]
    public void Git_Push_DoesNotBlockForceWithLease()
        => Assert.False(P("git", "push").Evaluate(["--force-with-lease"], Ctx()).IsBlocked);

    [Fact]
    public void Git_Push_AllowsRemoteAndBranch()
        => Assert.False(P("git", "push").Evaluate(["origin", "main"], Ctx()).IsBlocked);

    [Theory]
    [InlineData("--no-verify")]
    [InlineData("-n")]
    public void Git_Commit_BlocksHookBypass(string flag)
        => Assert.True(P("git", "commit").Evaluate([flag], Ctx()).IsBlocked);

    [Fact]
    public void Git_Commit_BlocksAmend_WithNullSuggestion()
    {
        var block = P("git", "commit").Evaluate(["--amend"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Null(block.Suggestion);
    }

    [Fact]
    public void Git_Commit_AllowsMessage()
        => Assert.False(P("git", "commit").Evaluate(["-m", "msg"], Ctx()).IsBlocked);

    [Theory]
    [InlineData("-A")]
    [InlineData("--all")]
    [InlineData(".")]
    public void Git_Add_BlocksStageAll(string arg)
        => Assert.True(P("git", "add").Evaluate([arg], Ctx()).IsBlocked);

    [Fact]
    public void Git_Add_AllowsSpecificFile()
        => Assert.False(P("git", "add").Evaluate(["file.cs"], Ctx()).IsBlocked);

    [Fact]
    public void Git_Checkout_BlocksDot()
        => Assert.True(P("git", "checkout").Evaluate(["."], Ctx()).IsBlocked);

    [Fact]
    public void Git_Checkout_DirtyTree_BlocksBranchSwitch()
    {
        var block = P("git", "checkout")
            .Evaluate(["feature"], Ctx(repo: new FakeRepoProbe { IsCleanTree = false })).Block;
        Assert.NotNull(block);
        Assert.Contains("uncommitted changes", block.Reason);
    }

    [Fact]
    public void Git_Checkout_CleanTree_AllowsBranchSwitch()
        => Assert.False(P("git", "checkout").Evaluate(["feature"], Ctx()).IsBlocked);

    [Theory]
    [InlineData(".")]
    [InlineData("*")]
    public void Git_CheckoutFile_BlocksWildcards(string arg)
        => Assert.True(P("git", "checkout-file").Evaluate([arg], Ctx()).IsBlocked);

    [Theory]
    [InlineData("pull")]
    [InlineData("merge")]
    public void Git_PullMerge_DirtyTree_Blocks(string cmd)
        => Assert.True(P("git", cmd).Evaluate([], Ctx(repo: new FakeRepoProbe { IsCleanTree = false })).IsBlocked);

    [Theory]
    [InlineData("pull")]
    [InlineData("merge")]
    public void Git_PullMerge_CleanTree_Allows(string cmd)
        => Assert.False(P("git", cmd).Evaluate([], Ctx()).IsBlocked);

    [Fact]
    public void Git_CommitAmend_HeadPushed_Blocks()
    {
        var block = P("git", "commit-amend")
            .Evaluate([], Ctx(repo: new FakeRepoProbe { IsHeadPushed = true })).Block;
        Assert.NotNull(block);
        Assert.Contains("already been pushed", block.Reason);
    }

    [Fact]
    public void Git_CommitAmend_DefaultState_Allows()
        => Assert.False(P("git", "commit-amend").Evaluate([], Ctx()).IsBlocked);

    [Fact]
    public void Git_RequireGitRepo_NotARepo_Blocks()
    {
        var block = P("git", "status")
            .Evaluate([], Ctx(repo: new FakeRepoProbe { IsGitRepo = false })).Block;
        Assert.NotNull(block);
        Assert.Equal("Not a git repository", block.Reason);
        Assert.Null(block.Suggestion);
    }

    [Fact]
    public void Git_Log_Rewrite_DropsUnknownFlag_KeepsPositional()
    {
        var decision = P("git", "log").Evaluate(["--oneline", "--evil", "HEAD"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "--oneline", "HEAD" }, decision.SafeArgs);
    }

    [Fact]
    public void Git_Log_Rewrite_KeepsValueFlagAndValue()
    {
        var decision = P("git", "log").Evaluate(["-n", "5"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "-n", "5" }, decision.SafeArgs);
    }

    [Fact]
    public void Git_Diff_Rewrite_DropsUnknownFlag()
    {
        var decision = P("git", "diff").Evaluate(["--staged", "--bogus"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "--staged" }, decision.SafeArgs);
    }

    // ============================================================ db

    [Theory]
    [InlineData("prisma-migrate-dev")]
    [InlineData("prisma-migrate-deploy")]
    [InlineData("ef-database-update")]
    [InlineData("drizzle-migrate")]
    public void Db_Migrate_BlocksDestructiveFlags(string cmd)
    {
        var policy = P("db", cmd);
        Assert.True(policy.Evaluate(["--force"], Ctx()).IsBlocked);
        Assert.True(policy.Evaluate(["--force=true"], Ctx()).IsBlocked);          // DEFECT #1
        Assert.True(policy.Evaluate(["--accept-data-loss"], Ctx()).IsBlocked);
        Assert.True(policy.Evaluate(["--force-reset"], Ctx()).IsBlocked);
    }

    [Fact]
    public void Db_PrismaMigrateDev_AllowsCleanName()
        => Assert.False(P("db", "prisma-migrate-dev").Evaluate(["--name", "x"], Ctx()).IsBlocked);

    [Theory]
    [InlineData("prisma-migrate-deploy")]
    [InlineData("ef-database-update")]
    [InlineData("drizzle-migrate")]
    public void Db_Migrate_AllowsEmptyArgs(string cmd)
        => Assert.False(P("db", cmd).Evaluate([], Ctx()).IsBlocked);

    [Theory]
    [InlineData("migrate:fresh")]
    [InlineData("fresh")]
    [InlineData("reset")]
    [InlineData("rollback")]
    [InlineData("wipe")]
    public void Db_ArtisanMigrate_BlocksDestructiveSubcommands(string arg)
        => Assert.True(P("db", "artisan-migrate").Evaluate([arg], Ctx()).IsBlocked);

    [Fact]
    public void Db_ArtisanMigrate_AllowsStep()
        => Assert.False(P("db", "artisan-migrate").Evaluate(["--step"], Ctx()).IsBlocked);

    [Fact]
    public void Db_DjangoMigrate_BlocksZero()
        => Assert.True(P("db", "django-migrate").Evaluate(["zero"], Ctx()).IsBlocked);

    [Fact]
    public void Db_DjangoMigrate_AllowsApp()
        => Assert.False(P("db", "django-migrate").Evaluate(["myapp"], Ctx()).IsBlocked);

    [Fact]
    public void Db_DjangoMigrate_DoesNotMatchZeroSubstring()
    {
        // Exact-token (BlockFlags via Flag.Base), not substring: "zerotech" must NOT block.
        Assert.False(P("db", "django-migrate").Evaluate(["zerotech"], Ctx()).IsBlocked);
    }

    // ============================================================ docker

    [Theory]
    [InlineData("-v")]
    [InlineData("--volumes")]
    [InlineData("--rmi")]
    public void Docker_ComposeDown_BlocksVolumeAndImageRemoval(string flag)
        => Assert.True(P("docker", "compose-down").Evaluate([flag], Ctx()).IsBlocked);

    [Fact]
    public void Docker_ComposeDown_AllowsEmptyArgs()
        => Assert.False(P("docker", "compose-down").Evaluate([], Ctx()).IsBlocked);

    [Fact]
    public void Docker_Build_Rewrite_KeepsValueFlag_DropsUnknown()
    {
        var decision = P("docker", "build").Evaluate(["-t", "img", "--evil"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "-t", "img" }, decision.SafeArgs);
    }

    [Fact]
    public void Docker_Build_Rewrite_KeepsNoCache()
    {
        var decision = P("docker", "build").Evaluate(["--no-cache"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "--no-cache" }, decision.SafeArgs);
    }

    [Fact]
    public void Docker_ComposeUp_Rewrite_DropsUnknown()
    {
        var decision = P("docker", "compose-up").Evaluate(["-d", "--evil"], Ctx());
        Assert.False(decision.IsBlocked);
        Assert.Equal(new[] { "-d" }, decision.SafeArgs);
    }

    // ============================================================ process

    [Fact]
    public void Process_KillName_AllowsDevTool()
        => Assert.False(P("process", "kill-name").Evaluate(["node"], Ctx()).IsBlocked);

    [Fact]
    public void Process_KillName_BlocksDisallowedProcess()
    {
        var block = P("process", "kill-name").Evaluate(["rm"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("Process 'rm' is not in the allowed list", block.Reason);
    }

    // ============================================================ npm / pnpm

    [Fact]
    public void Npm_Run_AllowsKnownScript()
        => Assert.False(P("npm", "run").Evaluate(["build"], Ctx()).IsBlocked);

    [Fact]
    public void Npm_Run_BlocksUnknownScript()
    {
        var block = P("npm", "run").Evaluate(["evil"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Contains("not in the allowed list", block.Reason);
    }

    [Fact]
    public void Npm_AuditFix_BlocksForce()
        => Assert.True(P("npm", "audit-fix").Evaluate(["--force"], Ctx()).IsBlocked);

    [Fact]
    public void Npm_AuditFix_BlocksForceEqualsTrue_Defect1()
        => Assert.True(P("npm", "audit-fix").Evaluate(["--force=true"], Ctx()).IsBlocked);

    [Fact]
    public void Pnpm_Run_AllowsKnownScript()
        => Assert.False(P("pnpm", "run").Evaluate(["build"], Ctx()).IsBlocked);

    [Fact]
    public void Pnpm_Run_BlocksUnknownScript()
        => Assert.True(P("pnpm", "run").Evaluate(["evil"], Ctx()).IsBlocked);

    // ----------------------------------------- shared allowlist (npm/pnpm/bun run)

    [Theory]
    [InlineData("npm")]
    [InlineData("pnpm")]
    [InlineData("bun")]
    public void Run_SharesSingleScriptAllowlist(string group)
    {
        var policy = P(group, "run");
        Assert.False(policy.Evaluate(["storybook"], Ctx()).IsBlocked);   // same accepted script
        Assert.True(policy.Evaluate(["definitely-not-a-script"], Ctx()).IsBlocked);
    }

    // ============================================================ dispatch-level (blocked path)

    [Fact]
    public void Dispatch_GitPushForce_Blocked_NeverSpawns()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("git", "push");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace());

        var rc = CommandDispatcher.Execute(cmd, ports, "git", "push", ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var block = Assert.Single(render.Blocks);
        Assert.Equal("git push --force", block.Command);
        Assert.Equal("Force push and delete are not allowed", block.Reason);
    }

    [Fact]
    public void Dispatch_GitPushForce_UnderJsonMode_EmitsBlockedJson_Defect3()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("git", "push");
        Assert.NotNull(cmd);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var render = new ConsoleRenderer(jsonMode: true, stdout, stderr);
        var exec = new FakeExecutor();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace());

        var rc = CommandDispatcher.Execute(cmd, ports, "git", "push", ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal("git push --force", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("Force push and delete are not allowed", doc.RootElement.GetProperty("reason").GetString());
    }
}
