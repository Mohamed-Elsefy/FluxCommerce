using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Api.Options;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    public Dictionary<string, string> ModuleConnections { get; init; } = [];

    public int MaxRetryCount { get; init; } = 3;

    public int CommandTimeoutSeconds { get; init; } = 30;

    public bool EnableDetailedErrors { get; init; } = false;

    public bool EnableSensitiveDataLogging { get; init; } = false;

    public string GetModuleConnectionString(string moduleName)
    {
        return ModuleConnections.TryGetValue(moduleName, out var moduleConnection)
            ? moduleConnection
            : ConnectionString;
    }
}
