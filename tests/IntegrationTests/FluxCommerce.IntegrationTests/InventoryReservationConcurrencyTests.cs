using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Inventory.Application.Commands.CommitReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ExpireReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ReleaseReservation;
using FluxCommerce.Modules.Inventory.Application.Commands.ReserveInventory;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Domain.Enums;
using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Mandatory Phase 5 concurrency tests (spec section 5.6). These run against the real SQL Server
/// engine so the atomic conditional reservation and the optimistic concurrency tokens are proven
/// under parallel load: concurrent requests cannot oversell inventory, and lifecycle operations
/// cannot double-release or double-commit.
/// </summary>
[Collection(SqlServerInventoryTestCollection.Name)]
public class InventoryReservationConcurrencyTests
{
    private readonly SqlServerInventoryTestFixture _fixture;

    public InventoryReservationConcurrencyTests(SqlServerInventoryTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- Basic race ----

    [Fact]
    public async Task BasicRace_AvailableOne_TwoConcurrentReservations_ExactlyOneSucceeds()
    {
        var sku = new InventorySku(UniqueSku("RACE")).Value;
        await _fixture.CreateItemAsync(sku, 1);

        var results = await Task.WhenAll(FireReserve(sku, 1), FireReserve(sku, 1));
        var succeeded = results.Count(result => result.IsSuccess);

        Assert.Equal(1, succeeded);
        Assert.Equal(1, results.Count(result => result.IsFailure));
        Assert.Equal(
            "Inventory.InsufficientStock",
            results.Single(result => result.IsFailure).Error.Code);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(1, item.ReservedQuantity);
        Assert.Equal(0, item.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        Assert.Single(reservations, reservation => reservation.Sku.Value == sku);
    }

    // ---- Parallel reservations ----

    [Fact]
    public async Task ParallelReservations_StockBelowTotalDemand_NoOverselling()
    {
        await RunParallelReservationTest(new InventorySku(UniqueSku("PAR-BELOW")).Value, stock: 3, requests: 10, quantityPerRequest: 1);
    }

    [Fact]
    public async Task ParallelReservations_StockEqualToTotalDemand_AllAvailableQuantityReserved()
    {
        await RunParallelReservationTest(new InventorySku(UniqueSku("PAR-EQUAL")).Value, stock: 5, requests: 5, quantityPerRequest: 1);
    }

    [Fact]
    public async Task ParallelReservations_StockGreaterThanTotalDemand_AllRequestsSucceed()
    {
        await RunParallelReservationTest(new InventorySku(UniqueSku("PAR-ABOVE")).Value, stock: 10, requests: 4, quantityPerRequest: 1);
    }

    [Fact]
    public async Task ParallelReservations_ManyConcurrentRequests_CannotOversell()
    {
        await RunParallelReservationTest(new InventorySku(UniqueSku("PAR-MANY")).Value, stock: 5, requests: 50, quantityPerRequest: 1);
    }

    [Fact]
    public async Task ParallelReservations_DifferentSkus_AreIndependent()
    {
        var skuA = new InventorySku(UniqueSku("PAR-A")).Value;
        var skuB = new InventorySku(UniqueSku("PAR-B")).Value;

        await _fixture.CreateItemAsync(skuA, 4);
        await _fixture.CreateItemAsync(skuB, 6);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 30)
                .Select(index => FireReserve(index % 2 == 0 ? skuA : skuB, 1)));

        var skuAResults = results.Where((_, index) => index % 2 == 0).ToArray();
        var skuBResults = results.Where((_, index) => index % 2 == 1).ToArray();

        Assert.Equal(4, skuAResults.Count(result => result.IsSuccess));
        Assert.Equal(6, skuBResults.Count(result => result.IsSuccess));

        var itemA = await _fixture.GetItemAsync(skuA);
        var itemB = await _fixture.GetItemAsync(skuB);

        Assert.Equal(4, itemA.ReservedQuantity);
        Assert.Equal(0, itemA.AvailableQuantity);
        Assert.Equal(6, itemB.ReservedQuantity);
        Assert.Equal(0, itemB.AvailableQuantity);
    }

    // ---- Lifecycle concurrency ----

