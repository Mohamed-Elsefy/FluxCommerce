using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrder;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Infrastructure.Outbox;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Orders.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Fallback correlation provider keeps the module's persistence stack self-contained; the API
        // host overrides it with the web-backed provider (last registration wins).
        services.AddSingleton<ICorrelationIdProvider, GuidCorrelationIdProvider>();

        services.AddSingleton<OrdersOutboxMessageFactory>();
        services.AddSingleton<OrdersOutboxSaveChangesInterceptor>();
        services.AddScoped<OrdersOutboxStore>();
        services.AddScoped<IOutboxStore, OrdersOutboxStore>();

        services.AddOptions<OutboxDispatchOptions>();
        services.AddScoped<OrdersOutboxDispatcher>();
        services.AddHostedService<OrdersOutboxDispatcher>();

        services.AddDbContext<OrdersDbContext>((serviceProvider, options) =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Orders");
                sqlOptions.MigrationsAssembly(typeof(OrdersDbContext).Assembly.GetName().Name);
            })
            .AddInterceptors(serviceProvider.GetRequiredService<OrdersOutboxSaveChangesInterceptor>()));

        services.AddScoped<ICheckoutOrderSnapshotBuilder, CheckoutOrderSnapshotBuilder>();
        services.AddScoped<ICheckoutStore, CheckoutStore>();
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IOrderSummaryProjectionStore, OrderSummaryProjectionStore>();
        services.AddScoped<IOrderSummaryProjectionService, OrderSummaryProjectionService>();
        services.AddScoped<IIdempotencyStore, OrdersIdempotencyStore>();
        services.AddScoped<IDuplicateDetector, OrdersDuplicateDetector>();
        services.AddScoped<IIdempotentProcessor, OrdersIdempotentProcessor>();
        services.AddScoped<ICommandHandler<CheckoutCommand, Result<Order>>, CheckoutCommandHandler>();
        services.AddScoped<IQueryHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryResponse>>>, GetMyOrdersQueryHandler>();
        services.AddScoped<IQueryHandler<GetOrderQuery, Result<OrderDetailsResponse>>, GetOrderQueryHandler>();
        services.AddScoped<ICommandHandler<CancelOrderCommand, Result<OrderDetailsResponse>>, CancelOrderCommandHandler>();

        return services;
    }
}
