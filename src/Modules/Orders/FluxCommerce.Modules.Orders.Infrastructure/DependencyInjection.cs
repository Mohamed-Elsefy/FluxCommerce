using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Orders.Application.Commands.CancelOrder;
using FluxCommerce.Modules.Orders.Application.Commands.CancelOrderAsAdmin;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;
using FluxCommerce.Modules.Orders.Application.Commands.RequestOrderRefund;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrder;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetMyOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrder;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrderDailyPoints;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Infrastructure.Background;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.Idempotency;
using FluxCommerce.Modules.Orders.Infrastructure.Outbox;
using FluxCommerce.Modules.Orders.Infrastructure.Persistence.Stores;
using FluxCommerce.Modules.Orders.Infrastructure.Sagas;
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
        services.AddScoped<IOrderSagaStore, OrderSagaStore>();
        services.AddScoped<IOrderSummaryProjectionStore, OrderSummaryProjectionStore>();
        services.AddScoped<IOrderSummaryProjectionService, OrderSummaryProjectionService>();

        // Phase 8.3 Order Saga orchestration. The orchestrator is resolved by the saga consumer and
        // stages the saga and reservation-request outbox records inside the idempotent-processing unit
        // of work; options follow the defaults-only registration pattern of OutboxDispatchOptions.
        services.AddOptions<ReservationRequestOptions>();
        services.AddSingleton<SagaOutboxMessageFactory>();
        services.AddSingleton<IntegrationEventOutboxMessageFactory>();
        services.AddScoped<OrderSagaOrchestrator>();

        // Idempotency: the concrete, module-owned implementations are registered (so the consumer
        // infrastructure can resolve them by concrete type — each consumer carries its module's
        // processor on its descriptor) and the shared interfaces are forwarded to them. The interfaces
        // stay resolvable for this module's internal use and single-module tests; when several modules
        // are composed in one container the consumer infrastructure NEVER resolves the interfaces (they
        // would be last-registration-wins across modules) and always uses the concrete types instead.
        services.AddScoped<OrdersIdempotencyStore>();
        services.AddScoped<OrdersDuplicateDetector>();
        services.AddScoped<OrdersIdempotentProcessor>();
        services.AddScoped<IIdempotencyStore>(serviceProvider => serviceProvider.GetRequiredService<OrdersIdempotencyStore>());
        services.AddScoped<IDuplicateDetector>(serviceProvider => serviceProvider.GetRequiredService<OrdersDuplicateDetector>());
        services.AddScoped<IIdempotentProcessor>(serviceProvider => serviceProvider.GetRequiredService<OrdersIdempotentProcessor>());
        services.AddScoped<ICommandHandler<CheckoutCommand, Result<Order>>, CheckoutCommandHandler>();
        services.AddScoped<ICommandHandler<CancelOrderAsAdminCommand, Result<OrderDetailsResponse>>, CancelOrderAsAdminCommandHandler>();
        services.AddScoped<ICommandHandler<RequestOrderRefundCommand, Result<OrderDetailsResponse>>, RequestOrderRefundCommandHandler>();
        services.AddScoped<IOrderRefundService, OrderRefundService>();
        services.AddScoped<IQueryHandler<GetMyOrdersQuery, Result<PagedResult<OrderSummaryResponse>>>, GetMyOrdersQueryHandler>();
        services.AddScoped<IQueryHandler<GetOrderQuery, Result<OrderDetailsResponse>>, GetOrderQueryHandler>();
        services.AddScoped<IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>>, GetAdminOrdersQueryHandler>();
        services.AddScoped<IQueryHandler<GetAdminOrderQuery, Result<OrderDetailsResponse>>, GetAdminOrderQueryHandler>();
        services.AddScoped<IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>>, GetAdminOrderCountsQueryHandler>();
        services.AddScoped<IQueryHandler<GetOrderDailyPointsQuery, Result<IReadOnlyList<OrderDailyPointReadModel>>>, GetOrderDailyPointsQueryHandler>();
        services.AddScoped<ICommandHandler<CancelOrderCommand, Result<OrderDetailsResponse>>, CancelOrderCommandHandler>();

        return services;
    }
}
