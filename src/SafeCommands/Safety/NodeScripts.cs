namespace SafeCommands.Safety;

/// <summary>
/// Shared allowlist of npm/pnpm/bun lifecycle and developer scripts that
/// <c>safe (npm|pnpm|bun) run &lt;script&gt;</c> will pass through. The same list
/// is consumed by <c>BunCommands</c>, <c>NpmCommands</c>, and <c>PnpmCommands</c>;
/// previously each maintained its own copy.
/// </summary>
static class NodeScripts
{
    public static readonly HashSet<string> AllowedScripts =
    [
        "build", "dev", "start", "test", "lint", "format",
        "typecheck", "check", "compile", "watch", "serve", "preview",
        "generate", "codegen", "migrate", "seed", "prisma",
        "storybook", "e2e", "cypress", "playwright",
        "clean", "prebuild", "postbuild",
    ];
}