    [Fact]
    public async Task ConcurrentCommits_OnSameReservation_OnlyOneSucceeds_NoDoubleCommit()
    {
        var sku = new InventorySku(UniqueSku("LIFE-COMMIT")).Value;
        await _fixture.CreateItemAsync(sku, 10);

        var reservationId = await ReserveOne(sku, 3);

        var first = FireLifecycle(LifecycleOperation.Commit, reservationId);
        var second = FireLifecycle(LifecycleOperation.Commit, reservationId);

        var results = await Task.WhenAll(first, second);
        var succeeded = results.Count(result => result.Succeeded);

        Assert.Equal(1, succeeded);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(7, item.OnHandQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(7, item.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        Assert.Single(
            reservations,
            reservation => reservation.Id.Value == reservationId
                && reservation.Status == InventoryReservationStatus.Committed);
    }

    [Fact]
    public async Task ConcurrentReleaseAndExpire_OnSameReservation_OnlyOneSucceeds_NoDoubleRelease()
    {
        var sku = new InventorySku(UniqueSku("LIFE-REL-EXP")).Value;
        await _fixture.CreateItemAsync(sku, 10);

        var reservationId = await ReserveOne(sku, 3);

        var release = FireLifecycle(LifecycleOperation.Release, reservationId);
        var expire = FireLifecycle(LifecycleOperation.Expire, reservationId);

        var results = await Task.WhenAll(release, expire);
        var succeeded = results.Count(result => result.Succeeded);

        Assert.Equal(1, succeeded);

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(10, item.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var reservation = reservations.Single(candidate => candidate.Id.Value == reservationId);
        Assert.True(
            reservation.Status == InventoryReservationStatus.Released
                || reservation.Status == InventoryReservationStatus.Expired,
            $"Expected a terminal state but found {reservation.Status}.");
    }

    [Fact]
    public async Task ConcurrentReleases_OnSameItem_NeverDoubleReleaseOrCorruptStock()
    {
        var sku = new InventorySku(UniqueSku("LIFE-MIXED")).Value;
        const int stock = 10;
        const int reservationQuantity = 3;
        const int reservationCount = 2;

        await _fixture.CreateItemAsync(sku, stock);

        var reservationIds = new List<Guid>();
        for (var index = 0; index < reservationCount; index++)
        {
            reservationIds.Add(await ReserveOne(sku, reservationQuantity));
        }

        // Two releases target different reservations but the same inventory item row. The version
        // token serializes them: at most one succeeds while the others report a conflict. Whatever
        // the interleaving, the stock must never be double-released and the item must stay valid.
        var releaseResults = await Task.WhenAll(
            reservationIds.Select(id => FireLifecycle(LifecycleOperation.Release, id)));

        Assert.True(releaseResults.Count(result => result.Succeeded) >= 1,
            $"Expected at least one successful release but found [{string.Join(",", releaseResults.Select(r => r.ErrorCode ?? "OK"))}].");
        Assert.All(releaseResults, result => Assert.True(result.Succeeded || result.ErrorCode == "Inventory.InvalidStateTransition"));

        var itemAfterRelease = await _fixture.GetItemAsync(sku);
        Assert.True(itemAfterRelease.ReservedQuantity >= 0, "Reserved quantity went negative.");
        Assert.True(itemAfterRelease.AvailableQuantity >= 0, "Available quantity went negative.");
        Assert.Equal(stock - reservationQuantity * releaseResults.Count(result => result.Succeeded), itemAfterRelease.AvailableQuantity);

        var reservations = await _fixture.GetAllReservationsAsync();
        var releasedReservations = reservations
            .Count(candidate => reservationIds.Contains(candidate.Id.Value) && candidate.Status == InventoryReservationStatus.Released);
        Assert.Equal(releaseResults.Count(result => result.Succeeded), releasedReservations);

        // Stock that was actually released can be reserved again; nothing can be reserved twice.
        var reReserveResults = await Task.WhenAll(
            Enumerable.Range(0, releaseResults.Count(result => result.Succeeded))
                .Select(_ => FireReserve(sku, reservationQuantity)));

        Assert.All(reReserveResults, result => Assert.True(result.IsSuccess));
    }

    // ---- Helpers ----

    private Task<Result<InventoryReservationReadModel>> FireReserve(string sku, int quantity) =>
        _fixture.ReserveAsync(new ReserveInventoryCommand(
            Guid.NewGuid(),
            sku,
            quantity,
            DateTimeOffset.UtcNow.AddMinutes(30)));

    private async Task<Guid> ReserveOne(string sku, int quantity)
    {
        var result = await FireReserve(sku, quantity);
        Assert.True(result.IsSuccess, $"Seeding reservation failed for {sku}.");
        return result.Value!.ReservationId;
    }

    /// <summary>
    /// Invokes a lifecycle command handler through the real application path. Concurrent attempts that
    /// lose the optimistic-concurrency race surface as <see cref="DbUpdateConcurrencyException"/>,
    /// which is the engine's way of guaranteeing the losing operation changes nothing.
    /// </summary>
    private Task<LifecycleOutcome> FireLifecycle(LifecycleOperation operation, Guid reservationId) =>
        Task.Run(async () =>
        {
            try
            {
                using var scope = _fixture.Services.CreateScope();
                var services = scope.ServiceProvider;

                Result result = operation switch
                {
                    LifecycleOperation.Release => await ExecuteAsync(
                        services.GetRequiredService<ICommandHandler<ReleaseReservationCommand, Result<InventoryReservationReadModel>>>(),
                        new ReleaseReservationCommand(reservationId)),
                    LifecycleOperation.Commit => await ExecuteAsync(
                        services.GetRequiredService<ICommandHandler<CommitReservationCommand, Result<InventoryReservationReadModel>>>(),
                        new CommitReservationCommand(reservationId)),
                    LifecycleOperation.Expire => await ExecuteAsync(
                        services.GetRequiredService<ICommandHandler<ExpireReservationCommand, Result<InventoryReservationReadModel>>>(),
                        new ExpireReservationCommand(reservationId)),
                    _ => throw new ArgumentOutOfRangeException(nameof(operation))
                };

                return new LifecycleOutcome(result.IsSuccess, result.Error?.Code);
            }
            catch (DbUpdateConcurrencyException)
            {
                return new LifecycleOutcome(false, "DbUpdateConcurrencyException");
            }
        });

    private static async Task<Result> ExecuteAsync<TCommand>(
        ICommandHandler<TCommand, Result<InventoryReservationReadModel>> handler,
        TCommand command)
        where TCommand : ICommand<Result<InventoryReservationReadModel>>
    {
        var result = await handler.HandleAsync(command, CancellationToken.None);
        return new Result(result.IsSuccess, result.Error);
    }

    private async Task RunParallelReservationTest(string sku, int stock, int requests, int quantityPerRequest)
    {
        await _fixture.CreateItemAsync(sku, stock);

        var results = await Task.WhenAll(
            Enumerable.Range(0, requests).Select(_ => FireReserve(sku, quantityPerRequest)));

        var succeeded = results.Count(result => result.IsSuccess);
        var failed = results.Count(result => result.IsFailure);

        Assert.Equal(requests, succeeded + failed);

        var totalReserved = succeeded * quantityPerRequest;
        Assert.True(totalReserved <= stock, $"Oversold! Reserved {totalReserved} from {stock} available.");
        Assert.All(results, result =>
        {
            if (result.IsFailure)
                Assert.Equal("Inventory.InsufficientStock", result.Error.Code);
        });

        var item = await _fixture.GetItemAsync(sku);
        Assert.Equal(totalReserved, item.ReservedQuantity);
        Assert.Equal(stock - totalReserved, item.AvailableQuantity);
        Assert.True(item.AvailableQuantity >= 0, "Available quantity went negative.");

        var reservations = await _fixture.GetAllReservationsAsync();
        Assert.Equal(succeeded, reservations.Count(reservation => reservation.Sku.Value == sku));
    }

    private static string UniqueSku(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];

    private sealed record LifecycleOutcome(bool Succeeded, string? ErrorCode);

    private sealed record Result(bool IsSuccess, Error? Error)
    {
        public bool IsFailure => !IsSuccess;
    }

    private enum LifecycleOperation
    {
        Release,
        Commit,
        Expire,
    }
}
