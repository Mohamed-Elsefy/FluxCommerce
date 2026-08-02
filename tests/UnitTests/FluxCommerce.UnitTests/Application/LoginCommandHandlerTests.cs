using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Identity.Application.Commands.Login;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class LoginCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeJwtProvider _jwt = new();
    private readonly PasswordHasher _hasher = new();

    private LoginCommandHandler CreateHandler() =>
        new(_users, _hasher, _jwt, new FakeTokenGenerator(), _clock, _unitOfWork);

    private async Task SeedVerifiedUser()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
    }

    [Fact]
    public async Task Login_Succeeds_IssuesAccessAndRefreshTokens()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LoginCommand("user@example.com", TestUsers.Password));

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        Assert.Equal("user@example.com", result.Value.Email);
        Assert.Equal("user@example.com", _jwt.LastEmail);
        Assert.Equal(Roles.Customer, Assert.Single(_jwt.LastRoles!));
        Assert.Equal(result.Value.UserId, _jwt.LastUserId);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized_AndRecordsAttempt()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LoginCommand("user@example.com", "Wrong!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidCredentials", result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        Assert.Equal(1, _users.Users.Single().Lockout.FailedAccessAttemptCount);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized_WithoutPersisting()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LoginCommand("missing@example.com", TestUsers.Password));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidCredentials", result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Login_WithInvalidEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LoginCommand("not-an-email", TestUsers.Password));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Login_WhenLocked_ReturnsForbidden()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        for (var i = 0; i < 5; i++)
        {
            await handler.HandleAsync(new LoginCommand("user@example.com", "Wrong!Pass1"));
        }

        var result = await handler.HandleAsync(new LoginCommand("user@example.com", TestUsers.Password));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.AccountLocked", result.Error.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public async Task Login_WithUnverifiedEmail_ReturnsForbidden()
    {
        var user = TestUsers.CreateUnverified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LoginCommand("user@example.com", TestUsers.Password));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.EmailNotVerified", result.Error.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public async Task Login_AfterLockoutWindow_Succeeds()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        for (var i = 0; i < 5; i++)
        {
            await handler.HandleAsync(new LoginCommand("user@example.com", "Wrong!Pass1"));
        }

        _clock.UtcNow = _clock.UtcNow.AddMinutes(16);

        var result = await handler.HandleAsync(new LoginCommand("user@example.com", TestUsers.Password));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _users.Users.Single().Lockout.FailedAccessAttemptCount);
    }

    [Fact]
    public async Task Login_ValidationFailure_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new LoginCommand(null!, null!));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}


