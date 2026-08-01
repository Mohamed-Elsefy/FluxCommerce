using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

public class IdentityArchitectureTests
{
    [Fact]
    public void Identity_ShouldNot_ReferenceOtherModules()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ModuleAssemblies.Where(a =>
                     a.GetName().Name!.StartsWith("FluxCommerce.Modules.Identity.", StringComparison.Ordinal)))
        {
            foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Identity"))
            {
                foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Contracts" })
                {
                    AssertNoDependencies(
                        assembly,
                        $"FluxCommerce.Modules.{module}.{layer}",
                        $"Identity -> {module}.{layer}",
                        failures);
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Identity_Domain_ShouldNot_ReferenceApplication()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            ArchitectureRegistry.DomainAssemblies.Where(a =>
                a.GetName().Name!.StartsWith("FluxCommerce.Modules.Identity.", StringComparison.Ordinal)),
            ["FluxCommerce.Modules.Identity.Application"],
            "Identity.Domain -> Identity.Application",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Identity_Application_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            ArchitectureRegistry.ApplicationAssemblies.Where(a =>
                a.GetName().Name!.StartsWith("FluxCommerce.Modules.Identity.", StringComparison.Ordinal)),
            ["FluxCommerce.Modules.Identity.Infrastructure"],
            "Identity.Application -> Identity.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Domain_ShouldNot_ReferenceApplicationLayers()
    {
        var failures = new List<string>();

        foreach (var domainAssembly in ArchitectureRegistry.DomainAssemblies)
        {
            foreach (var module in ArchitectureRegistry.ModuleNames)
            {
                AssertNoDependencies(
                    domainAssembly,
                    $"FluxCommerce.Modules.{module}.Application",
                    $"{domainAssembly.GetName().Name} -> {module}.Application",
                    failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void NoModule_ShouldReference_AnotherModulesDbContext()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames)
        {
            var otherAssemblies = ArchitectureRegistry.ModuleAssemblies
                .Where(a => !a.GetName().Name!.StartsWith($"FluxCommerce.Modules.{module}.", StringComparison.Ordinal))
                .ToList();

            AssertNoDependencies(
                otherAssemblies,
                [$"FluxCommerce.Modules.{module}.Infrastructure.DbContexts"],
                $"Other modules -> {module} DbContext",
                failures);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Identity_ShouldNot_ReferenceOtherModulesDbContexts()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Identity"))
        {
            foreach (var assembly in ArchitectureRegistry.ModuleAssemblies.Where(a =>
                         a.GetName().Name!.StartsWith("FluxCommerce.Modules.Identity.", StringComparison.Ordinal)))
            {
                AssertNoDependencies(
                    assembly,
                    $"FluxCommerce.Modules.{module}.Infrastructure.DbContexts",
                    $"Identity -> {module} DbContext",
                    failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void AssertNoDependencies(
        System.Reflection.Assembly assembly,
        string forbiddenNamespace,
        string rule,
        List<string> failures)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
        failures.Add($"{rule}: {assembly.GetName().Name} depends on '{forbiddenNamespace}' via: {string.Join(", ", failing)}");
    }

    private static void AssertNoDependencies(
        IEnumerable<System.Reflection.Assembly> assemblies,
        IReadOnlyList<string> forbiddenNamespaces,
        string rule,
        List<string> failures)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var ns in forbiddenNamespaces)
            {
                AssertNoDependencies(assembly, ns, rule, failures);
            }
        }
    }
}
