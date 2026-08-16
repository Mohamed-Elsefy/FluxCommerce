using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Inventory.Application.Commands.AdjustStock;
using FluxCommerce.Modules.Inventory.Application.Commands.CommitReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Commands.ExpireReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.Expiration;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryReservation;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Idempotency;
using FluxCommerce.Modules.Inventory.Infrastructure.Outbox;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, string connectionString)
        => services.AddInventoryInfrastructure(connectionString, configureExpiration: null);

    public static IServiceCollection AddInventoryInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Action<ReservationExpirationOptions>? configureExpiration)
    {
        // Fallback correlation provider keeps the module's persistence stack self-contained; the API
        // host overrides it with the web-backed provider (last registration wins).
        services.AddSingleton<ICorrelationIdProvider, GuidCorrelationIdProvider>();

        services.AddSingleton<InventoryOutboxMessageFactory>();
        services.AddSingleton<InventoryOutboxSaveChangesInterceptor>();
        services.AddScoped<InventoryOutboxStore>();
        services.AddScoped<IOutboxStore, InventoryOutboxStore>();

        services.AddDbContext<InventoryDbContext>((serviceProvider, options) =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Inventory");
                sqlOptions.MigrationsAssembly(typeof(InventoryDbContext).Assembly.GetName().Name);
            })
            .AddInterceptors(serviceProvider.GetRequiredService<InventoryOutboxSaveChangesInterceptor>()));

        services.AddScoped<IInventoryStore, InventoryStore>();
        services.AddScoped<IIdempotencyStore, InventoryIdempotencyStore>();
        services.AddScoped<IDuplicateDetector, InventoryDuplicateDetector>();
        services.AddScoped<IIdempotentProcessor, InventoryIdempotentProcessor>();
        services.AddScoped<ICommandHandler<CreateInventoryItemCommand, Result<InventoryItemReadModel>>, CreateInventoryItemCommandHandler>();
        services.AddScoped<ICommandHandler<AdjustStockCommand, Result<InventoryItemReadModel>>, AdjustStockCommandHandler>();
        services.AddScoped<IQueryHandler<GetInventoryItemQuery, Result<InventoryItemReadModel>>, GetInventoryItemQueryHandler>();
        services.AddScoped<IQueryHandler<GetInventoryReservationQuery, Result<InventoryReservationReadModel>>, GetInventoryReservationQueryHandler>();
        services.AddScoped<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>, ReserveInventoryCommandHandler>();
        services.AddScoped<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>, ReleaseReservationCommandHandler>();
        services.AddScoped<ICommandHandler<CommitReservationCommand, Result<InventoryReservationReadModel>>, CommitReservationCommandHandler>();
        services.AddScoped<ICommandHandler<ExpireReservationCommand, Result<InventoryReservationReadModel>>, ExpireReservationCommandHandler>();

        services.AddScoped<IExpiredReservationProcessor, ExpiredReservationProcessor>();

        services.AddOptions<ReservationExpirationOptions>();
        services.AddOptions<OutboxDispatchOptions>();

        if (configureExpiration is not null)
            services.Configure(configureExpiration);

        services.AddHostedService<ReservationExpirationBackgroundService>();
        services.AddScoped<InventoryOutboxDispatcher>();
        services.AddHostedService<InventoryOutboxDispatcher>();

        return services;
    }
}
