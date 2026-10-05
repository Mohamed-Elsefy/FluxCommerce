using FluxCommerce.Modules.Identity.Application.Commands.UnlockUser;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Identity.Application.Commands;

public class UnlockUserCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeLocalizer _localizer = new();

    private UnlockUserCommandHandler CreateHandler() =>
        new(_users, _unitOfWork, _localizer);

    [Fact]
    public async Task HandleAsync_UnlocksLockedUser_AndPersists()
    {
        var user = TestUsers.CreateVerified();
        // Simulate 5 failed authentications to lock the account (5 attempts / 15min)
        var utcNow = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            user.Authenticate(false, utcNow.AddMinutes(i));
        }
        Assert.True(user.IsLockedAt(utcNow.AddMinutes(5)));

        await _users.AddAsync(user);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UnlockUserCommand(user.Id));

        Assert.True(result.IsSuccess);
        Assert.False(user.IsLockedAt(utcNow.AddMinutes(5)));
        Assert.Equal(0, user.Lockout.FailedAccessAttemptCount);
        Assert.Null(user.Lockout.LockoutEndUtc);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task HandleAsync_UnlocksAlreadyUnlockedUser_SucceedsIdempotently()
    {
        var user = TestUsers.CreateVerified();
        await _users.AddAsync(user);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UnlockUserCommand(user.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UnlockUserCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserNotFound", result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task HandleAsync_EmptyUserId_ReturnsValidationFailed()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UnlockUserCommand(Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task HandleAsync_Unlock_PersistsAndReturnsUserResponse()
    {
        var user = TestUsers.CreateVerified(firstName: "Alice", lastName: "Smith");
        await _users.AddAsync(user);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new UnlockUserCommand(user.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("Alice", result.Value.FirstName);
    }
}
