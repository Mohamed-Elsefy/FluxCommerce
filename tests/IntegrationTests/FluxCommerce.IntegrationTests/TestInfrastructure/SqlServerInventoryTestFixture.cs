using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Inventory.Application.Commands.CreateInventoryItem;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.Expiration;
using FluxCommerce.Modules.Inventory.Application.Interfaces;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.Aggregates;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.Modules.Inventory.Infrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Host for the mandatory Phase 5 concurrency tests. The real SQL Server container is used (not
/// SQLite) so the atomic conditional UPDATE and the optimistic concurrency tokens are exercised by
/// the actual database engine under parallel load. Schema is created from the module migration.
/// Every operation runs in its own DI scope, mirroring a per-request DbContext lifetime.
/// </summary>
public sealed class SqlServerInventoryTestFixture : IDisposable
{
    public const string ConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Inventory;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly ServiceProvider _provider;

    public SqlServerInventoryTestFixture()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        services.AddInventoryInfrastructure(ConnectionString);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        context.Database.Migrate();
    }

    /// <summary>
    /// Runs a reservation against a freshly created DI scope so every concurrent call has its own
    /// DbContext and connection, exactly like independent HTTP requests would.
    /// </summary>
    public Task<Result<InventoryReservationReadModel>> ReserveAsync(
        ReserveInventoryCommand command) =>
        RunScopedAsync<ICommandHandler<ReserveInventoryCommand, Result<InventoryReservationReadModel>>, Result<InventoryReservationReadModel>>(
            handler => handler.HandleAsync(command, CancellationToken.None));

    /// <summary>
    /// Creates an inventory item and returns the normalized (trimmed, upper-cased) SKU that the
    /// domain actually persisted, so tests always compare against the exact stored value.
    /// </summary>
    public async Task<string> CreateItemAsync(string sku, int onHandQuantity)
    {
        var result = await RunScopedAsync<ICommandHandler<CreateInventoryItemCommand, Result<InventoryItemReadModel>>, Result<InventoryItemReadModel>>(
            handler => handler.HandleAsync(new CreateInventoryItemCommand(sku, onHandQuantity), CancellationToken.None));

        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to create inventory item for SKU '{sku}': {result.Error.Code}");

        return result.Value!.Sku;
    }

    public Task<InventoryItemReadModel> GetItemAsync(string sku) =>
        Task.Run(() =>
        {
            using var scope = _provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            var item = context.InventoryItems
                .AsNoTracking()
                .Single(candidate => candidate.Id == new InventorySku(sku));

            return new InventoryItemReadModel(
                item.Sku.Value,
                item.OnHandQuantity,
                item.ReservedQuantity,
                item.AvailableQuantity,
                item.Version,
                item.UpdatedAtUtc);
        });

    public Task<IReadOnlyList<InventoryReservation>> GetAllReservationsAsync() =>
        Task.Run(() =>
        {
            using var scope = _provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

            IReadOnlyList<InventoryReservation> reservations = context.InventoryReservations
                .AsNoTracking()
                .ToList()
                .AsReadOnly();

            return reservations;
        });

    /// <summary>
    /// Seeds an inventory item together with a reservation in the requested status and deadline, so a
    /// test can deterministically produce expired/committed/released states that the real store query
    /// and the expiration processor would observe. Returns the reservation's id.
    /// </summary>
    public async Task<Guid> SeedReservationAsync(
        string sku,
        int onHand,
        int quantity,
        DateTimeOffset expiresAt,
        InventoryReservationStatus status = InventoryReservationStatus.Pending,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        var item = InventoryItem.Create(new InventorySku(sku.ToUpperInvariant()), onHand, now);

        switch (status)
        {
            case InventoryReservationStatus.Committed:
                item.Reserve(quantity, now);
                item.Commit(quantity, now);
                break;
            case InventoryReservationStatus.Released:
            case InventoryReservationStatus.Expired:
                item.Reserve(quantity, now);
                item.Release(quantity, now);
                break;
            case InventoryReservationStatus.Pending:
                item.Reserve(quantity, now);
                break;
        }

        var reservation = InventoryReservation.Hydrate(
            InventoryReservationId.Create(),
            OrderId.Create(),
            item.Sku,
            quantity,
            status,
            expiresAt,
            InventoryReservation.InitialVersion,
            now,
            now);

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        context.InventoryItems.Add(item);
        context.InventoryReservations.Add(reservation);
        await context.SaveChangesAsync();

        return reservation.Id.Value;
    }

    /// <summary>
    /// Runs a single expiration cycle against the real processor and store within its own DI scope.
    /// </summary>
    public Task<int> RunExpirationProcessorAsync(int maxResults) =>
        RunScopedAsync<IExpiredReservationProcessor, int>(
            processor => processor.ProcessExpiredReservationsAsync(maxResults, CancellationToken.None));

    public IServiceProvider Services => _provider;

    private async Task<TResult> RunScopedAsync<THandler, TResult>(Func<THandler, Task<TResult>> operation)
        where THandler : notnull
    {
        using var scope = _provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        return await operation(handler).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;

        public string[] Roles => [];

        public bool IsAuthenticated => false;
    }
}
