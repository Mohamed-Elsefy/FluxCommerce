using FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;
using FluxCommerce.Modules.Identity.Domain.Entities;
using ContractUserStatus = FluxCommerce.Modules.Identity.Contracts.Enums.UserStatus;
using DomainUserStatus = FluxCommerce.Modules.Identity.Domain.Enums.UserStatus;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class VerifyEmailCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeTokenRepository _tokens = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordHasher _hasher = new();

    private VerifyEmailCommandHandler CreateHandler() =>
        new(_tokens, _users, _clock, _unitOfWork);

    private async Task SeedUnverifiedUser()
    {
        var user = TestUsers.CreateUnverified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
        _tokens.Add(user.VerificationToken!);
    }

    [Fact]
    public async Task VerifyEmail_Succeeds()
    {
        await SeedUnverifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new VerifyEmailCommand("verify-token"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEmailVerified);
        Assert.Equal(ContractUserStatus.Active, result.Value.Status);
        Assert.True(_users.Users.Single().IsEmailVerified);
        Assert.Equal(DomainUserStatus.Active, _users.Users.Single().Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task VerifyEmail_WithWrongToken_ReturnsValidation()
    {
        await SeedUnverifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new VerifyEmailCommand("wrong-token"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
        Assert.False(_users.Users.Single().IsEmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_WithUsedToken_ReturnsValidation()
    {
        await SeedUnverifiedUser();
        var handler = CreateHandler();
        Assert.True((await handler.HandleAsync(new VerifyEmailCommand("verify-token"))).IsSuccess);

        var replay = await handler.HandleAsync(new VerifyEmailCommand("verify-token"));

        Assert.True(replay.IsFailure);
        Assert.Equal("Identity.InvalidToken", replay.Error.Code);
    }

    [Fact]
    public async Task VerifyEmail_WithExpiredToken_ReturnsValidation()
    {
        var user = TestUsers.CreateUnverified(
            passwordHash: _hasher.Hash(TestUsers.Password),
            verificationTokenExpiresAtUtc: _clock.UtcNow.AddHours(1));
        await _users.AddAsync(user);
        _tokens.Add(user.VerificationToken!);
        var handler = CreateHandler();

        _clock.UtcNow = _clock.UtcNow.AddHours(2);

        var result = await handler.HandleAsync(new VerifyEmailCommand("verify-token"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
    }

    [Fact]
    public async Task VerifyEmail_WithUnknownToken_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new VerifyEmailCommand("unknown-token"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
    }

    [Fact]
    public async Task VerifyEmail_WithEmptyToken_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new VerifyEmailCommand(""));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
