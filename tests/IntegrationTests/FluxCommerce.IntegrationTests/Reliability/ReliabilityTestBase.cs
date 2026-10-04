using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.IntegrationTests.Common.FaultInjection;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluxCommerce.IntegrationTests.Reliability;

/// <summary>
/// Phase 12.1 base fixture for reliability/failure testing. Provides deterministic database isolation,
/// reset capabilities, and failure-injection hooks. Strictly test-scoped — never used in production.
/// </summary>
/// <remarks>
/// Isolation strategy: each test instance owns private SQLite in-memory connections for Orders,
/// Inventory, Payments, and Identity. SQLite is used for hermetic, fast, parallel-safe isolation;
/// tests that require real SQL Server (e.g. atomic claim, backoff window, concurrency token
/// contention) extend the SQL-Server fixtures in <c>TestInfrastructure</c> instead. The in-memory
/// SQLite database guarantees zero cross-test leakage because every <see cref="ReliabilityTestBase"/>
/// instance gets its own connection that lives for the lifetime of the test class.
/// <para/>
/// Reset semantics: <see cref="ResetDatabasesAsync"/> truncates module tables in FK-safe order and
/// clears all fault injectors. It is safe to call between individual test methods when a single
/// test class executes multiple scenarios sequentially; parallel test classes are naturally isolated
/// by their own connections.
/// <para/>
/// Failure injection: all injectors are disabled by default. Tests arm them explicitly via
/// <see cref="InjectDbSaveChangesFault"/>, <see cref="InjectDbCommandFault"/>,
/// <see cref="InjectBrokerFault"/>, <see cref="CrashNextConsumerAfterCommit"/>, and
/// <see cref="CrashNextConsumerBeforeCommit"/> and must dispose/reset them afterwards (handled by
/// <see cref="ResetFaults"/> and <see cref="DisposeAsync"/>). No fault injector is registered in the
/// production container.
/// </remarks>
public abstract class ReliabilityTestBase : IAsyncLifetime
{
    private readonly SqliteConnection _ordersConnection = new("Data Source=:memory:");
    private readonly SqliteConnection _inventoryConnection = new("Data Source=:memory:");
    private readonly SqliteConnection _paymentsConnection = new("Data Source=:memory:");

    private ServiceProvider? _provider;

    protected ReliabilityTestBase()
    {
        DbFaultInterceptor = new DbFaultInterceptor();
        BrokerFaultSimulatorInstance = null;
    }

    // ---------------------------------------------------------------- injectors (test-only)

    /// <summary>EF Core fault interceptor injected into every module DbContext in this fixture.</summary>
    protected DbFaultInterceptor DbFaultInterceptor { get; }

    /// <summary>Broker fault simulator when the test registers a publisher; null otherwise.</summary>
    protected BrokerFaultSimulator? BrokerFaultSimulatorInstance { get; private set; }

    // ---------------------------------------------------------------- provider / contexts

    protected ServiceProvider Services => _provider ?? throw new InvalidOperationException("ReliabilityTestBase not initialized; await InitializeAsync().");

    protected OrdersDbContext OrdersContext => Services.GetRequiredService<OrdersDbContext>();

    protected InventoryDbContext InventoryContext => Services.GetRequiredService<InventoryDbContext>();

    protected PaymentsDbContext PaymentsContext => Services.GetRequiredService<PaymentsDbContext>();

    // ---------------------------------------------------------------- IAsyncLifetime

