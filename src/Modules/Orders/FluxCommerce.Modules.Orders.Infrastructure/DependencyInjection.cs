using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrder;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Orders");
                sqlOptions.MigrationsAssembly(typeof(OrdersDbContext).Assembly.GetName().Name);
            }));

        services.AddScoped<ICheckoutOrderSnapshotBuilder, CheckoutOrderSnapshotBuilder>();
        services.AddScoped<ICheckoutStore, CheckoutStore>();
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<ICommandHandler<CheckoutCommand, Result<Order>>, CheckoutCommandHandler>();
        services.AddScoped<IQueryHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryResponse>>>, GetMyOrdersQueryHandler>();
        services.AddScoped<IQueryHandler<GetOrderQuery, Result<OrderDetailsResponse>>, GetOrderQueryHandler>();
        services.AddScoped<ICommandHandler<CancelOrderCommand, Result<OrderDetailsResponse>>, CancelOrderCommandHandler>();

        return services;
    }
}
