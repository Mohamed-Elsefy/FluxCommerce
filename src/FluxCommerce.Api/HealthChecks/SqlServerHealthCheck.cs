using FluxCommerce.Api.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Api.HealthChecks;

public sealed class SqlServerHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public SqlServerHealthCheck(IOptions<DatabaseOptions> options)
    {
        var builder = new SqlConnectionStringBuilder(options.Value.ConnectionString)
        {
            InitialCatalog = "master"
        };

        _connectionString = builder.ConnectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            return HealthCheckResult.Healthy("SQL Server is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("SQL Server health check was cancelled.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("SQL Server is not reachable.");
        }
    }
}
