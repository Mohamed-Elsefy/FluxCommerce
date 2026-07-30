using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextPool<InventoryDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Inventory");
                sqlOptions.MigrationsAssembly(typeof(InventoryDbContext).Assembly.GetName().Name);
            }));

        return services;
    }
}
