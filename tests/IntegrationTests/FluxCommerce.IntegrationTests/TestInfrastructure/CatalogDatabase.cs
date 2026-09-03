using FluxCommerce.Modules.Catalog.Domain.Aggregates;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

public static class CatalogDatabase
{
    public static string GetVerificationToken(CatalogWebApplicationFactory factory, string email) =>
        QueryIdentity(factory, user => user.VerificationToken!.Value, email);

    public static UserStatus GetUserStatus(CatalogWebApplicationFactory factory, string email) =>
        QueryIdentity(factory, user => user.Status, email);

    public static int CountCategories(CatalogWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        return dbContext.Set<Category>().Count();
    }

    public static void ResetCatalogDatabase(CatalogWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        dbContext.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");

        dbContext.Database.ExecuteSqlRaw("DELETE FROM ProductAttributeValues");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM ProductSpecifications");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM ProductImages");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM Variants");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM AttributeDefinitions");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM Products");
        dbContext.Database.ExecuteSqlRaw("DELETE FROM Categories");

        dbContext.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
    }

    private static TResult QueryIdentity<TResult>(
        CatalogWebApplicationFactory factory,
        Func<User, TResult> selector,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FluxCommerce.Modules.Identity.Infrastructure.DbContexts.IdentityDbContext>();

        var user = dbContext.Set<User>()
            .Single(item => item.Email.Value == email);

        return selector(user);
    }
}
