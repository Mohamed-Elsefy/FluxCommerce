using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_ShouldNot_ReferenceInfrastructure()
    {
        AssertNoDependencies(
            ArchitectureRegistry.DomainAssemblies,
            ArchitectureRegistry.InfrastructureNamespaces,
            "Domain -> Infrastructure");
    }

    [Fact]
    public void Application_ShouldNot_ReferenceApi()
    {
        AssertNoDependencies(
            ArchitectureRegistry.ApplicationAssemblies,
            [ArchitectureRegistry.Api.GetName().Name!],
            "Application -> API");
    }

    [Fact]
    public void BuildingBlocks_ShouldNot_ReferenceModules()
    {
        AssertNoDependencies(
            ArchitectureRegistry.BuildingBlocks,
            ArchitectureRegistry.ModuleNamespaces,
            "BuildingBlocks -> Modules");
    }

    [Fact]
    public void Modules_ShouldNot_ReferenceAnotherModulesInfrastructure()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames)
        {
            var moduleAssemblies = ArchitectureRegistry.ModuleAssemblies
                .Where(a => a.GetName().Name!.StartsWith($"FluxCommerce.Modules.{module}.", StringComparison.Ordinal))
                .ToList();

            AssertNoDependencies(
                moduleAssemblies,
                ArchitectureRegistry.OtherModulesInfrastructureNamespaces(module),
                $"Module '{module}' -> other modules' Infrastructure");
        }
    }

    [Fact]
    public void Domain_ShouldNot_ReferenceEfCore()
    {
        AssertNoDependencies(
            ArchitectureRegistry.DomainAssemblies,
            ArchitectureRegistry.EfCoreNamespaces,
            "Domain -> EF Core");
    }

    [Fact]
    public void Application_ShouldNot_ReferenceAspNetCore()
    {
        AssertNoDependencies(
            ArchitectureRegistry.ApplicationAssemblies,
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Application -> ASP.NET Core");
    }

    [Fact]
    public void ModuleLayers_ShouldNot_ReferenceBuildingBlocksWeb()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ModuleAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("FluxCommerce.BuildingBlocks.Web")
                .GetResult();

            if (result.IsSuccessful)
            {
                continue;
            }

            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Module layer -> BuildingBlocks.Web: {assembly.GetName().Name} via: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Controllers_Should_InheritApiControllerBase()
    {
        var result = Types.InAssembly(ArchitectureRegistry.Api)
            .That()
            .HaveNameEndingWith("Controller")
            .Should()
            .Inherit(typeof(FluxCommerce.BuildingBlocks.Web.Controllers.ApiControllerBase))
            .GetResult();

        var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
        Assert.True(result.IsSuccessful, $"All controllers must inherit ApiControllerBase: {string.Join(", ", failing)}");
    }

    [Fact]
    public void Controllers_ShouldNot_ReferenceAnyModuleInfrastructure()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames)
        {
            var result = Types.InAssembly(ArchitectureRegistry.Api)
                .That()
                .HaveNameEndingWith("Controller")
                .ShouldNot()
                .HaveDependencyOn($"FluxCommerce.Modules.{module}.Infrastructure")
                .GetResult();

            if (result.IsSuccessful)
            {
                continue;
            }

            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Controllers must remain thin (no '{module}.Infrastructure' reference): {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void AllModuleDbContexts_ShouldDeriveFromBaseDbContext()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ModuleAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("DbContext")
                .Should()
                .Inherit(typeof(FluxCommerce.BuildingBlocks.Persistence.DbContexts.BaseDbContext))
                .GetResult();

            if (result.IsSuccessful)
            {
                continue;
            }

            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Module DbContexts must derive from BaseDbContext: {assembly.GetName().Name} via: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void SqlBackedModules_ShouldOwnExactlyOneDbContext_CartHasNone()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames)
        {
            var moduleAssemblies = ArchitectureRegistry.ModuleAssemblies
                .Where(a => a.GetName().Name!.StartsWith($"FluxCommerce.Modules.{module}.", StringComparison.Ordinal))
                .ToList();

            var dbContextTypes = moduleAssemblies
                .SelectMany(a => Types.InAssembly(a).That().HaveNameEndingWith("DbContext").GetTypes())
                .ToArray();

            if (module == "Cart")
            {
                if (dbContextTypes.Length != 0)
                {
                    failures.Add($"Cart is Redis-backed and must own no DbContext; found: {string.Join(", ", dbContextTypes.Select(t => t.FullName))}");
                }

                continue;
            }

            if (dbContextTypes.Length != 1)
            {
                failures.Add($"SQL-backed module '{module}' must own exactly one DbContext; found: {string.Join(", ", dbContextTypes.Select(t => t.FullName))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void AssertNoDependencies(
        IReadOnlyList<System.Reflection.Assembly> assemblies,
        IReadOnlyList<string> forbiddenNamespaces,
        string rule)
    {
        var failures = new List<string>();

        foreach (var assembly in assemblies)
        {
            var types = Types.InAssembly(assembly);

            foreach (var ns in forbiddenNamespaces)
            {
                var result = types.ShouldNot().HaveDependencyOn(ns).GetResult();
                if (result.IsSuccessful)
                {
                    continue;
                }

                var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                failures.Add($"{rule}: {assembly.GetName().Name} depends on '{ns}' via: {string.Join(", ", failing)}");
            }
        }

        Assert.True(
            failures.Count == 0,
            string.Join(Environment.NewLine, failures));
    }
}
