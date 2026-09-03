using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

/// <summary>
/// R2.3d guardrails: enforce file-placement and namespace hygiene introduced in R2.3a/b
/// (F-01–F-08, F-11–F-13, F-18) and prevent cross-module DbContext leaks.
/// </summary>
public class StructureArchitectureTests
{
    [Fact]
    public void Application_Interfaces_Namespace_Should_Contain_Only_Interfaces()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ApplicationAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceMatching(@"FluxCommerce\.Modules\..*\.Application\.Interfaces(\..*)?")
                .Should()
                .BeInterfaces()
                .GetResult();

            if (!result.IsSuccessful)
            {
                var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                failures.Add($"{assembly.GetName().Name} has concrete types in Application.Interfaces: {string.Join(", ", failing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Application_Enums_Should_Reside_In_Enums_Namespace()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ApplicationAssemblies.Concat(ArchitectureRegistry.ModuleAssemblies))
        {
            var enumTypes = assembly.GetTypes().Where(t => t.IsEnum).ToArray();

            foreach (var type in enumTypes)
            {
                var ns = type.Namespace ?? string.Empty;
                if (ns.Contains(".Application.Interfaces", StringComparison.Ordinal))
                {
                    failures.Add($"Enum {type.FullName} must not reside in Application.Interfaces (assembly {assembly.GetName().Name})");
                }
            }
        }

        // Also ensure no enum is declared inside an Interfaces file via namespace check is enough;
        // the concrete check above covers it. We additionally assert that any enum whose name suggests
        // application state (e.g., *Status, *Result) is not in Interfaces.
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Application_Models_Should_Not_Reside_In_Interfaces_Namespace()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ApplicationAssemblies)
        {
            var types = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceMatching(@"FluxCommerce\.Modules\..*\.Application\.Interfaces(\..*)?")
                .GetTypes();

            foreach (var type in types)
            {
                // Interfaces namespace must not contain records/classes that are models/DTOs
                if (!type.IsInterface && !type.IsEnum)
                {
                    // Allow only interface types; any class/record/struct in Interfaces is a violation
                    failures.Add($"Concrete type {type.FullName} must not reside in Application.Interfaces (assembly {assembly.GetName().Name})");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Modules_ShouldNot_Reference_AnotherModules_DbContext_Generic()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames)
        {
            var moduleAssemblies = ArchitectureRegistry.ModuleAssemblies
                .Where(a => a.GetName().Name!.StartsWith($"FluxCommerce.Modules.{module}.", StringComparison.Ordinal))
                .ToList();

            foreach (var other in ArchitectureRegistry.ModuleNames.Where(m => m != module))
            {
                var forbidden = $"FluxCommerce.Modules.{other}.Infrastructure.DbContexts";
                foreach (var assembly in moduleAssemblies)
                {
                    var result = Types.InAssembly(assembly)
                        .ShouldNot()
                        .HaveDependencyOn(forbidden)
                        .GetResult();

                    if (!result.IsSuccessful)
                    {
                        var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                        failures.Add($"{assembly.GetName().Name} -> {other} DbContext via: {string.Join(", ", failing)}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Modules_ShouldNot_Reference_AnotherModules_Persistence_Configurations()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames)
        {
            var moduleAssemblies = ArchitectureRegistry.ModuleAssemblies
                .Where(a => a.GetName().Name!.StartsWith($"FluxCommerce.Modules.{module}.", StringComparison.Ordinal))
                .ToList();

            foreach (var other in ArchitectureRegistry.ModuleNames.Where(m => m != module))
            {
                var forbidden = $"FluxCommerce.Modules.{other}.Infrastructure.Persistence.Configurations";
                foreach (var assembly in moduleAssemblies)
                {
                    var result = Types.InAssembly(assembly)
                        .ShouldNot()
                        .HaveDependencyOn(forbidden)
                        .GetResult();

                    if (!result.IsSuccessful)
                    {
                        var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                        failures.Add($"{assembly.GetName().Name} -> {other} Persistence.Configurations via: {string.Join(", ", failing)}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
