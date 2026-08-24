using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Gateways;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Payments");
                sqlOptions.MigrationsAssembly(typeof(PaymentsDbContext).Assembly.GetName().Name);
            }));

        services.AddScoped<IStripePaymentGateway, StripePaymentGateway>();

        return services;
    }
}
