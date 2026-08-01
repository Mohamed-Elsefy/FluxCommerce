using FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class RegisterAdminCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordHasher _hasher = new();

    private RegisterAdminCommandHandler CreateHandler() =>
        new(_users, _hasher, _clock, _unitOfWork);

    [Fact]
    public async Task Register_Succeeds_AndCreatesAdmin()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("admin@example.com", "Str0ng!Pass1"));

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_users.Users);
        Assert.Equal(Roles.Admin, Assert.Single(user.Roles));
        Assert.True(_hasher.Verify("Str0ng!Pass1", user.PasswordHash));
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        await _users.AddAsync(TestUsers.CreateUnverified(passwordHash: _hasher.Hash(TestUsers.Password)));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("user@example.com", "Str0ng!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.DuplicateEmail", result.Error.Code);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("admin@example.com", "weak"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("not-an-email", "Str0ng!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
