using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

public static class TestDatabase
{
    public static string GetVerificationToken(IdentityWebApplicationFactory factory, string email) =>
        Query(factory, user => user.VerificationToken!.Value, email);

    public static string GetPasswordResetToken(IdentityWebApplicationFactory factory, string email) =>
        Query(factory, user => user.ResetToken!.Value, email);

    public static Guid GetUserId(IdentityWebApplicationFactory factory, string email) =>
        Query(factory, user => user.Id, email);

    private static TResult Query<TResult>(
        IdentityWebApplicationFactory factory,
        Func<User, TResult> selector,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FluxCommerce.Modules.Identity.Infrastructure.DbContexts.IdentityDbContext>();

        return dbContext.Set<User>()
            .Single(user => user.Email.Value == email)
            .Let(selector);
    }
}

internal static class ObjectExtensions
{
    public static TResult Let<TSource, TResult>(this TSource source, Func<TSource, TResult> selector) =>
        selector(source);
}
