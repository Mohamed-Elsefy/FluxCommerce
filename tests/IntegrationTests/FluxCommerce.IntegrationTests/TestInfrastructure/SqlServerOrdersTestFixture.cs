using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Infrastructure.Services;
using FluxCommerce.BuildingBlocks.Persistence.Interceptors;
using FluxCommerce.Modules.Orders.Infrastructure;
using FluxCommerce.Modules.Orders.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Host for the Phase 6.3 Orders outbox integration tests. The real SQL Server container is used so
/// the Business State + Outbox atomic commit and rollback are exercised by the actual database
/// engine. Schema comes from the module migrations. Every operation runs in its own DI scope,
/// mirroring a per-request DbContext lifetime.
/// </summary>
public sealed class SqlServerOrdersTestFixture : IDisposable
{
    public const string ConnectionString =
        "Server=localhost,1433;Database=FluxCommerce_Orders;User Id=sa;Password=FluxCommerceDBPass3030;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly ServiceProvider _provider;

    public SqlServerOrdersTestFixture()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<ISaveChangesInterceptor, DomainEventInterceptor>();

        services.AddOrdersInfrastructure(ConnectionString);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        context.Database.Migrate();
    }

    public IServiceProvider Services => _provider;

    public void Dispose()
    {
        _provider.Dispose();
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;

        public string[] Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public bool IsAuthenticated => false;
    }
}