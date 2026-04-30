using SafeCommands.Commands;
using SafeCommands.Infrastructure.Ports;
using SafeCommands.Tests.Fakes;

namespace SafeCommands.Tests;

public class DbCommandsTests
{
    private static (Ports ports, FakeExecutor exec, FakeRenderer render) Setup()
    {
        var exec = new FakeExecutor();
        var render = new FakeRenderer();
        return (new Ports(exec, render), exec, render);
    }

    // ---- Destructive flag block (Prisma, Drizzle, EF) ----

    [Theory]
    [InlineData("--force")]
    [InlineData("-f")]
    [InlineData("--force-reset")]
    [InlineData("--accept-data-loss")]
    [InlineData("--skip-seed")]
    [InlineData("--skip-generate")]
    public void PrismaMigrateDev_DestructiveFlag_IsBlocked(string flag)
    {
        var (ports, exec, render) = Setup();

        var rc = DbCommands.RunPrismaMigrateDev(ports, [flag, "--name", "x"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("drops tables/data", render.Blocks[0].Reason);
    }

    [Fact]
    public void PrismaMigrateDev_RequiresName()
    {
        var (ports, exec, render) = Setup();

        var rc = DbCommands.RunPrismaMigrateDev(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("--name", render.Errors[0]);
    }

    [Fact]
    public void PrismaMigrateDev_WithName_Spawns()
    {
        var (ports, exec, _) = Setup();

        var rc = DbCommands.RunPrismaMigrateDev(ports, ["--name", "init"]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("npx", call.Tool);
        Assert.Equal(new[] { "prisma", "migrate", "dev", "--name", "init" }, call.Args);
    }

    [Fact]
    public void PrismaMigrateDeploy_DestructiveFlag_IsBlocked()
    {
        var (ports, exec, _) = Setup();

        var rc = DbCommands.RunPrismaMigrateDeploy(ports, ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void DrizzleMigrate_DestructiveFlag_IsBlocked()
    {
        var (ports, exec, _) = Setup();

        var rc = DbCommands.RunDrizzleMigrate(ports, ["--force-reset"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
    }

    [Fact]
    public void EfDatabaseUpdate_DestructiveFlag_IsBlocked()
    {
        var (ports, exec, _) = Setup();

        var rc = DbCommands.RunEfDatabaseUpdate(ports, ["--force"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
    }

    // ---- Artisan substring block ----

    [Theory]
    [InlineData("migrate:fresh")]
    [InlineData("migrate:reset")]
    [InlineData("migrate:rollback")]
    [InlineData("db:wipe")]
    public void ArtisanMigrate_DestructiveSubcommand_IsBlocked(string arg)
    {
        var (ports, exec, render) = Setup();

        var rc = DbCommands.RunArtisanMigrate(ports, [arg]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("drop tables", render.Blocks[0].Reason);
    }

    [Fact]
    public void ArtisanMigrate_CleanArgs_Spawns()
    {
        var (ports, exec, _) = Setup();

        var rc = DbCommands.RunArtisanMigrate(ports, ["--step", "1"]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("php", call.Tool);
        Assert.Equal(new[] { "artisan", "migrate", "--step", "1" }, call.Args);
    }

    // ---- Django zero block ----

    [Fact]
    public void DjangoMigrate_Zero_IsBlocked()
    {
        var (ports, exec, render) = Setup();

        var rc = DbCommands.RunDjangoMigrate(ports, ["myapp", "zero"]);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Contains("drops all tables", render.Blocks[0].Reason);
    }

    [Fact]
    public void DjangoMigrate_NormalArgs_Spawns()
    {
        var (ports, exec, _) = Setup();

        var rc = DbCommands.RunDjangoMigrate(ports, ["myapp", "0042"]);

        Assert.Equal(0, rc);
        var call = Assert.Single(exec.Calls);
        Assert.Equal("python", call.Tool);
        Assert.Equal(new[] { "manage.py", "migrate", "myapp", "0042" }, call.Args);
    }

    // ---- EF migrations-add usage ----

    [Fact]
    public void EfMigrationsAdd_RequiresName()
    {
        var (ports, exec, render) = Setup();

        var rc = DbCommands.RunEfMigrationsAdd(ports, []);

        Assert.Equal(1, rc);
        Assert.Empty(exec.Calls);
        Assert.Single(render.Errors);
    }
}
