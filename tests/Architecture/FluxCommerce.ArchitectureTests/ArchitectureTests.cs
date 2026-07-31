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
