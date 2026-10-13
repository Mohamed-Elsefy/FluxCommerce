using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

public class CatalogArchitectureTests
{
    // ---- Module isolation ----

    [Fact]
    public void Catalog_ShouldNot_ReferenceOtherModules()
    {
        var failures = new List<string>();

        foreach (var assembly in CatalogAssemblies)
        {
            foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Catalog"))
            {
                foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Contracts" })
                {
                    AssertNoDependencies(
                        assembly,
                        $"FluxCommerce.Modules.{module}.{layer}",
                        $"Catalog -> {module}.{layer}",
                        failures);
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_ShouldNot_ReferenceOtherModulesDbContexts()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Catalog"))
        {
            foreach (var assembly in CatalogAssemblies)
            {
                AssertNoDependencies(
                    assembly,
                    $"FluxCommerce.Modules.{module}.Infrastructure.DbContexts",
                    $"Catalog -> {module} DbContext",
                    failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Domain layer rules ----

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferenceApplication()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ["FluxCommerce.Modules.Catalog.Application"],
            "Catalog.Domain -> Catalog.Application",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferenceContracts()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ["FluxCommerce.Modules.Catalog.Contracts"],
            "Catalog.Domain -> Catalog.Contracts",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ["FluxCommerce.Modules.Catalog.Infrastructure"],
            "Catalog.Domain -> Catalog.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ArchitectureRegistry.EfCoreNamespaces,
            "Catalog.Domain -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Catalog.Domain -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferenceLocalization()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ["FluxCommerce.BuildingBlocks.Localization"],
            "Catalog.Domain -> Localization",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Domain_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogDomain],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Catalog.Domain -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Application layer rules ----

    [Fact]
    public void Catalog_Application_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogApplication],
            ["FluxCommerce.Modules.Catalog.Infrastructure"],
            "Catalog.Application -> Catalog.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Application_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogApplication],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Catalog.Application -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Application_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogApplication],
            ArchitectureRegistry.EfCoreNamespaces,
            "Catalog.Application -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Application_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogApplication],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Catalog.Application -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Application_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogApplication],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Catalog.Application -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Infrastructure layer rules ----

    [Fact]
    public void Catalog_Infrastructure_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogInfrastructure],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Catalog.Infrastructure -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_DbContext_ShouldResideInInfrastructure()
    {
        var failures = new List<string>();

        foreach (var assembly in CatalogAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("DbContext")
                .Should()
                .ResideInNamespace("FluxCommerce.Modules.Catalog.Infrastructure.DbContexts")
                .GetResult();

            if (!result.IsSuccessful)
            {
                var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                failures.Add($"Catalog DbContexts must reside in Infrastructure.DbContexts: {assembly.GetName().Name} via: {string.Join(", ", failing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Contracts layer rules ----

    [Fact]
    public void Catalog_Contracts_ShouldNot_ReferenceModuleLayers()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogContracts],
            [
                "FluxCommerce.Modules.Catalog.Domain",
                "FluxCommerce.Modules.Catalog.Application",
                "FluxCommerce.Modules.Catalog.Infrastructure",
            ],
            "Catalog.Contracts -> Catalog layers",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Contracts_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogContracts],
            ArchitectureRegistry.EfCoreNamespaces,
            "Catalog.Contracts -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Contracts_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogContracts],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Catalog.Contracts -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- CQRS rules ----

    [Fact]
    public void Catalog_Commands_ShouldNot_ReferenceQueries()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CatalogApplication)
            .That()
            .ResideInNamespace("FluxCommerce.Modules.Catalog.Application.Commands")
            .ShouldNot()
            .HaveDependencyOn("FluxCommerce.Modules.Catalog.Application.Queries")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog command handlers must not reference queries: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Queries_ShouldNot_ReferenceCommands()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CatalogApplication)
            .That()
            .ResideInNamespace("FluxCommerce.Modules.Catalog.Application.Queries")
            .ShouldNot()
            .HaveDependencyOn("FluxCommerce.Modules.Catalog.Application.Commands")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog query handlers must not reference commands: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Repository rules ----

    [Fact]
    public void Catalog_RepositoryInterfaces_ShouldResideInDomain()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CatalogDomain)
            .That()
            .HaveNameEndingWith("Repository")
            .And()
            .AreInterfaces()
            .Should()
            .ResideInNamespace("FluxCommerce.Modules.Catalog.Domain.Repositories")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog repository interfaces must reside in Domain.Repositories: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_RepositoryImplementations_ShouldResideInInfrastructure()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CatalogInfrastructure)
            .That()
            .HaveNameEndingWith("Repository")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespace("FluxCommerce.Modules.Catalog.Infrastructure.Repositories")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog repository implementations must reside in Infrastructure.Repositories: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Application_ShouldNot_ReferenceRepositoryImplementations()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [CatalogApplication],
            ["FluxCommerce.Modules.Catalog.Infrastructure.Repositories"],
            "Catalog.Application -> repository implementations",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Presentation rules ----

    [Fact]
    public void Catalog_Controllers_Should_InheritApiControllerBase()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(ArchitectureRegistry.Api)
            .That()
            .HaveNameEndingWith("Controller")
            .And()
            .HaveDependencyOn("FluxCommerce.Modules.Catalog")
            .Should()
            .Inherit(typeof(FluxCommerce.BuildingBlocks.Web.Controllers.ApiControllerBase))
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog controllers must inherit ApiControllerBase: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Catalog_Controllers_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(ArchitectureRegistry.Api)
            .That()
            .HaveNameEndingWith("Controller")
            .And()
            .HaveDependencyOn("FluxCommerce.Modules.Catalog")
            .ShouldNot()
            .HaveDependencyOn("FluxCommerce.Modules.Catalog.Infrastructure")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog controllers must remain thin (no Infrastructure reference): {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Localization rules ----

    [Fact]
    public void Catalog_Validators_Should_Use_Localizer()
    {
        var failures = new List<string>();

        var result = Types.InAssembly(CatalogApplication)
            .That()
            .HaveNameEndingWith("Validator")
            .Should()
            .HaveDependencyOn("FluxCommerce.BuildingBlocks.Localization.Abstractions")
            .GetResult();

        if (!result.IsSuccessful)
        {
            var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
            failures.Add($"Catalog validators must resolve messages through ILocalizer: {string.Join(", ", failing)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Contract mirror drift protection ----

    [Theory]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.AttributeDataType), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.AttributeDataType))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.AttributeStatus), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.AttributeStatus))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.CatalogProductSortBy), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogProductSortBy))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.CatalogProductSortDirection), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.CatalogProductSortDirection))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.CategoryStatus), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.CategoryStatus))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.ProductStatus), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.ProductStatus))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.ProductVisibility), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.ProductVisibility))]
    [InlineData(typeof(FluxCommerce.Modules.Catalog.Domain.Enums.VariantStatus), typeof(FluxCommerce.Modules.Catalog.Contracts.Enums.VariantStatus))]
    public void CatalogEnums_ContractsMirror_ShouldMatchDomain(Type domainEnum, Type contractEnum)
    {
        Assert.Equal(Enum.GetNames(domainEnum), Enum.GetNames(contractEnum));
        Assert.Equal(
            Enum.GetValues(domainEnum).Cast<int>().ToArray(),
            Enum.GetValues(contractEnum).Cast<int>().ToArray());
    }

    // ---- Assembly helpers ----

    private static readonly string CatalogRoot = "FluxCommerce.Modules.Catalog";

    private static IReadOnlyList<System.Reflection.Assembly> CatalogAssemblies =>
        ArchitectureRegistry.ModuleAssemblies
            .Where(a => a.GetName().Name!.StartsWith($"{CatalogRoot}.", StringComparison.Ordinal))
            .ToList();

    private static System.Reflection.Assembly CatalogDomain =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CatalogRoot}.Domain");

    private static System.Reflection.Assembly CatalogApplication =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CatalogRoot}.Application");

    private static System.Reflection.Assembly CatalogInfrastructure =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CatalogRoot}.Infrastructure");

    private static System.Reflection.Assembly CatalogContracts =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{CatalogRoot}.Contracts");

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
