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

    [Fact]
    public void AdminController_ShouldRequire_SuperAdminPolicy()
    {
        var apiAssembly = ArchitectureRegistry.Api;
        var adminController = apiAssembly.GetTypes().FirstOrDefault(t => t.Name == "AdminController");

        Assert.NotNull(adminController);

        var classAuth = adminController!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(classAuth);
        Assert.Equal("SuperAdmin", classAuth!.Policy);

        var methods = adminController.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false).Any());

        Assert.True(methods.Any(), "AdminController should have POST actions");

        foreach (var method in methods)
        {
            var methodAuth = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .FirstOrDefault();

            // Class-level SuperAdmin covers all, but if method has its own it must also be SuperAdmin
            if (methodAuth != null)
            {
                Assert.Equal("SuperAdmin", methodAuth.Policy);
            }
        }
    }

    [Fact]
    public void RegisterAdmin_ShouldRequire_SuperAdminPolicy()
    {
        var apiAssembly = ArchitectureRegistry.Api;
        var authController = apiAssembly.GetTypes().FirstOrDefault(t => t.Name == "AuthController");

        Assert.NotNull(authController);

        var method = authController!.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "RegisterAdmin");

        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(authAttr);
        Assert.Equal("SuperAdmin", authAttr!.Policy);
    }

    [Fact]
    public void CatalogMutations_ShouldRequire_AdminPolicy()
    {
        var apiAssembly = ArchitectureRegistry.Api;
        var catalogControllers = apiAssembly.GetTypes()
            .Where(t => t.Namespace == "FluxCommerce.Api.Controllers.Catalog" && t.Name.EndsWith("Controller"))
            .ToList();

        Assert.True(catalogControllers.Any(), "Catalog controllers should exist");

        var failures = new List<string>();

        foreach (var controller in catalogControllers)
        {
            var methods = controller.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                var httpAttrs = method.GetCustomAttributes(false)
                    .Where(a => a.GetType().Name.StartsWith("HttpPost") || a.GetType().Name.StartsWith("HttpPut") || a.GetType().Name.StartsWith("HttpDelete") || a.GetType().Name.StartsWith("HttpPatch"))
                    .ToList();

                if (!httpAttrs.Any())
                    continue;

                // Allow anonymous reads are HttpGet with AllowAnonymous — skip those
                var allowAnon = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true).Any();
                if (allowAnon)
                    continue;

                var authorizeAttrs = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                    .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                    .ToList();

                // Must have Authorize with Admin policy either on method or class
                var hasAdminPolicy = authorizeAttrs.Any(a => a.Policy == "Admin");

                if (!hasAdminPolicy)
                {
                    var classAuth = controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                        .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                        .FirstOrDefault();

                    hasAdminPolicy = classAuth != null && classAuth.Policy == "Admin";
                }

                if (!hasAdminPolicy)
                {
                    failures.Add($"{controller.Name}.{method.Name} is a mutation but does not require AdminPolicy");
                }
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
