using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using FluxCommerce.Modules.Inventory.Infrastructure.Persistence.Stores;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Guard tests for <see cref="InventoryStore.GetExpiredReservationIdsAsync"/>. The functional
/// filtering (pending-only, past-deadline, oldest-first, bounded) is exercised against the real SQL
/// Server engine in <c>InventoryReservationExpirationTests</c> because the SQLite provider used here
/// cannot translate <c>DateTimeOffset</c> comparisons — a known project limitation (the Orders module
/// works around it the same way). Only the behaviour that must not touch the database is covered here.
/// </summary>
public class InventoryExpirationStoreTests : IDisposable
{
    private static readonly DateTimeOffset UtcNow = new(2026, 2, 10, 8, 15, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public InventoryExpirationStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task GetExpiredReservationIdsAsync_ZeroMaxResults_Throws()
    {
        await using var context = CreateContext();
        var store = new InventoryStore(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.GetExpiredReservationIdsAsync(UtcNow, 0, CancellationToken.None));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new InventoryDbContext(
            options,
            new FakeDateTimeProvider(),
            new FakeCurrentUser(),
            new FakeSaveChangesInterceptor());

        context.Database.EnsureCreated();

        return context;
    }

    private sealed class FakeSaveChangesInterceptor : SaveChangesInterceptor
    {
    }
}
