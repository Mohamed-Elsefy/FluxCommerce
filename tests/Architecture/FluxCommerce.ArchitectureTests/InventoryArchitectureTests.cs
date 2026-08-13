using FluxCommerce.BuildingBlocks.Persistence.DbContexts;
using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

public class InventoryArchitectureTests
{
    // ---- Module isolation ----

    [Fact]
    public void Inventory_ShouldNot_ReferenceOtherModules()
    {
        var failures = new List<string>();

        foreach (var assembly in InventoryAssemblies)
        {
            foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Inventory"))
            {
                foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Contracts" })
                {
                    AssertNoDependencies(
                        assembly,
                        $"FluxCommerce.Modules.{module}.{layer}",
                        $"Inventory -> {module}.{layer}",
                        failures);
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_ShouldNot_ReferenceOtherModulesDbContexts()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Inventory"))
        {
            foreach (var assembly in InventoryAssemblies)
            {
                AssertNoDependencies(
                    assembly,
                    $"FluxCommerce.Modules.{module}.Infrastructure.DbContexts",
                    $"Inventory -> {module} DbContext",
                    failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_ShouldNot_ReferenceMessagingOrRabbitMq()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            InventoryAssemblies,
            ["FluxCommerce.BuildingBlocks.Messaging", "RabbitMQ.Client"],
            "Inventory -> Messaging/RabbitMQ",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_ShouldNot_ReferenceOutbox()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            InventoryAssemblies,
            ["FluxCommerce.BuildingBlocks.Outbox"],
            "Inventory -> Outbox",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_ShouldNot_IntroduceSagaOrOrchestratorTypes()
    {
        var failures = new List<string>();

        foreach (var assembly in InventoryAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("Saga")
                .Or()
                .HaveNameEndingWith("Orchestrator")
                .GetTypes()
                .ToArray();

            if (result.Length > 0)
            {
                failures.Add($"Inventory introduced saga/orchestrator types: {assembly.GetName().Name} via: {string.Join(", ", result.Select(t => t.FullName))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Domain layer rules ----

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferenceApplication()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ["FluxCommerce.Modules.Inventory.Application"],
            "Inventory.Domain -> Inventory.Application",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferenceContracts()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ["FluxCommerce.Modules.Inventory.Contracts"],
            "Inventory.Domain -> Inventory.Contracts",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ["FluxCommerce.Modules.Inventory.Infrastructure"],
            "Inventory.Domain -> Inventory.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ArchitectureRegistry.EfCoreNamespaces,
            "Inventory.Domain -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Inventory.Domain -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferenceLocalization()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ["FluxCommerce.BuildingBlocks.Localization"],
            "Inventory.Domain -> Localization",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Domain_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryDomain],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Inventory.Domain -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Application layer rules ----

    [Fact]
    public void Inventory_Application_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryApplication],
            ["FluxCommerce.Modules.Inventory.Infrastructure"],
            "Inventory.Application -> Inventory.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Application_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryApplication],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Inventory.Application -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Application_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryApplication],
            ArchitectureRegistry.EfCoreNamespaces,
            "Inventory.Application -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Application_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryApplication],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Inventory.Application -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Application_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryApplication],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Inventory.Application -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Application_ShouldNot_ReferenceBuildingBlocksInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryApplication],
            ["FluxCommerce.BuildingBlocks.Infrastructure"],
            "Inventory.Application -> BuildingBlocks.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Infrastructure layer rules ----

    [Fact]
    public void Inventory_Infrastructure_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryInfrastructure],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Inventory.Infrastructure -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_DbContext_ShouldResideInInfrastructure()
    {
        var failures = new List<string>();

        foreach (var assembly in InventoryAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("DbContext")
                .Should()
                .ResideInNamespace("FluxCommerce.Modules.Inventory.Infrastructure.DbContexts")
                .GetResult();

            if (!result.IsSuccessful)
            {
                var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                failures.Add($"Inventory DbContexts must reside in Infrastructure.DbContexts: {assembly.GetName().Name} via: {string.Join(", ", failing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void InventoryDbContext_ShouldDeriveFromBaseDbContext()
    {
        Assert.Equal(typeof(BaseDbContext), typeof(FluxCommerce.Modules.Inventory.Infrastructure.DbContexts.InventoryDbContext).BaseType);
    }

    // ---- Contracts layer rules ----

    [Fact]
    public void Inventory_Contracts_ShouldNot_ReferenceModuleLayers()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryContracts],
            [
                "FluxCommerce.Modules.Inventory.Domain",
                "FluxCommerce.Modules.Inventory.Application",
                "FluxCommerce.Modules.Inventory.Infrastructure",
            ],
            "Inventory.Contracts -> Inventory layers",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Contracts_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryContracts],
            ArchitectureRegistry.EfCoreNamespaces,
            "Inventory.Contracts -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Contracts_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryContracts],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Inventory.Contracts -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Inventory_Contracts_ShouldNot_ReferenceMessagingOrOutbox()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [InventoryContracts],
            ["FluxCommerce.BuildingBlocks.Messaging", "FluxCommerce.BuildingBlocks.Outbox"],
            "Inventory.Contracts -> Messaging/Outbox",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Contract mirror drift protection ----

    [Fact]
    public void InventoryReservationStatus_ContractsMirror_ShouldMatchDomain()
    {
        var domainNames = Enum.GetNames(typeof(FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus));
        var contractNames = Enum.GetNames(typeof(FluxCommerce.Modules.Inventory.Contracts.Enums.InventoryReservationStatus));

        var domainValues = Enum.GetValues(typeof(FluxCommerce.Modules.Inventory.Domain.Enums.InventoryReservationStatus))
            .Cast<int>()
            .ToArray();
        var contractValues = Enum.GetValues(typeof(FluxCommerce.Modules.Inventory.Contracts.Enums.InventoryReservationStatus))
            .Cast<int>()
            .ToArray();

        Assert.Equal(domainNames, contractNames);
        Assert.Equal(domainValues, contractValues);
    }

    // ---- Assembly helpers ----

    private const string InventoryRoot = "FluxCommerce.Modules.Inventory";

    private static IReadOnlyList<System.Reflection.Assembly> InventoryAssemblies =>
        ArchitectureRegistry.ModuleAssemblies
            .Where(a => a.GetName().Name!.StartsWith($"{InventoryRoot}.", StringComparison.Ordinal))
            .ToList();

    private static System.Reflection.Assembly InventoryDomain =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{InventoryRoot}.Domain");

    private static System.Reflection.Assembly InventoryApplication =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{InventoryRoot}.Application");

    private static System.Reflection.Assembly InventoryInfrastructure =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{InventoryRoot}.Infrastructure");

    private static System.Reflection.Assembly InventoryContracts =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{InventoryRoot}.Contracts");

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
