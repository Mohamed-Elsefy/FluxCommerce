using System.Reflection;
using FluxCommerce.BuildingBlocks.Messaging.Consuming;
using NetArchTest.Rules;
using Xunit;

namespace FluxCommerce.ArchitectureTests;

/// <summary>
/// Phase 7.11 (architecture &amp; security hardening): the transport-boundary guarantees of the
/// consumer architecture. Consumer handlers are transport handlers — they live in the module's
/// Infrastructure layer beside the module DbContext and IIdempotentProcessor they share, and they
/// must never leak RabbitMQ.Client or consumer-transport internals into Domain, Application or
/// Contracts.
/// </summary>
public class ConsumerArchitectureTests
{
    [Fact]
    public void ConsumerHandlers_ShouldResideInModuleInfrastructure()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.ModuleAssemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(IsConsumerHandler)
                .ToArray();

            foreach (var type in handlerTypes)
            {
                var ns = type.Namespace ?? string.Empty;

                if (!ns.StartsWith("FluxCommerce.Modules.", StringComparison.Ordinal)
                    || ns.IndexOf(".Infrastructure", StringComparison.Ordinal) < 0)
                {
                    failures.Add(
                        $"Consumer handler '{type.FullName}' must reside in the module's Infrastructure layer; " +
                        "consumer transport must never leak into Domain, Application or Contracts.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void DomainAndApplication_ShouldNot_ReferenceRabbitMqClientOrConsumerTransport()
    {
        var failures = new List<string>();

        foreach (var assembly in ArchitectureRegistry.DomainAssemblies.Concat(ArchitectureRegistry.ApplicationAssemblies))
        {
            AssertNoDependencies(assembly, "RabbitMQ.Client", "Domain/Application -> RabbitMQ", failures);
            AssertNoDependencies(
                assembly,
                "FluxCommerce.BuildingBlocks.Infrastructure.Consuming",
                "Domain/Application -> consumer transport",
                failures);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ModuleContracts_ShouldNot_ReferenceRabbitMqClientOrEfCore()
    {
        var failures = new List<string>();

        foreach (var assembly in ContractsAssemblies)
        {
            AssertNoDependencies(assembly, "RabbitMQ.Client", "Contracts -> RabbitMQ", failures);

            foreach (var ns in ArchitectureRegistry.EfCoreNamespaces)
            {
                AssertNoDependencies(assembly, ns, "Contracts -> EF Core", failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool IsConsumerHandler(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsInterface)
        {
            return false;
        }

        return type.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>));
    }

    private static IReadOnlyList<Assembly> ContractsAssemblies =>
        ArchitectureRegistry.ModuleAssemblies
            .Where(a => a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToList();

    private static void AssertNoDependencies(
        Assembly assembly,
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
}
