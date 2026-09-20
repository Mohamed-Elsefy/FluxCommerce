using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Idempotency.Detection;
using FluxCommerce.BuildingBlocks.Idempotency.Processing;
using FluxCommerce.BuildingBlocks.Idempotency.Stores;
using FluxCommerce.BuildingBlocks.Outbox.Configuration;
using FluxCommerce.BuildingBlocks.Outbox.Mapping;
using FluxCommerce.BuildingBlocks.Outbox.Stores;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;
using FluxCommerce.Modules.Payments.Application.Services;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using FluxCommerce.Modules.Payments.Infrastructure.Background;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.Gateways;
using FluxCommerce.Modules.Payments.Infrastructure.Idempotency;
using FluxCommerce.Modules.Payments.Infrastructure.Outbox;
using FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
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

        services.AddSingleton<IntegrationEventOutboxMessageFactory>();

        // Phase 10.3 Payments outbox dispatcher (publishes payments.payment_succeeded/failed)
        services.AddScoped<PaymentsOutboxStore>();
        services.AddScoped<IOutboxStore, PaymentsOutboxStore>();
        services.AddOptions<OutboxDispatchOptions>();
        services.AddScoped<PaymentsOutboxDispatcher>();
        services.AddHostedService<PaymentsOutboxDispatcher>();

        // Phase 10.3 Payments idempotent consumer infrastructure for create_payment_requested
        services.AddScoped<PaymentsIdempotencyStore>();
        services.AddScoped<PaymentsDuplicateDetector>();
        services.AddScoped<PaymentsIdempotentProcessor>();
        services.AddScoped<IIdempotencyStore>(sp => sp.GetRequiredService<PaymentsIdempotencyStore>());
        services.AddScoped<IDuplicateDetector>(sp => sp.GetRequiredService<PaymentsDuplicateDetector>());
        services.AddScoped<IIdempotentProcessor>(sp => sp.GetRequiredService<PaymentsIdempotentProcessor>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IStripePaymentGateway, StripePaymentGateway>();
        services.AddScoped<IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>>, GetPaymentInitializationQueryHandler>();
        services.AddSingleton<IPaymentWebhookInterpreter, PaymentWebhookInterpreter>();
        services.AddScoped<IStripeWebhookService, StripeWebhookService>();
        services.AddScoped<IWebhookEventProcessor, WebhookEventProcessor>();

        return services;
    }
}
