using System.Text.Json;
using SafeCommands.Commands;
using SafeCommands.Infrastructure.Adapters;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Registry;
using SafeCommands.Safety;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

/// <summary>
/// Pins the policies wired onto the command groups (git, db, docker, process, npm, pnpm)
/// whose inline validation was migrated to declared Policy chains.
///
/// SPAWN HAZARD (historical — now fully closed): all command groups route their side effects through
/// ports. Tool spawns go through ports.Exec (a FakeExecutor absorbs them) and process enumeration/kill
/// goes through ports.Processes (a FakeProcessHost absorbs them — process was the last group on raw
/// System.Diagnostics.Process and is now migrated). So an ALLOWED input pushed through
/// CommandDispatcher.Execute no longer touches a real tool or process — the dispatch-level allow tests
/// at the end exercise exactly that, including process kill-name (the kill lands in the FakeProcessHost,
/// never a real Process). Policy-direct assertions (Find(...)!.Policy.Evaluate) are still used where the
/// pure verdict is the thing under test, but they are no longer forced by a spawn hazard.
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
    [InlineData("-b")] // create + switch carries uncommitted changes onto the new branch
    [InlineData("-B")] // create-or-reset; case-folds to -b and likewise preserves the working tree
    public void Git_Checkout_DirtyTree_AllowsBranchCreate(string flag)
        => Assert.False(P("git", "checkout")
            .Evaluate([flag, "feature"], Ctx(repo: new FakeRepoProbe { IsCleanTree = false })).IsBlocked);

    [Fact]
    public void Git_Checkout_BranchCreate_StillBlocksDot()
        => Assert.True(P("git", "checkout").Evaluate(["-b", "."], Ctx()).IsBlocked);

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

    // ============================================================ file (path containment)

    // A FakeWorkspace with identity Resolve and the default within-predicate
    // (p == /proj || p.StartsWith("/proj/")) — pins the wired policy without filesystem dependence.
    private static SafetyContext FileCtx() => Ctx(ws: new FakeWorkspace { ProjectRoot = "/proj" });

    // --- Single-positional, one per safety level (read-only / safe-write / checked-write) ---

    [Theory]
    [InlineData("read")]      // ReadOnly
    [InlineData("mkdir")]     // SafeWrite
    [InlineData("delete-tracked")] // CheckedWrite
    public void File_SinglePositional_InsideRoot_Allowed(string cmd)
        => Assert.False(P("file", cmd).Evaluate(["/proj/src/x.cs"], FileCtx()).IsBlocked);

    [Theory]
    [InlineData("read")]
    [InlineData("mkdir")]
    [InlineData("delete-tracked")]
    public void File_SinglePositional_OutsideRoot_Blocked(string cmd)
    {
        var block = P("file", cmd).Evaluate(["/etc/passwd"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    // --- Absent path → Allow (handler's "." default / usage error takes over) ---

    [Fact]
    public void File_List_NoPositional_Allowed()
        => Assert.False(P("file", "list").Evaluate([], FileCtx()).IsBlocked);

    [Fact]
    public void File_DeleteTemp_NoPositional_Allowed()
        => Assert.False(P("file", "delete-temp").Evaluate([], FileCtx()).IsBlocked);

    // --- find: --in flag value ---

    [Fact]
    public void File_Find_InsideRoot_Allowed()
        => Assert.False(P("file", "find").Evaluate(["*.cs", "--in", "/proj/src"], FileCtx()).IsBlocked);

    [Fact]
    public void File_Find_OutsideRoot_Blocked()
        => Assert.True(P("file", "find").Evaluate(["*.cs", "--in", "/etc"], FileCtx()).IsBlocked);

    [Fact]
    public void File_Find_NoInFlag_Allowed()
        // No --in → FlagValue.Extract returns null → Allow (handler defaults to ".").
        => Assert.False(P("file", "find").Evaluate(["*.cs"], FileCtx()).IsBlocked);

    // --- copy / move: two-path chain, short-circuit on first offending path ---

    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    public void File_TwoPath_BothInside_Allowed(string cmd)
        => Assert.False(P("file", cmd).Evaluate(["/proj/a", "/proj/b"], FileCtx()).IsBlocked);

    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    public void File_TwoPath_SourceOutside_Blocked(string cmd)
    {
        var block = P("file", cmd).Evaluate(["/etc/x", "/proj/b"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("/etc/x", block.Reason);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("move")]
    public void File_TwoPath_DestinationOutside_Blocked(string cmd)
    {
        var block = P("file", cmd).Evaluate(["/proj/a", "/etc/b"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("/etc/b", block.Reason);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    // --- delete-pattern: within-project THEN safe-dir chain ---

    [Fact]
    public void File_DeletePattern_SafeDirSegment_Allowed()
        => Assert.False(P("file", "delete-pattern").Evaluate(["*.log", "--in", "/proj/bin"], FileCtx()).IsBlocked);

    [Fact]
    public void File_DeletePattern_SafeAncestorSegment_Allowed()
        // Ancestor "tmp" is a safe dir — proves the segment-by-segment ancestor walk.
        => Assert.False(P("file", "delete-pattern").Evaluate(["*.log", "--in", "/proj/tmp/.dotnet"], FileCtx()).IsBlocked);

    [Fact]
    public void File_DeletePattern_WithinButNotSafeDir_Blocked()
    {
        var block = P("file", "delete-pattern").Evaluate(["*.log", "--in", "/proj/src"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("is not inside a safe delete directory", block.Reason);
    }

    [Fact]
    public void File_DeletePattern_OutsideProject_BlocksWithWithinReasonFirst()
    {
        // The within rule is chained FIRST, so it fires before the safe-dir rule for an outside path.
        var block = P("file", "delete-pattern").Evaluate(["*.log", "--in", "/etc/evil"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    [Fact]
    public void File_DeletePattern_ProjectRootItself_Blocked()
    {
        // The root is within the project but is not itself a safe delete dir.
        var block = P("file", "delete-pattern").Evaluate(["*.log", "--in", "/proj"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("is not inside a safe delete directory", block.Reason);
    }

    // ============================================================ generate hash-file (Positional w/ --algorithm skip)

    [Fact]
    public void Generate_HashFile_InsideRoot_Allowed()
        => Assert.False(P("generate", "hash-file").Evaluate(["/proj/file"], FileCtx()).IsBlocked);

    [Fact]
    public void Generate_HashFile_OutsideRoot_Blocked()
        => Assert.True(P("generate", "hash-file").Evaluate(["/etc/passwd"], FileCtx()).IsBlocked);

    [Fact]
    public void Generate_HashFile_AlgorithmBeforePath_SkipsValue_BlocksOutsidePath()
    {
        // SECURITY: the path after the skipped --algorithm value is the one validated.
        var block = P("generate", "hash-file").Evaluate(["--algorithm", "sha256", "/etc/passwd"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    [Fact]
    public void Generate_HashFile_AlgorithmFlagCaseInsensitive_SkipsValue_BlocksOutsidePath()
    {
        // SECURITY HOLE GUARD: value-flag skip is case-insensitive. If "--ALGORITHM" is NOT
        // recognized, "sha256" becomes the validated positional (inside-by-coincidence) and
        // "/etc/passwd" slips through. This must BLOCK.
        var block = P("generate", "hash-file").Evaluate(["--ALGORITHM", "sha256", "/etc/passwd"], FileCtx()).Block;
        Assert.NotNull(block);
        Assert.Contains("is outside the project directory", block.Reason);
    }

    [Fact]
    public void Generate_HashFile_PathThenAlgorithm_Allowed()
        => Assert.False(P("generate", "hash-file").Evaluate(["/proj/file", "--algorithm", "sha256"], FileCtx()).IsBlocked);

    [Fact]
    public void Generate_HashFile_PolicyAndHandler_ShareTheSamePathSelector_NoDecoyBypass()
        // B1 regression: the policy and RunHashFile BOTH extract the path via the one shared
        // GenerateCommands.HashFilePath, so the decoy "sha256 <outside> --algorithm sha256" resolves
        // to the leading positional ("sha256") for BOTH — the handler can never hash a token the
        // policy did not validate. (A fake-workspace Evaluate can't show this: identity Resolve makes
        // "sha256" look outside /proj, the opposite of production — hence this selector-level guard.)
        => Assert.Equal("sha256",
            GenerateCommands.HashFilePath.Extract(["sha256", "/etc/passwd", "--algorithm", "sha256"]));

    // ============================================================ proxy (declared per-tool policies, 3c)

    // SPAWN HAZARD: an ALLOWED proxy input pushed through CommandDispatcher.Execute would reach
    // RunTool -> ProcessRunner.CommandExists (a real PATH probe, flaky in CI) and Run.Tool.
    // So every allow case here asserts on the policy DIRECTLY via P("proxy", tool).Evaluate.
    // BLOCKED cases block in the policy before the handler, so they are deterministic.

    // --- Defect #2 closed: the per-subcommand flag allowlist is now ENFORCED (was dead before) ---

    [Theory]
    [InlineData("-X")]
    [InlineData("--method")]
    [InlineData("-H")]
    [InlineData("--header")]
    public void Proxy_GhApi_BlocksWriteMethodOverride(string flag)
        // -X/--method are omitted from api's allowlist on purpose: gh api confines writes to
        // POST-via-fields (create), so a method override that could DELETE/PUT/PATCH must block.
        // -H/--header is also blocked so the confinement can't be bypassed via a method-override
        // header (e.g. X-HTTP-Method-Override: DELETE).
        => Assert.True(P("proxy", "gh").Evaluate(["api", flag, "DELETE", "repos/o/r/issues/1"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_GhApi_AllowsReadAndCreateFlags()
        // The blocked-by-da09144 case: field params + output filtering on a real issue-create call.
        => Assert.False(P("proxy", "gh")
            .Evaluate(["api", "repos/o/r/issues", "-f", "title=t", "-F", "body=b", "--jq", ".html_url"], Ctx())
            .IsBlocked);

    [Fact]
    public void Proxy_TerraformPlan_BlocksAutoApprove_Defect2()
        // -auto-approve is not in plan's allowed flags — flag enforcement now rejects it.
        => Assert.True(P("proxy", "terraform").Evaluate(["plan", "-auto-approve"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_GhPrList_BlocksWebFlag_Defect2()
        // --web is allowed under "pr view" but NOT under "pr list": flags are per-subcommand.
        => Assert.True(P("proxy", "gh").Evaluate(["pr", "list", "--web"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_FlagBlock_ReasonIsPerSubcommand()
    {
        var block = P("proxy", "gh").Evaluate(["api", "-X", "POST"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Contains("not allowed for this subcommand", block.Reason);
    }

    // --- Allowed inputs still pass (policy-direct, no spawn) ---

    [Fact]
    public void Proxy_GhPrList_AllowsState()
        => Assert.False(P("proxy", "gh").Evaluate(["pr", "list", "--state", "open"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_GhPrView_AllowsJson()
        => Assert.False(P("proxy", "gh").Evaluate(["pr", "view", "--json"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_TerraformPlan_AllowsVar()
        => Assert.False(P("proxy", "terraform").Evaluate(["plan", "-var", "foo=bar"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_KubectlGet_AllowsNamespace()
        => Assert.False(P("proxy", "kubectl").Evaluate(["get", "--namespace", "kube-system"], Ctx()).IsBlocked);

    // --- curl method block (curl-specific message, prepended before the subcommand rule) ---

    [Theory]
    [InlineData("-X", "POST")]
    [InlineData("-d", "data")]
    public void Proxy_Curl_BlocksWriteMethod(string flag, string val)
    {
        var block = P("proxy", "curl").Evaluate([flag, val, "https://x"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Contains("GET/HEAD", block.Reason);
    }

    [Fact]
    public void Proxy_Curl_BlocksDataRawWithEqualsValue()
        // Flag.Base normalizes "--data-raw=x" to "--data-raw", which is a blocked write flag.
        => Assert.True(P("proxy", "curl").Evaluate(["--data-raw=x", "https://y"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_Curl_AllowsSilentGet()
        => Assert.False(P("proxy", "curl").Evaluate(["-s", "https://x"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_Curl_AllowsHeader()
        => Assert.False(P("proxy", "curl").Evaluate(["https://x", "-H", "Accept: text"], Ctx()).IsBlocked);

    // --- Subcommand gate still holds (prefix-level) ---

    [Theory]
    [InlineData("destroy")]
    [InlineData("apply")]
    public void Proxy_Terraform_BlocksDangerousSubcommands(string sub)
        => Assert.True(P("proxy", "terraform").Evaluate([sub], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_Gh_BlocksRepoDelete()
        => Assert.True(P("proxy", "gh").Evaluate(["repo", "delete"], Ctx()).IsBlocked);

    [Fact]
    public void Proxy_Gh_SubcommandBlock_ReasonNamesAllowed()
    {
        var block = P("proxy", "gh").Evaluate(["repo", "delete"], Ctx()).Block;
        Assert.NotNull(block);
        Assert.Equal("Subcommand is not allowed", block.Reason);
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
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

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
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = CommandDispatcher.Execute(cmd, ports, "git", "push", ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal("git push --force", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("Force push and delete are not allowed", doc.RootElement.GetProperty("reason").GetString());
    }

    // ============================================================ dispatch-level (allow path, post-migration)

    // These prove the docker/npm migration onto ports.Exec: an ALLOWED input pushed through the
    // dispatcher is absorbed by FakeExecutor rather than spawning a real process — the assertion the
    // SPAWN HAZARD note says was impossible while these handlers called the static ProcessRunner.

    [Fact]
    public void Dispatch_DockerPs_Allowed_RoutesThroughExecutor_NoRealSpawn()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("docker", "ps");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = CommandDispatcher.Execute(cmd, ports, "docker", "ps", []);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("docker", call.Tool);
        Assert.Equal(new[] { "ps" }, call.Args);
    }

    [Fact]
    public void Dispatch_NpmRunAllowedScript_RoutesThroughExecutor_NoRealSpawn()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("npm", "run");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = CommandDispatcher.Execute(cmd, ports, "npm", "run", ["build"]);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("npm", call.Tool);
        Assert.Equal(new[] { "run", "build" }, call.Args);
    }

    [Fact]
    public void Dispatch_DbPrismaStatus_Allowed_RoutesThroughExecutor_NoRealSpawn()
    {
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("db", "prisma-status");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = CommandDispatcher.Execute(cmd, ports, "db", "prisma-status", []);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("npx", call.Tool);
        Assert.Equal(new[] { "prisma", "migrate", "status" }, call.Args);
    }

    [Fact]
    public void Dispatch_DbMigrateDev_DestructiveFlag_Blocked_NeverSpawns()
    {
        // The destructive-flag block is enforced at dispatch BEFORE the handler runs, so a blocked
        // db migrate never reaches the (now ports-backed) executor.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("db", "prisma-migrate-dev");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = CommandDispatcher.Execute(cmd, ports, "db", "prisma-migrate-dev", ["--name", "x", "--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Blocks);
    }

    [Fact]
    public void Dispatch_GitStatus_Allowed_RoutesThroughExecutor_NoRealSpawn()
    {
        // Post-migration, an allowed git command is absorbed by FakeExecutor instead of spawning real
        // git — the assertion the SPAWN HAZARD note said was impossible while git used ProcessRunner.
        // (RequireGitRepo passes here because FakeRepoProbe reports IsGitRepo=true.)
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("git", "status");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), new FakeProcessHost());

        var rc = CommandDispatcher.Execute(cmd, ports, "git", "status", []);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("git", call.Tool);
        Assert.Equal(new[] { "status" }, call.Args);
    }

    [Fact]
    public void Dispatch_FileDeleteTracked_PathAllowed_RoutesProbeThroughExecutor_NoRealSpawn()
    {
        // file's only spawns are the delete-tracked/move git probes. Post-migration they go through
        // ports.Exec, so a FakeExecutor absorbs them. The path policy ALLOWS the file (WithinPredicate
        // = true), so dispatch reaches the handler; the handler's "not tracked" block (FakeExecutor
        // returns exit 1) is distinct from a policy "outside project" block, proving the policy passed
        // AND the git probe ran through the executor rather than spawning real git.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("file", "delete-tracked");
        Assert.NotNull(cmd);

        var tmp = Path.Combine(Path.GetTempPath(), "safecmd-dispatch-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(tmp, "x");
        try
        {
            var exec = new FakeExecutor { NextResult = new ExecResult(1, "", "") }; // git ls-files -> not tracked
            var render = new FakeRenderer();
            var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace { WithinPredicate = _ => true }, new FakeProcessHost());

            var rc = CommandDispatcher.Execute(cmd, ports, "file", "delete-tracked", [tmp]);

            Assert.Equal(1, rc);
            var probe = Assert.Single(exec.Calls);
            Assert.Equal("git", probe.Tool);
            Assert.Equal(new[] { "ls-files", "--error-unmatch", tmp }, probe.Args);
            Assert.Contains("not tracked", Assert.Single(render.Blocks).Reason);
            Assert.True(File.Exists(tmp)); // never deleted
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Dispatch_ProcessKillName_AllowedName_RoutesKillThroughHost_NoRealProcess()
    {
        // Post-migration, process kill goes through ports.Processes. An allowlisted name ("node")
        // passes the policy, reaches the handler, and the kill lands in a FakeProcessHost — the
        // assertion the SPAWN HAZARD note said was impossible while process used raw Process.Kill.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("process", "kill-name");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var host = new FakeProcessHost();
        host.Table.Add(new ProcessInfo(999, "node", 0));
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), host);

        var rc = CommandDispatcher.Execute(cmd, ports, "process", "kill-name", ["node"]);

        Assert.Equal(0, rc);
        Assert.Empty(render.Blocks);            // "node" is allowlisted
        Assert.Contains(999, host.KillCalls);   // killed through the host, never a real Process
        Assert.Empty(exec.Calls);               // kill-name spawns no external tool
    }

    [Fact]
    public void Dispatch_ProcessKillName_DisallowedName_Blocked_NeverTouchesHost()
    {
        // E2: a non-allowlisted name is blocked at the dispatch seam before the handler runs, so the
        // process host is never invoked — the kill can't reach even a faked process table.
        CommandRegistry.Initialize();
        var cmd = CommandRegistry.Find("process", "kill-name");
        Assert.NotNull(cmd);
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        var host = new FakeProcessHost();
        host.Table.Add(new ProcessInfo(1, "rm", 0));
        var ports = new Ports(exec, render, new FakeRepoProbe(), new FakeWorkspace(), host);

        var rc = CommandDispatcher.Execute(cmd, ports, "process", "kill-name", ["rm"]);

        Assert.Equal(1, rc);
        Assert.Single(render.Blocks);
        Assert.Empty(host.KillCalls);           // blocked before the handler -> host untouched
    }
}
