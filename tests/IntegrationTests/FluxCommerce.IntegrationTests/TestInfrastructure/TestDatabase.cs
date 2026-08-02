using FluxCommerce.Modules.Identity.Application.Emailing;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Enums;
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

    public static UserStatus GetUserStatus(IdentityWebApplicationFactory factory, string email) =>
        Query(factory, user => user.Status, email);

    public static EmailMessage GetLastEmail(IdentityWebApplicationFactory factory, string toEmail) =>
        factory.EmailSender.Sent.Last(message => message.ToEmail == toEmail);

    public static int CountVerificationTokenRows(IdentityWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FluxCommerce.Modules.Identity.Infrastructure.DbContexts.IdentityDbContext>();

        var userId = dbContext.Set<User>()
            .Single(user => user.Email.Value == email)
            .Id;

        return dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM EmailVerificationTokens WHERE UserId = {userId}")
            .Single();
    }

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