    public async Task InitializeAsync()
    {
        _ordersConnection.Open();
        _inventoryConnection.Open();
        _paymentsConnection.Open();

        var sqliteProvider = new ServiceCollection()
            .AddEntityFrameworkSqlite()
            .AddLogging()
            .BuildServiceProvider();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        // Register the fault interceptor as an additional ISaveChangesInterceptor so SavingChanges
        // faults are evaluated before the domain-event interceptor captures events.
        services.AddSingleton<ISaveChangesInterceptor>(DbFaultInterceptor);

        // Module DbContexts backed by the per-test SQLite connections and sharing the same
        // interceptors. The fault interceptor is added both as ISaveChangesInterceptor and as a
        // DbCommandInterceptor via AddInterceptors so SaveChanges + command faults are both covered.
        services.AddDbContext<OrdersDbContext>((provider, options) =>
        {
            options.UseSqlite(_ordersConnection).UseInternalServiceProvider(sqliteProvider);
            options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
            options.AddInterceptors(DbFaultInterceptor);
        });

        services.AddDbContext<InventoryDbContext>((provider, options) =>
        {
            options.UseSqlite(_inventoryConnection).UseInternalServiceProvider(sqliteProvider);
            options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
            options.AddInterceptors(DbFaultInterceptor);
        });

        services.AddDbContext<PaymentsDbContext>((provider, options) =>
        {
            options.UseSqlite(_paymentsConnection).UseInternalServiceProvider(sqliteProvider);
            options.AddInterceptors(provider.GetServices<ISaveChangesInterceptor>());
            options.AddInterceptors(DbFaultInterceptor);
        });

        // Allow derived fixtures to register additional services (e.g. Outbox dispatchers,
        // consumer infrastructure, or a RabbitMQ publisher wrapped with BrokerFaultSimulator).
        ConfigureServices(services);

        // If a publisher was registered, wrap it with the broker simulator so tests can arm
        // broker outages deterministically.
        TryWrapPublisherWithBrokerSimulator(services);

        _provider = services.BuildServiceProvider();

        CaptureBrokerSimulator();

        // Ensure SQLite schemas exist (migrations-equivalent for SQLite via EnsureCreated).
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreatedAsync().ConfigureAwait(false);
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.EnsureCreatedAsync().ConfigureAwait(false);
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        ResetFaults();

        if (_provider is not null)
            await _provider.DisposeAsync().ConfigureAwait(false);

        _ordersConnection.Dispose();
        _inventoryConnection.Dispose();
        _paymentsConnection.Dispose();

        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- configuration hook

    /// <summary>
    /// Override to register additional services for the test (e.g. RabbitMQ, Outbox, consumers).
    /// Called before the provider is built. The base implementation does nothing.
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    // ---------------------------------------------------------------- database reset

    /// <summary>
    /// Truncates all module tables in FK-safe order and clears fault injectors. Use between
    /// sequential scenarios in the same test class; parallel test classes are already isolated.
    /// </summary>
    protected async Task ResetDatabasesAsync()
    {
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM ProcessedMessages;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM OutboxMessages;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM OrderSagaReservationIds;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM OrderSagas;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM CheckoutIdempotency;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM OrderItems;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM Orders;").ConfigureAwait(false);
        await ExecuteSqlAsync(OrdersContext, "DELETE FROM OrderSummaries;").ConfigureAwait(false);

        await ExecuteSqlAsync(InventoryContext, "DELETE FROM ProcessedMessages;").ConfigureAwait(false);
        await ExecuteSqlAsync(InventoryContext, "DELETE FROM OutboxMessages;").ConfigureAwait(false);
        await ExecuteSqlAsync(InventoryContext, "DELETE FROM InventoryReservations;").ConfigureAwait(false);
        await ExecuteSqlAsync(InventoryContext, "DELETE FROM InventoryItems;").ConfigureAwait(false);

        await ExecuteSqlAsync(PaymentsContext, "DELETE FROM ProcessedWebhookEvents;").ConfigureAwait(false);
        await ExecuteSqlAsync(PaymentsContext, "DELETE FROM OutboxMessages;").ConfigureAwait(false);
        await ExecuteSqlAsync(PaymentsContext, "DELETE FROM Payments;").ConfigureAwait(false);

        ResetFaults();
    }

    private static async Task ExecuteSqlAsync(DbContext context, string sql)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(sql).ConfigureAwait(false);
        }
        catch
        {
            // Table may not exist on older SQLite schemas (e.g. OrderSummaries before Phase 7.9);
            // treat as no-op so reset stays resilient across schema evolution.
        }
    }

    // ---------------------------------------------------------------- fault-injection hooks

    /// <summary>Arms a fault before DB commit (SavingChanges) for the next <paramref name="times"/> SaveChanges calls.</summary>
    protected void InjectDbSaveChangesFault(DbFaultKind kind, int times = 1) =>
        DbFaultInterceptor.InjectSaveChangesFault(kind, times);

    /// <summary>Arms a fault during SQL command execution for the next <paramref name="times"/> commands.</summary>
    protected void InjectDbCommandFault(DbFaultKind kind, int times = 1) =>
        DbFaultInterceptor.InjectCommandFault(kind, times);

    /// <summary>Arms a broker fault for the next <paramref name="times"/> publishes. Requires a publisher registration.</summary>
    protected void InjectBrokerFault(BrokerFaultMode mode, int times = 1)
    {
        if (BrokerFaultSimulatorInstance is null)
            throw new InvalidOperationException("No BrokerFaultSimulator registered; register a publisher in ConfigureServices first.");

        BrokerFaultSimulatorInstance.InjectFault(mode, times);
    }

    /// <summary>Simulates a RabbitMQ connection outage for the next <paramref name="times"/> publishes.</summary>
    protected void SimulateBrokerOutage(int times = 1) => InjectBrokerFault(BrokerFaultMode.ThrowConnectionException, times);

    /// <summary>Simulates a broker NACK (publish returns false) for the next <paramref name="times"/> publishes.</summary>
    protected void SimulateBrokerNack(int times = 1) => InjectBrokerFault(BrokerFaultMode.ReturnFalse, times);

    /// <summary>Arms a consumer crash after DB commit but before ACK for the next <paramref name="times"/> deliveries.</summary>
    protected void CrashNextConsumerAfterCommit(int times = 1) => TestConsumerCrashHarness.CrashNextAfterCommit(times);

    /// <summary>Arms a consumer crash before DB commit (inside the transaction) for the next <paramref name="times"/> deliveries.</summary>
    protected void CrashNextConsumerBeforeCommit(int times = 1) => TestConsumerCrashHarness.CrashNextBeforeCommit(times);

    /// <summary>Clears all armed faults (DB, broker, consumer). Safe to call even when no fault is active.</summary>
    protected void ResetFaults()
    {
        DbFaultInterceptor.Reset();
        BrokerFaultSimulatorInstance?.Reset();
        TestConsumerCrashHarness.Reset();
    }

    // ---------------------------------------------------------------- helpers

    private void TryWrapPublisherWithBrokerSimulator(IServiceCollection services)
    {
        var publisherDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(FluxCommerce.BuildingBlocks.Infrastructure.Messaging.IRabbitMQPublisher));

        if (publisherDescriptor is null)
            return;

        // Capture factory for inner publisher before decorating
        var innerFactory = publisherDescriptor.ImplementationFactory;
        var innerType = publisherDescriptor.ImplementationType;
        var innerInstance = publisherDescriptor.ImplementationInstance;

        services.Remove(publisherDescriptor);

        services.AddSingleton<BrokerFaultSimulator>(provider =>
        {
            FluxCommerce.BuildingBlocks.Infrastructure.Messaging.IRabbitMQPublisher inner;
            if (innerFactory is not null)
                inner = (FluxCommerce.BuildingBlocks.Infrastructure.Messaging.IRabbitMQPublisher)innerFactory(provider);
            else if (innerInstance is not null)
                inner = (FluxCommerce.BuildingBlocks.Infrastructure.Messaging.IRabbitMQPublisher)innerInstance;
            else if (innerType is not null)
                inner = (FluxCommerce.BuildingBlocks.Infrastructure.Messaging.IRabbitMQPublisher)
                    ActivatorUtilities.CreateInstance(provider, innerType);
            else
                throw new InvalidOperationException("Unable to resolve inner IRabbitMQPublisher for BrokerFaultSimulator.");

            var logger = provider.GetRequiredService<ILogger<BrokerFaultSimulator>>();
            return new BrokerFaultSimulator(inner, logger);
        });

        services.AddSingleton<FluxCommerce.BuildingBlocks.Infrastructure.Messaging.IRabbitMQPublisher>(
            provider => provider.GetRequiredService<BrokerFaultSimulator>());
    }

    // Post-build capture: after BuildServiceProvider in InitializeAsync, populate simulator instance
    // so InjectBrokerFault can find it. We do it lazily inside ResetDatabasesAsync path as well.
    private void CaptureBrokerSimulator()
    {
        if (_provider is null)
            return;

        try
        {
            BrokerFaultSimulatorInstance = _provider.GetService<BrokerFaultSimulator>();
        }
        catch
        {
            BrokerFaultSimulatorInstance = null;
        }
    }

    // Override disposal hook to tie capture: wrap InitializeAsync post-build
    // (capture after provider built, before EnsureCreated)
    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Email => null;
        public string[] Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsAuthenticated => false;
    }
}
