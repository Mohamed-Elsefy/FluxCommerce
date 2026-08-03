using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ChangePasswordCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordHasher _hasher = new();

    private ChangePasswordCommandHandler CreateHandler() =>
        new(_users, _hasher, _currentUser, _clock, _unitOfWork, new FakeLocalizer());

    private async Task SeedVerifiedUser()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
        _currentUser.UserId = user.Id;
        _currentUser.IsAuthenticated = true;
    }

    [Fact]
    public async Task ChangePassword_Succeeds_AndReplacesPassword()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new ChangePasswordCommand(TestUsers.Password, "New!Pass99", "New!Pass99"));

        Assert.True(result.IsSuccess);
        var user = _users.Users.Single();
        Assert.True(_hasher.Verify("New!Pass99", user.PasswordHash));
        Assert.False(_hasher.Verify(TestUsers.Password, user.PasswordHash));
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsValidation()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new ChangePasswordCommand("Wrong!Pass1", "New!Pass99", "New!Pass99"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.InvalidCredentials", result.Error.Code);
        Assert.True(_hasher.Verify(TestUsers.Password, _users.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task ChangePassword_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new ChangePasswordCommand(TestUsers.Password, "New!Pass99", "New!Pass99"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.Unauthenticated", result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task ChangePassword_WhenUserNotFound_ReturnsNotFound()
    {
        _currentUser.UserId = Guid.NewGuid();
        _currentUser.IsAuthenticated = true;
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new ChangePasswordCommand(TestUsers.Password, "New!Pass99", "New!Pass99"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task ChangePassword_WithMismatchedConfirmation_ReturnsValidation()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new ChangePasswordCommand(TestUsers.Password, "New!Pass99", "Different!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task ChangePassword_WithWeakNewPassword_ReturnsValidation()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new ChangePasswordCommand(TestUsers.Password, "weak", "weak"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}


