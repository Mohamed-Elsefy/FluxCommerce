using FluxCommerce.Modules.Identity.Application.Commands.Logout;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class LogoutCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeTokenRepository _tokens = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordHasher _hasher = new();

    private LogoutCommandHandler CreateHandler() =>
        new(_tokens, _users, _clock, _unitOfWork, new FakeLocalizer());

    private async Task SeedUserWithRefreshToken(string tokenValue = "refresh-token-1")
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        user.IssueRefreshToken(tokenValue, _clock.UtcNow, _clock.UtcNow.AddDays(7));
        await _users.AddAsync(user);
        _tokens.Add(user.RefreshTokens.Single());
    }

    [Fact]
    public async Task Logout_Succeeds_AndRevokesToken()
    {
        await SeedUserWithRefreshToken();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LogoutCommand("refresh-token-1"));

        Assert.True(result.IsSuccess);
        Assert.True(_users.Users.Single().RefreshTokens.Single().IsRevoked);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Logout_WithUnknownToken_IsIdempotent()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LogoutCommand("unknown-token"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Logout_RevokedToken_IsIdempotent()
    {
        await SeedUserWithRefreshToken();
        var handler = CreateHandler();

        await handler.HandleAsync(new LogoutCommand("refresh-token-1"));

        var result = await handler.HandleAsync(new LogoutCommand("refresh-token-1"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Logout_WithEmptyToken_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LogoutCommand(""));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
