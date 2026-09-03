using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Identity.Application.Commands.Refresh;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class RefreshCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeTokenRepository _tokens = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeJwtProvider _jwt = new();
    private readonly PasswordHasher _hasher = new();

    private RefreshCommandHandler CreateHandler() =>
        new(_tokens, _users, _jwt, new FakeTokenGenerator(), _clock, _unitOfWork, new FakeLocalizer());

    private async Task SeedUserWithRefreshToken(string tokenValue = "refresh-token-1")
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        user.IssueRefreshToken(tokenValue, _clock.UtcNow, _clock.UtcNow.AddDays(7));
        await _users.AddAsync(user);
        _tokens.Add(user.RefreshTokens.Single());
    }

    [Fact]
    public async Task Refresh_Succeeds_AndRotatesToken()
    {
        await SeedUserWithRefreshToken();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RefreshCommand("refresh-token-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);

        var user = _users.Users.Single();
        Assert.True(user.RefreshTokens.Single(token => token.Matches("refresh-token-1")).IsRevoked);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RefreshCommand("unknown-token"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task Refresh_ReplayOfRotatedToken_ReturnsUnauthorized()
    {
        await SeedUserWithRefreshToken();
        var handler = CreateHandler();

        Assert.True((await handler.HandleAsync(new RefreshCommand("refresh-token-1"))).IsSuccess);

        var replay = await handler.HandleAsync(new RefreshCommand("refresh-token-1"));

        Assert.True(replay.IsFailure);
        Assert.Equal("Identity.InvalidToken", replay.Error.Code);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsUnauthorized()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        user.IssueRefreshToken("expired-token", _clock.UtcNow.AddDays(-8), _clock.UtcNow.AddDays(-1));
        await _users.AddAsync(user);
        _tokens.Add(user.RefreshTokens.Single());
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RefreshCommand("expired-token"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
    }

    [Fact]
    public async Task Refresh_WithEmptyToken_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RefreshCommand(""));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}


