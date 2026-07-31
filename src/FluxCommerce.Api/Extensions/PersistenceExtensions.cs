using FluxCommerce.Api.Options;

namespace FluxCommerce.Api.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? throw new InvalidOperationException("Database configuration is missing.");

        services.AddSingleton(databaseOptions);

        return services;
    }

    public static string GetModuleConnectionString(this DatabaseOptions options, string moduleName)
    {
        return options.ModuleConnections.TryGetValue(moduleName, out var moduleConnection)
            ? moduleConnection
            : options.ConnectionString;
    }
}
