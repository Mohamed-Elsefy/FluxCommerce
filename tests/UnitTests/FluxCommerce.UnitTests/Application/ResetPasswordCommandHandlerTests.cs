using FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;
using FluxCommerce.Modules.Identity.Domain.Entities;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ResetPasswordCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeTokenRepository _tokens = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordHasher _hasher = new();

    private ResetPasswordCommandHandler CreateHandler() =>
        new(_tokens, _users, _hasher, _clock, _unitOfWork, new FakeLocalizer());

    private async Task SeedUserWithResetToken()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        user.RequestPasswordReset("reset-token", _clock.UtcNow, _clock.UtcNow.AddHours(1));
        await _users.AddAsync(user);
        _tokens.Add(user.ResetToken!);
    }

    [Fact]
    public async Task ResetPassword_Succeeds_AndReplacesPassword()
    {
        await SeedUserWithResetToken();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResetPasswordCommand("reset-token", "New!Pass99"));

        Assert.True(result.IsSuccess);
        var user = _users.Users.Single();
        Assert.True(_hasher.Verify("New!Pass99", user.PasswordHash));
        Assert.False(_hasher.Verify(TestUsers.Password, user.PasswordHash));
        Assert.True(user.ResetToken!.IsUsed);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task ResetPassword_WithUnknownToken_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResetPasswordCommand("unknown-token", "New!Pass99"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
    }

    [Fact]
    public async Task ResetPassword_WithUsedToken_ReturnsValidation()
    {
        await SeedUserWithResetToken();
        var handler = CreateHandler();
        Assert.True((await handler.HandleAsync(new ResetPasswordCommand("reset-token", "New!Pass99"))).IsSuccess);

        var replay = await handler.HandleAsync(new ResetPasswordCommand("reset-token", "Other!Pass99"));

        Assert.True(replay.IsFailure);
        Assert.Equal("Identity.InvalidToken", replay.Error.Code);
    }

    [Fact]
    public async Task ResetPassword_WithExpiredToken_ReturnsValidation()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        user.RequestPasswordReset("old-token", _clock.UtcNow.AddHours(-2), _clock.UtcNow.AddHours(-1));
        await _users.AddAsync(user);
        _tokens.Add(user.ResetToken!);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResetPasswordCommand("old-token", "New!Pass99"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidToken", result.Error.Code);
        Assert.True(_hasher.Verify(TestUsers.Password, _users.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithWeakPassword_ReturnsValidation()
    {
        await SeedUserWithResetToken();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResetPasswordCommand("reset-token", "weak"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
