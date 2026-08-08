using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

public class CartArchitectureTests
{
    // ---- Module isolation ----

    [Fact]
    public void Cart_ShouldNot_ReferenceOtherModules()
    {
        var failures = new List<string>();

        foreach (var assembly in CartAssemblies)
        {
            foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Cart"))
            {
                foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Contracts" })
                {
                    // Phase 3.7 boundary: Cart.Application is allowed to depend on the approved
                    // cross-module Catalog contract (FluxCommerce.Modules.Catalog.Contracts) — the
                    // ONLY Catalog boundary Cart may cross. All other module references remain
                    // forbidden.
                    if (module == "Catalog"
                        && layer == "Contracts"
                        && assembly.GetName().Name == "FluxCommerce.Modules.Cart.Application")
                    {
                        continue;
                    }

                    AssertNoDependencies(
                        assembly,
                        $"FluxCommerce.Modules.{module}.{layer}",
                        $"Cart -> {module}.{layer}",
                        failures);
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_ShouldNot_ReferenceOtherModulesDbContexts()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Cart"))
        {
            foreach (var assembly in CartAssemblies)
            {
                AssertNoDependencies(
                    assembly,
                    $"FluxCommerce.Modules.{module}.Infrastructure.DbContexts",
                    $"Cart -> {module} DbContext",
                    failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_ShouldNot_ReferenceIdentityInfrastructure_OrJwtImplementation()
    {
        // Phase 3.5 boundary: Cart consumes the shared ICurrentUser abstraction only. It must not
        // reference the Identity infrastructure namespace (which owns IdentityDbContext, JwtProvider,
        // JwtOptions and CurrentUser) nor any JWT implementation type.
        var failures = new List<string>();

        foreach (var assembly in CartAssemblies)
        {
            var infra = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("FluxCommerce.Modules.Identity.Infrastructure")
                .GetResult();

            if (!infra.IsSuccessful)
            {
                var failing = infra.FailingTypeNames is null ? [] : infra.FailingTypeNames.ToArray();
                failures.Add($"Cart -> Identity.Infrastructure: {assembly.GetName().Name} via {string.Join(", ", failing)}");
            }

            foreach (var jwtType in new[] { "JwtProvider", "JwtOptions" })
            {
                var jwt = Types.InAssembly(assembly)
                    .ShouldNot()
                    .HaveDependencyOn(jwtType)
                    .GetResult();

                if (!jwt.IsSuccessful)
                {
                    var failing = jwt.FailingTypeNames is null ? [] : jwt.FailingTypeNames.ToArray();
                    failures.Add($"Cart -> {jwtType}: {assembly.GetName().Name} via {string.Join(", ", failing)}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        foreach (var assembly in CartAssemblies)
        {
            AssertNoDependencies(
                assembly,
                ArchitectureRegistry.EfCoreNamespaces,
                "Cart -> EF Core",
                failures);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Domain layer rules ----

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceApplication()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ["FluxCommerce.Modules.Cart.Application"],
            "Cart.Domain -> Cart.Application",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceContracts()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ["FluxCommerce.Modules.Cart.Contracts"],
            "Cart.Domain -> Cart.Contracts",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ["FluxCommerce.Modules.Cart.Infrastructure"],
            "Cart.Domain -> Cart.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ArchitectureRegistry.EfCoreNamespaces,
            "Cart.Domain -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Cart.Domain -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceLocalization()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ["FluxCommerce.BuildingBlocks.Localization"],
            "Cart.Domain -> Localization",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Cart.Domain -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceRedis()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            RedisNamespaces,
            "Cart.Domain -> Redis",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Application layer rules ----

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            ["FluxCommerce.Modules.Cart.Infrastructure"],
            "Cart.Application -> Cart.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Cart.Application -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            ArchitectureRegistry.EfCoreNamespaces,
            "Cart.Application -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Cart.Application -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Cart.Application -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceBuildingBlocksInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            ["FluxCommerce.BuildingBlocks.Infrastructure"],
            "Cart.Application -> BuildingBlocks.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceRedis()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            RedisNamespaces,
            "Cart.Application -> Redis",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_ICartStore_ResidesInApplication()
    {
        var storeType = Types.InAssembly(CartApplication)
            .That()
            .HaveName("ICartStore")
            .GetTypes()
            .SingleOrDefault();

        Assert.NotNull(storeType);
        Assert.Equal("FluxCommerce.Modules.Cart.Application.Interfaces", storeType!.Namespace);
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceRedisConnectionProviderTypes()
    {
        var failures = new List<string>();

        foreach (var providerType in new[] { "IRedisConnectionProvider", "RedisConnectionProvider" })
        {
            var result = Types.InAssembly(CartApplication)
                .ShouldNot()
                .HaveDependencyOn(providerType)
                .GetResult();

            if (!result.IsSuccessful)
            {
                var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                failures.Add($"Cart.Application depends on '{providerType}' via: {string.Join(", ", failing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_UsesTheSharedICurrentUser_Abstraction()
    {
        // The current authenticated identity must come from the existing BuildingBlocks
        // ICurrentUser abstraction. Cart.Application must reference the assembly that defines
        // it, must consume its namespace, and must never define a Cart-local duplicate.
        var definingAssembly = typeof(FluxCommerce.BuildingBlocks.Application.Abstractions.ICurrentUser).Assembly;

        Assert.Equal("FluxCommerce.BuildingBlocks.Application", definingAssembly.GetName().Name);

        var referenced = CartApplication.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Contains(definingAssembly.GetName().Name);

        Assert.True(referenced, "Cart.Application must reference the BuildingBlocks assembly that defines ICurrentUser.");

        var localDefinitions = Types.InAssembly(CartApplication)
            .That()
            .HaveName("ICurrentUser")
            .GetTypes()
            .ToArray();

        Assert.Empty(localDefinitions);

        var consumers = Types.InAssembly(CartApplication)
            .That()
            .HaveDependencyOn("FluxCommerce.BuildingBlocks.Application.Abstractions")
            .GetTypes()
            .ToArray();

        Assert.NotEmpty(consumers);
    }

    [Fact]
    public void Cart_AuthenticatedCommands_DoNotAcceptUserId_OrCartId_FromRequestData()
    {
        // Authenticated ownership must come from ICurrentUser, never from a client-supplied
        // UserId or CartId in the request payload.
        var types = Types.InAssembly(CartApplication)
            .That()
            .HaveNameEndingWith("AuthenticatedCartCommand")
            .Or()
            .HaveNameEndingWith("AuthenticatedCartQuery")
            .GetTypes();

        Assert.NotEmpty(types);

        var failures = new List<string>();
        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name is "UserId" or "CartId")
                {
                    failures.Add($"{type.FullName}.{property.Name} must not be accepted from request data.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Infrastructure layer rules ----

    [Fact]
    public void Cart_Infrastructure_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartInfrastructure],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Cart.Infrastructure -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Contracts layer rules ----

    [Fact]
    public void Cart_Contracts_ShouldNot_ReferenceModuleLayers()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartContracts],
            [
                "FluxCommerce.Modules.Cart.Domain",
                "FluxCommerce.Modules.Cart.Application",
                "FluxCommerce.Modules.Cart.Infrastructure",
            ],
            "Cart.Contracts -> Cart layers",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Contracts_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartContracts],
            ArchitectureRegistry.EfCoreNamespaces,
            "Cart.Contracts -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Contracts_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartContracts],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Cart.Contracts -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Contracts_ShouldNot_ReferenceRedis()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartContracts],
            RedisNamespaces,
            "Cart.Contracts -> Redis",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Redis key & serialization placement ----

    [Fact]
    public void Cart_RedisKeyGenerator_ResidesInInfrastructure()
    {
        var result = Types.InAssembly(CartInfrastructure)
            .That()
            .HaveName("CartKeyGenerator")
            .Should()
            .ResideInNamespace("FluxCommerce.Modules.Cart.Infrastructure.Redis")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Cart_StateSerializer_ResidesInInfrastructure()
    {
        var result = Types.InAssembly(CartInfrastructure)
            .That()
            .HaveName("CartStateSerializer")
            .Should()
            .ResideInNamespace("FluxCommerce.Modules.Cart.Infrastructure.Redis")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void Cart_Domain_ShouldNot_ReferenceCartKeyGenerator()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartDomain],
            ["FluxCommerce.Modules.Cart.Infrastructure.Redis"],
            "Cart.Domain -> Cart Redis implementation",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Application_ShouldNot_ReferenceCartRedisImplementation()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartApplication],
            ["FluxCommerce.Modules.Cart.Infrastructure.Redis"],
            "Cart.Application -> Cart Redis implementation",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Contracts_ShouldNot_ReferenceCartRedisImplementation()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CartContracts],
            ["FluxCommerce.Modules.Cart.Infrastructure.Redis"],
            "Cart.Contracts -> Cart Redis implementation",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- CQRS rules ----

    [Fact]
    public void Cart_Commands_ShouldNot_ReferenceQueries()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CartApplication)
            .That()
            .ResideInNamespace("FluxCommerce.Modules.Cart.Application.Commands")
            .ShouldNot()
            .HaveDependencyOn("FluxCommerce.Modules.Cart.Application.Queries")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Cart command handlers must not reference queries: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Cart_Queries_ShouldNot_ReferenceCommands()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CartApplication)
            .That()
            .ResideInNamespace("FluxCommerce.Modules.Cart.Application.Queries")
            .ShouldNot()
            .HaveDependencyOn("FluxCommerce.Modules.Cart.Application.Commands")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Cart query handlers must not reference commands: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Assembly helpers ----

    private const string CartRoot = "FluxCommerce.Modules.Cart";

    private static readonly string[] RedisNamespaces =
    [
        "StackExchange.Redis",
        "FluxCommerce.BuildingBlocks.Infrastructure.Caching",
    ];

    private static IReadOnlyList<System.Reflection.Assembly> CartAssemblies =>
        ArchitectureRegistry.ModuleAssemblies
            .Where(a => a.GetName().Name!.StartsWith($"{CartRoot}.", StringComparison.Ordinal))
            .ToList();

    private static System.Reflection.Assembly CartDomain =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CartRoot}.Domain");

    private static System.Reflection.Assembly CartApplication =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CartRoot}.Application");

    private static System.Reflection.Assembly CartInfrastructure =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CartRoot}.Infrastructure");

    private static System.Reflection.Assembly CartContracts =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CartRoot}.Contracts");

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
        System.Reflection.Assembly assembly,
        IReadOnlyList<string> forbiddenNamespaces,
        string rule,
        List<string> failures)
    {
        foreach (var ns in forbiddenNamespaces)
        {
            AssertNoDependencies(assembly, ns, rule, failures);
        }
    }

    private static void AssertNoDependencies(
        IEnumerable<System.Reflection.Assembly> assemblies,
        IReadOnlyList<string> forbiddenNamespaces,
        string rule,
        List<string> failures)
    {
        foreach (var assembly in assemblies)
        {
            AssertNoDependencies(assembly, forbiddenNamespaces, rule, failures);
        }
    }
}
