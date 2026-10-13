using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using FluxCommerce.Modules.Identity.Infrastructure.Persistence.Seeding;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.IntegrationTests;

public class IdentitySeederTests
{
    private static (ServiceProvider provider, SqliteConnection connection) CreateServiceProvider(IdentitySeederOptions options)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(NullLogger<IdentitySeeder>.Instance);
        services.AddSingleton<FluxCommerce.BuildingBlocks.Application.Abstractions.IDateTimeProvider>(new FakeDateTimeProvider());
        services.AddSingleton<FluxCommerce.BuildingBlocks.Application.Abstractions.ICurrentUser>(new FakeCurrentUser());
        services.AddScoped<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>(_ => new FakeSaveChangesInterceptor());

        var sqliteProvider = new ServiceCollection()
            .AddEntityFrameworkSqlite()
            .AddLogging()
            .BuildServiceProvider();

        services.AddDbContext<IdentityDbContext>(o => o.UseSqlite(connection).UseInternalServiceProvider(sqliteProvider));

        services.AddScoped<FluxCommerce.Modules.Identity.Application.Interfaces.Repositories.IUserRepository, FluxCommerce.Modules.Identity.Infrastructure.Repositories.UserRepository>();
        services.AddScoped<FluxCommerce.Modules.Identity.Application.Interfaces.Repositories.IRoleRepository, FluxCommerce.Modules.Identity.Infrastructure.Repositories.RoleRepository>();
        services.AddScoped<FluxCommerce.BuildingBlocks.Application.Abstractions.IUnitOfWork, FluxCommerce.Modules.Identity.Infrastructure.UnitOfWork>();
        services.AddSingleton<FluxCommerce.Modules.Identity.Application.Interfaces.Security.IPasswordHasher, FluxCommerce.Modules.Identity.Infrastructure.Security.PasswordHasher>();

        services.AddSingleton<IOptions<IdentitySeederOptions>>(Options.Create(options));

        services.AddScoped<IdentitySeeder>();

        var provider = services.BuildServiceProvider();
        return (provider, connection);
    }

    [Fact]
    public async Task SeedAsync_Idempotent_SecondRunDoesNotCreateDuplicate()
    {
        var options = new IdentitySeederOptions
        {
            Enabled = true,
            SuperAdmin = new IdentitySeederOptions.SuperAdminOptions
            {
                Email = "admin@flux.local",
                Password = "Test123!",
                FirstName = "System",
                LastName = "Admin"
            }
        };

        var (provider, connection) = CreateServiceProvider(options);
        using (connection)
        {
            var seeder = provider.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync();

            using var scope1 = provider.CreateScope();
            var db1 = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count1 = await db1.Set<User>().CountAsync(u => u.Email.Value == "admin@flux.local");
            Assert.Equal(1, count1);
            var roleCount1 = await db1.Set<Role>().CountAsync();
            Assert.Equal(3, roleCount1); // Customer, Admin, SuperAdmin seeded

            // Second run — should be idempotent
            var seeder2 = provider.GetRequiredService<IdentitySeeder>();
            await seeder2.SeedAsync();

            using var scope2 = provider.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count2 = await db2.Set<User>().CountAsync(u => u.Email.Value == "admin@flux.local");
            Assert.Equal(1, count2);
            var roleCount2 = await db2.Set<Role>().CountAsync();
            Assert.Equal(3, roleCount2);
        }
    }

    [Fact]
    public async Task SeedAsync_Disabled_DoesNotCreateUser()
    {
        var options = new IdentitySeederOptions
        {
            Enabled = false,
            SuperAdmin = new IdentitySeederOptions.SuperAdminOptions
            {
                Email = "admin@flux.local",
                Password = "Test123!",
                FirstName = "System",
                LastName = "Admin"
            }
        };

        var (provider, connection) = CreateServiceProvider(options);
        using (connection)
        {
            var seeder = provider.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync();

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count = await db.Set<User>().CountAsync(u => u.Email.Value == "admin@flux.local");
            Assert.Equal(0, count);
            // Roles still seeded even when seeder disabled? Our current seeder seeds roles before Enabled check — so roles should be 3 even when disabled.
            var roleCount = await db.Set<Role>().CountAsync();
            Assert.Equal(3, roleCount);
        }
    }

    [Fact]
    public async Task SeedAsync_AlreadyExists_SkipsCreation()
    {
        var options = new IdentitySeederOptions
        {
            Enabled = true,
            SuperAdmin = new IdentitySeederOptions.SuperAdminOptions
            {
                Email = "existing@flux.local",
                Password = "Test123!",
                FirstName = "System",
                LastName = "Admin"
            }
        };

        var (provider, connection) = CreateServiceProvider(options);
        using (connection)
        {
            // Pre-create user manually
            using var scope0 = provider.CreateScope();
            var db0 = scope0.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db0.Database.EnsureCreatedAsync();
            var hasher = scope0.ServiceProvider.GetRequiredService<FluxCommerce.Modules.Identity.Application.Interfaces.Security.IPasswordHasher>();
            var user = User.Register(Guid.NewGuid(), new Email("existing@flux.local"), hasher.Hash("Test123!"), "System", "Admin", DateTime.UtcNow, [Roles.SuperAdmin], null, null);
            // Manually verify to make it Active (seeder does verification)
            var token = Guid.NewGuid().ToString("N");
            var userWithToken = User.Register(Guid.NewGuid(), new Email("dummy@flux.local"), hasher.Hash("Test123!"), "A", "B", DateTime.UtcNow, [Roles.Customer], token, DateTime.UtcNow.AddHours(24));
            // Instead, just add the existing user directly via context
            db0.Set<User>().Add(user);
            await db0.SaveChangesAsync();

            var seeder = provider.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync();

            using var scope1 = provider.CreateScope();
            var db1 = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count = await db1.Set<User>().CountAsync(u => u.Email.Value == "existing@flux.local");
            Assert.Equal(1, count);
        }
    }

    private sealed class FakeDateTimeProvider : FluxCommerce.BuildingBlocks.Application.Abstractions.IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class FakeCurrentUser : FluxCommerce.BuildingBlocks.Application.Abstractions.ICurrentUser
    {
        public Guid? UserId => null;
        public string? Email => null;
        public string[] Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsAuthenticated => false;
    }

    private sealed class FakeSaveChangesInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor { }
}
