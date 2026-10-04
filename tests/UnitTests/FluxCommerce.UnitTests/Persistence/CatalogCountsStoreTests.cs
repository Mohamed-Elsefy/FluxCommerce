using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using FluxCommerce.Modules.Catalog.Infrastructure.Repositories;
using FluxCommerce.UnitTests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FluxCommerce.UnitTests.Persistence;

/// <summary>
/// Coverage for the administrative catalog overview: scalar counts exclude soft-deleted rows
/// and the active-product count requires the active lifecycle status. Runs on SQLite to prove
/// the queries translate outside SQL Server.
/// </summary>
public sealed class CatalogCountsStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public CatalogCountsStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    [Fact]
    public async Task GetCountsAsync_ExcludesDeleted_AndCountsActiveSeparately()
    {
        await using (var seed = CreateContext())
        {
            var category = TestCategories.Create(name: "Phones", slug: "phones");
            seed.Categories.Add(category);
            seed.Categories.Add(TestCategories.CreateDeleted(name: "Gone", slug: "gone"));

            var active = TestProducts.Create(name: "Active Phone", slug: "active-phone", categoryId: category.Id);
            active.Activate();
            seed.Products.Add(active);
            seed.Products.Add(TestProducts.Create(name: "Draft Phone", slug: "draft-phone", categoryId: category.Id));
            var gone = TestProducts.Create(name: "Gone Phone", slug: "gone-phone", categoryId: category.Id);
            gone.SoftDelete(TestProducts.UtcNow);
            seed.Products.Add(gone);

            seed.Variants.Add(TestVariants.Create(productId: active.Id, sku: "ACTIVE-256GB", name: "256GB"));
            seed.Variants.Add(TestVariants.CreateDeleted(productId: active.Id, sku: "GONE-128GB", name: "Gone"));

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var store = new CatalogCountsStore(context);

        var counts = await store.GetCountsAsync();

        Assert.Equal(2, counts.TotalProducts);
        Assert.Equal(1, counts.ActiveProducts);
        Assert.Equal(1, counts.TotalCategories);
        Assert.Equal(1, counts.TotalVariants);
    }

    [Fact]
    public async Task GetCountsAsync_EmptyDatabase_ReturnsZeros()
    {
        await using var context = CreateContext();
        var store = new CatalogCountsStore(context);

        var counts = await store.GetCountsAsync();

        Assert.Equal(0, counts.TotalProducts);
        Assert.Equal(0, counts.ActiveProducts);
        Assert.Equal(0, counts.TotalCategories);
        Assert.Equal(0, counts.TotalVariants);
    }

    public void Dispose() => _connection.Dispose();

    private CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new CatalogDbContext(
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
