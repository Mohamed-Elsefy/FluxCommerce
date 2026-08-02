using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Identity.Application.Commands.GetCurrentUser;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class GetCurrentUserCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeCurrentUser _currentUser = new();
    private readonly PasswordHasher _hasher = new();

    private GetCurrentUserCommandHandler CreateHandler() =>
        new(_currentUser, _users);

    private async Task SeedVerifiedUser()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
        _currentUser.UserId = user.Id;
        _currentUser.IsAuthenticated = true;
    }

    [Fact]
    public async Task GetCurrentUser_Succeeds()
    {
        await SeedVerifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetCurrentUserCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal("user@example.com", result.Value.Email);
        Assert.Equal("John", result.Value.FirstName);
        Assert.Equal("Doe", result.Value.LastName);
        Assert.Equal(Roles.Customer, Assert.Single(result.Value.Roles));
        Assert.True(result.Value.IsEmailVerified);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetCurrentUserCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.Unauthenticated", result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUserNotFound_ReturnsNotFound()
    {
        _currentUser.UserId = Guid.NewGuid();
        _currentUser.IsAuthenticated = true;
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetCurrentUserCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}


