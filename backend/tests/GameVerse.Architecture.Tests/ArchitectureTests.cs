using System.Reflection;
using GameVerse.Infrastructure;
using GameVerse.Platform;
using NetArchTest.Rules;

namespace GameVerse.Architecture.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Rules = typeof(GameVerse.Rules.Math.Fixed).Assembly;
    private static readonly Assembly Contracts = typeof(GameVerse.Contracts.Session.SessionManifest).Assembly;
    private static readonly Assembly Platform = typeof(PlatformModules).Assembly;
    private static readonly Assembly Infrastructure = typeof(InfrastructureServices).Assembly;

    [Fact]
    public void Rules_depends_on_nothing_outside_the_base_library()
    {
        var result = Types.InAssembly(Rules).ShouldNot().HaveDependencyOnAny(
            "GameVerse.Contracts", "GameVerse.Platform", "GameVerse.Infrastructure", "GameVerse.Api",
            "Microsoft", "UnityEngine", "Newtonsoft", "System.Text.Json").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Contracts_does_not_depend_on_backend_projects()
    {
        var result = Types.InAssembly(Contracts).ShouldNot().HaveDependencyOnAny(
            "GameVerse.Platform", "GameVerse.Infrastructure", "GameVerse.Api", "Microsoft.AspNetCore", "UnityEngine").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Platform_and_Infrastructure_do_not_depend_on_the_Api_host()
    {
        foreach (var assembly in new[] { Platform, Infrastructure })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("GameVerse.Api").GetResult();
            result.IsSuccessful.ShouldBeTrue(assembly.GetName().Name + ": " + Describe(result));
        }
    }

    [Fact]
    public void Platform_does_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(Platform).ShouldNot().HaveDependencyOn("GameVerse.Infrastructure").GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Platform_modules_do_not_use_each_others_internals()
    {
        ModuleBoundaries.FindViolations(Platform, "GameVerse.Platform", PlatformModules.All).ShouldBeEmpty();
    }

    [Fact]
    public void Only_Accounts_uses_the_Identity_user_type()
    {
        // AccountUser is public only because the shared Identity DbContext needs it (see its doc comment). Other modules
        // ask about accounts through IAccountDirectory.
        var result = Types.InAssembly(Platform)
            .That().DoNotResideInNamespace("GameVerse.Platform.Accounts")
            .ShouldNot().HaveDependencyOn(typeof(GameVerse.Platform.Accounts.AccountUser).FullName)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Boundary_check_catches_a_real_violation()
    {
        var violations = ModuleBoundaries.FindViolations(
            typeof(ArchitectureTests).Assembly,
            "GameVerse.Architecture.Tests.Samples",
            ["ModuleA", "ModuleB", "ModuleC"]);

        violations.ShouldHaveSingleItem().ShouldBe(
            "GameVerse.Architecture.Tests.Samples.ModuleA.ReachesIntoB uses internals of module ModuleB");
    }

    [Fact]
    public void Every_module_has_a_folder_with_a_README_and_there_are_no_strays()
    {
        var platformDir = Path.Combine(RepoRoot(), "backend", "src", "GameVerse.Platform");
        var folders = Directory.GetDirectories(platformDir)
            .Select(d => Path.GetFileName(d)!)
            .Where(n => n is not ("bin" or "obj" or "Properties"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        folders.ShouldBe(PlatformModules.All.OrderBy(n => n, StringComparer.Ordinal).ToList());
        foreach (var module in PlatformModules.All)
        {
            File.Exists(Path.Combine(platformDir, module, "README.md")).ShouldBeTrue(module + " has no README.md");
        }
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful ? string.Empty : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
