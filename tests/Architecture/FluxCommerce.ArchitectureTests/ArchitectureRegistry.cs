using System.Reflection;

namespace FluxCommerce.ArchitectureTests;

internal static class ArchitectureRegistry
{
    private const string BuildingBlocksRoot = "FluxCommerce.BuildingBlocks";
    private const string ModulesRoot = "FluxCommerce.Modules";

    private static readonly string[] BuildingBlockProjects =
        ["Domain", "Application", "Infrastructure", "Messaging", "Observability", "Persistence", "Web", "Outbox", "Idempotency", "Resilience"];

    private static readonly string[] Modules = ["Catalog", "Cart", "Orders", "Inventory", "Payments"];

    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Contracts"];

    public static IReadOnlyList<string> ModuleNames { get; } = Modules;

    public static Assembly Api { get; } = GetAssembly("FluxCommerce.Api");

    public static IReadOnlyList<Assembly> BuildingBlocks { get; } = GetAssemblies(BuildingBlockProjects.Select(p => $"{BuildingBlocksRoot}.{p}"));

    public static IReadOnlyList<Assembly> DomainAssemblies { get; } = GetAssemblies(Modules.Select(m => $"{ModulesRoot}.{m}.Domain"));

    public static IReadOnlyList<Assembly> ApplicationAssemblies { get; } = GetAssemblies(Modules.Select(m => $"{ModulesRoot}.{m}.Application"));

    public static IReadOnlyList<Assembly> InfrastructureAssemblies { get; } = GetAssemblies(Modules.Select(m => $"{ModulesRoot}.{m}.Infrastructure"));

    public static IReadOnlyList<Assembly> ModuleAssemblies { get; } =
        GetAssemblies(Modules.SelectMany(m => Layers.Select(l => $"{ModulesRoot}.{m}.{l}")));

    public static IReadOnlyList<string> InfrastructureNamespaces { get; } =
        GetNamespaces(new[] { $"{BuildingBlocksRoot}.Infrastructure" }.Concat(Modules.Select(m => $"{ModulesRoot}.{m}.Infrastructure")));

    public static IReadOnlyList<string> ModuleNamespaces { get; } =
        GetNamespaces(Modules.SelectMany(m => Layers.Select(l => $"{ModulesRoot}.{m}.{l}")));

    public static IReadOnlyList<string> OtherModulesInfrastructureNamespaces(string module) =>
        GetNamespaces(Modules.Where(m => m != module).Select(m => $"{ModulesRoot}.{m}.Infrastructure"));

    public static IReadOnlyList<string> EfCoreNamespaces { get; } =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.Diagnostics",
        "Microsoft.EntityFrameworkCore.Infrastructure",
        "Microsoft.EntityFrameworkCore.Metadata",
        "Microsoft.EntityFrameworkCore.Storage",
        "Microsoft.EntityFrameworkCore.ChangeTracking",
        "Microsoft.EntityFrameworkCore.Query",
    ];

    public static IReadOnlyList<string> AspNetCoreNamespaces { get; } =
    [
        "Microsoft.AspNetCore",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Builder",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Logging",
    ];

    private static IReadOnlyList<Assembly> GetAssemblies(IEnumerable<string> names) =>
        names.Select(GetAssembly).ToList();

    private static IReadOnlyList<string> GetNamespaces(IEnumerable<string> names) =>
        names.Distinct().ToList();

    private static Assembly GetAssembly(string name) => Assembly.Load(name);
}
