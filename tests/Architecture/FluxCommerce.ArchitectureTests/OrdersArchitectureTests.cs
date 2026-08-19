using FluxCommerce.BuildingBlocks.Persistence.DbContexts;
using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

public class OrdersArchitectureTests
{
    // ---- Module isolation ----

    [Fact]
    public void Orders_ShouldNot_ReferenceOtherModules()
    {
        var failures = new List<string>();

        foreach (var assembly in OrdersAssemblies)
        {
            foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Orders"))
            {
                foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Contracts" })
                {
                    // Phase 4.5 boundary: Orders.Application is allowed to depend on the approved
                    // cross-module checkout contracts (FluxCommerce.Modules.Cart.Contracts and
                    // FluxCommerce.Modules.Catalog.Contracts) — the ONLY Cart/Catalog boundaries
                    // Orders may cross. All other module references remain forbidden.
                    if ((module == "Cart" || module == "Catalog")
                        && layer == "Contracts"
                        && assembly.GetName().Name == "FluxCommerce.Modules.Orders.Application")
                    {
                        continue;
                    }

                    // Phase 8.3 F2 carve-out (ADR-009): Orders.Infrastructure is allowed to reference
                    // the approved Inventory integration contract namespaces only. The strict boundary
                    // is pinned by Orders_Infrastructure_ReferencesInventory_OnlyThroughApprovedContractNamespaces.
                    if (module == "Inventory"
                        && layer == "Contracts"
                        && assembly.GetName().Name == "FluxCommerce.Modules.Orders.Infrastructure")
                    {
                        continue;
                    }

                    AssertNoDependencies(
                        assembly,
                        $"FluxCommerce.Modules.{module}.{layer}",
                        $"Orders -> {module}.{layer}",
                        failures);
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferenceOtherModulesDbContexts()
    {
        var failures = new List<string>();

        foreach (var module in ArchitectureRegistry.ModuleNames.Where(m => m != "Orders"))
        {
            foreach (var assembly in OrdersAssemblies)
            {
                AssertNoDependencies(
                    assembly,
                    $"FluxCommerce.Modules.{module}.Infrastructure.DbContexts",
                    $"Orders -> {module} DbContext",
                    failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferenceCatalogInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            OrdersAssemblies,
            ["FluxCommerce.Modules.Catalog.Infrastructure"],
            "Orders -> Catalog Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferenceCartInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            OrdersAssemblies,
            ["FluxCommerce.Modules.Cart.Infrastructure"],
            "Orders -> Cart Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferenceInventory()
    {
        // Phase 8.3 F2 carve-out (ADR-009): Orders.Infrastructure may depend on the approved Inventory
        // integration contract namespaces (FluxCommerce.Modules.Inventory.Contracts.Integration and
        // .IntegrationEvents), so the full Inventory surface is forbidden for Domain, Application and
        // Contracts, while Infrastructure may only still not reach the other Inventory layers.
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain, OrdersApplication, OrdersContracts],
            [
                "FluxCommerce.Modules.Inventory.Domain",
                "FluxCommerce.Modules.Inventory.Application",
                "FluxCommerce.Modules.Inventory.Infrastructure",
                "FluxCommerce.Modules.Inventory.Contracts",
            ],
            "Orders -> Inventory",
            failures);

        AssertNoDependencies(
            [OrdersInfrastructure],
            [
                "FluxCommerce.Modules.Inventory.Domain",
                "FluxCommerce.Modules.Inventory.Application",
                "FluxCommerce.Modules.Inventory.Infrastructure",
            ],
            "Orders.Infrastructure -> Inventory (non-contract)",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferencePayments()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            OrdersAssemblies,
            [
                "FluxCommerce.Modules.Payments.Domain",
                "FluxCommerce.Modules.Payments.Application",
                "FluxCommerce.Modules.Payments.Infrastructure",
                "FluxCommerce.Modules.Payments.Contracts",
            ],
            "Orders -> Payments",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferenceMessagingOrRabbitMq()
    {
        // Phase 6.4 (G12): Orders.Contracts is the home of the module Integration Event contracts,
        // which derive from the contract-only BuildingBlocks.Messaging metadata base; Infrastructure
        // necessarily touches that metadata transitively when it serializes the contracts into the
        // Outbox. Domain and Application remain free of Messaging, and the RabbitMQ client stays out
        // of Domain, Application and Infrastructure.
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain, OrdersApplication],
            ["FluxCommerce.BuildingBlocks.Messaging"],
            "Orders -> Messaging",
            failures);

        AssertNoDependencies(
            [OrdersDomain, OrdersApplication, OrdersInfrastructure],
            ["RabbitMQ.Client"],
            "Orders -> RabbitMQ",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_ShouldNot_ReferenceOutbox()
    {
        // Phase 6.2: Orders.Infrastructure implements the module Outbox persistence (it maps the
        // shared OutboxMessage entity into the Orders DbContext). Domain, Application and Contracts
        // must remain free of the Outbox abstraction.
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain, OrdersApplication, OrdersContracts],
            ["FluxCommerce.BuildingBlocks.Outbox"],
            "Orders -> Outbox",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Infrastructure_IsAllowedToReferenceOutbox()
    {
        var referencingTypes = Types.InAssembly(OrdersInfrastructure)
            .That()
            .HaveDependencyOn("FluxCommerce.BuildingBlocks.Outbox")
            .GetTypes();

        Assert.True(
            referencingTypes.Any(),
            "Orders.Infrastructure must reference the Outbox abstraction to map the module Outbox persistence entity.");
    }

    [Fact]
    public void Orders_ShouldNot_IntroduceOrchestratorTypesOutsideInfrastructure()
    {
        // Phase 8.2/8.3 (F3, ADR-009): the Saga state model (OrderSaga) is hosted in Orders.Domain and
        // the Saga orchestrator (OrderSagaOrchestrator) is deliberately placed in Orders.Infrastructure.
        // Only the layers above Infrastructure must stay free of orchestrator types.
        var failures = new List<string>();

        foreach (var assembly in new[] { OrdersDomain, OrdersApplication, OrdersContracts })
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("Orchestrator")
                .GetTypes()
                .ToArray();

            if (result.Length > 0)
            {
                failures.Add($"Orders introduced orchestrator types: {assembly.GetName().Name} via: {string.Join(", ", result.Select(t => t.FullName))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Should_HostTheSagaStateModelInDomain()
    {
        var sagaTypes = Types.InAssembly(OrdersDomain)
            .That()
            .ResideInNamespace("FluxCommerce.Modules.Orders.Domain.Sagas")
            .GetTypes()
            .ToArray();

        Assert.Contains(sagaTypes, type => type.Name == "OrderSaga");
    }

    [Fact]
    public void Orders_Should_HostTheSagaOrchestratorInInfrastructure()
    {
        // Phase 8.3 (ADR-009): the Saga orchestrator drives the workflow beside the module DbContext,
        // idempotency store, and consumers — never in Domain or Application.
        var orchestratorTypes = Types.InAssembly(OrdersInfrastructure)
            .That()
            .HaveNameEndingWith("Orchestrator")
            .GetTypes()
            .ToArray();

        Assert.Contains(orchestratorTypes, type => type.Name == "OrderSagaOrchestrator");
    }

    [Fact]
    public void Orders_Infrastructure_ReferencesInventory_OnlyThroughApprovedContractNamespaces()
    {
        // Phase 8.3 F2 carve-out (ADR-009): Orders.Infrastructure may depend on the Inventory module
        // ONLY through the approved integration contract namespaces. Reaching the request/response or
        // enum surfaces of Inventory.Contracts, or any other Inventory layer, is forbidden.
        var failures = new List<string>();

        AssertNoDependencies(
            OrdersInfrastructure,
            "FluxCommerce.Modules.Inventory.Contracts.Requests",
            "Orders.Infrastructure -> Inventory.Requests",
            failures);

        AssertNoDependencies(
            OrdersInfrastructure,
            "FluxCommerce.Modules.Inventory.Contracts.Responses",
            "Orders.Infrastructure -> Inventory.Responses",
            failures);

        AssertNoDependencies(
            OrdersInfrastructure,
            "FluxCommerce.Modules.Inventory.Contracts.Enums",
            "Orders.Infrastructure -> Inventory.Enums",
            failures);

        var referencingTypes = Types.InAssembly(OrdersInfrastructure)
            .That()
            .HaveDependencyOn("FluxCommerce.Modules.Inventory.Contracts.IntegrationEvents")
            .GetTypes();

        Assert.True(
            referencingTypes.Any(),
            "Orders.Infrastructure must reference the approved Inventory integration event contracts to issue reservation requests.");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Domain layer rules ----

    [Fact]
    public void Orders_Domain_ShouldNot_ReferenceApplication()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ["FluxCommerce.Modules.Orders.Application"],
            "Orders.Domain -> Orders.Application",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Domain_ShouldNot_ReferenceContracts()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ["FluxCommerce.Modules.Orders.Contracts"],
            "Orders.Domain -> Orders.Contracts",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Domain_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ["FluxCommerce.Modules.Orders.Infrastructure"],
            "Orders.Domain -> Orders.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Domain_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ArchitectureRegistry.EfCoreNamespaces,
            "Orders.Domain -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Domain_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Orders.Domain -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Domain_ShouldNot_ReferenceLocalization()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ["FluxCommerce.BuildingBlocks.Localization"],
            "Orders.Domain -> Localization",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Domain_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersDomain],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Orders.Domain -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Application layer rules ----

    [Fact]
    public void Orders_Application_ShouldNot_ReferenceInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersApplication],
            ["FluxCommerce.Modules.Orders.Infrastructure"],
            "Orders.Application -> Orders.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Application_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersApplication],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Orders.Application -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Application_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersApplication],
            ArchitectureRegistry.EfCoreNamespaces,
            "Orders.Application -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Application_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersApplication],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Orders.Application -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Application_ShouldNot_ReferencePersistence()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersApplication],
            ["FluxCommerce.BuildingBlocks.Persistence"],
            "Orders.Application -> Persistence",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Application_ShouldNot_ReferenceBuildingBlocksInfrastructure()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersApplication],
            ["FluxCommerce.BuildingBlocks.Infrastructure"],
            "Orders.Application -> BuildingBlocks.Infrastructure",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---- Infrastructure layer rules ----

    [Fact]
    public void Orders_Infrastructure_ShouldNot_ReferenceApi()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersInfrastructure],
            [ArchitectureRegistry.Api.GetName().Name!],
            "Orders.Infrastructure -> API",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_DbContext_ShouldResideInInfrastructure()
    {
        var failures = new List<string>();

        foreach (var assembly in OrdersAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("DbContext")
                .Should()
                .ResideInNamespace("FluxCommerce.Modules.Orders.Infrastructure.DbContexts")
                .GetResult();

            if (!result.IsSuccessful)
            {
                var failing = result.FailingTypeNames is null ? [] : result.FailingTypeNames.ToArray();
                failures.Add($"Orders DbContexts must reside in Infrastructure.DbContexts: {assembly.GetName().Name} via: {string.Join(", ", failing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void OrdersDbContext_ShouldDeriveFromBaseDbContext()
    {
        Assert.Equal(typeof(BaseDbContext), typeof(FluxCommerce.Modules.Orders.Infrastructure.DbContexts.OrdersDbContext).BaseType);
    }

    // ---- Contracts layer rules ----

    [Fact]
    public void Orders_Contracts_ShouldNot_ReferenceModuleLayers()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersContracts],
            [
                "FluxCommerce.Modules.Orders.Domain",
                "FluxCommerce.Modules.Orders.Application",
                "FluxCommerce.Modules.Orders.Infrastructure",
            ],
            "Orders.Contracts -> Orders layers",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Contracts_ShouldNot_ReferenceEfCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersContracts],
            ArchitectureRegistry.EfCoreNamespaces,
            "Orders.Contracts -> EF Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Contracts_ShouldNot_ReferenceAspNetCore()
    {
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersContracts],
            ArchitectureRegistry.AspNetCoreNamespaces,
            "Orders.Contracts -> ASP.NET Core",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Contracts_ShouldNot_ReferenceRabbitMqOrOutbox()
    {
        // Phase 6.4 (G12): Orders.Contracts may reference BuildingBlocks.Messaging (the contract-only
        // metadata base its Integration Event contracts derive from), but it must stay free of the
        // RabbitMQ client and the Outbox abstraction.
        var failures = new List<string>();

        AssertNoDependencies(
            [OrdersContracts],
            ["RabbitMQ.Client", "FluxCommerce.BuildingBlocks.Outbox"],
            "Orders.Contracts -> RabbitMQ/Outbox",
            failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Orders_Contracts_IsAllowedToReferenceMessaging()
    {
        // Phase 6.4 (G12): pins the intentional dependency — at least one Orders.Contracts type (the
        // Integration Event contracts) must derive from BuildingBlocks.Messaging, so the decision
        // cannot be accidentally reverted.
        var referencingTypes = Types.InAssembly(OrdersContracts)
            .That()
            .HaveDependencyOn("FluxCommerce.BuildingBlocks.Messaging")
            .GetTypes();

        Assert.True(
            referencingTypes.Any(),
            "Orders.Contracts must reference BuildingBlocks.Messaging to host the module Integration Event contracts (G12).");
    }

    // ---- Assembly helpers ----

    private const string OrdersRoot = "FluxCommerce.Modules.Orders";

    private static IReadOnlyList<System.Reflection.Assembly> OrdersAssemblies =>
        ArchitectureRegistry.ModuleAssemblies
            .Where(a => a.GetName().Name!.StartsWith($"{OrdersRoot}.", StringComparison.Ordinal))
            .ToList();

    private static System.Reflection.Assembly OrdersDomain =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{OrdersRoot}.Domain");

    private static System.Reflection.Assembly OrdersApplication =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{OrdersRoot}.Application");

    private static System.Reflection.Assembly OrdersInfrastructure =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{OrdersRoot}.Infrastructure");

    private static System.Reflection.Assembly OrdersContracts =>
        ArchitectureRegistry.ModuleAssemblies.Single(a => a.GetName().Name == $"{OrdersRoot}.Contracts");

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
