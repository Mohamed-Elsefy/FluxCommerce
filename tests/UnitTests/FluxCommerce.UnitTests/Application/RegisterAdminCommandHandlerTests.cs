using FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Enums;
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
    private readonly TokenGenerator _tokenGenerator = new();
    private readonly FakeEmailVerificationService _emails = new();

    private RegisterAdminCommandHandler CreateHandler() =>
        new(_users, _hasher, _tokenGenerator, _emails, _clock, _unitOfWork);

    [Fact]
    public async Task Register_Succeeds_AndCreatesAdmin()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("admin@example.com", "Str0ng!Pass1", "Jane", "Admin"));

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_users.Users);
        Assert.Equal(Roles.Admin, Assert.Single(user.Roles));
        Assert.Equal("Jane", user.FirstName);
        Assert.Equal("Admin", user.LastName);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.True(_hasher.Verify("Str0ng!Pass1", user.PasswordHash));
        Assert.NotNull(user.VerificationToken);
        Assert.False(user.VerificationToken.IsUsed);
        Assert.True(user.VerificationToken.ExpiresAtUtc > _clock.UtcNow);
        var sent = Assert.Single(_emails.Sent);
        Assert.Equal(user.VerificationToken.Value, sent.Token);
        Assert.Equal(user.Id, sent.UserId);
    }

    [Fact]
    public async Task Register_EmailSendFails_ReturnsFailure_ButUserIsPersisted()
    {
        _emails.ExceptionToThrow = new InvalidOperationException("SMTP unavailable");
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("admin@example.com", "Str0ng!Pass1", "Jane", "Admin"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.EmailSendFailed", result.Error.Code);
        Assert.Single(_users.Users);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        await _users.AddAsync(TestUsers.CreateUnverified(passwordHash: _hasher.Hash(TestUsers.Password)));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("user@example.com", "Str0ng!Pass1", "Jane", "Admin"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.DuplicateEmail", result.Error.Code);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("admin@example.com", "weak", "Jane", "Admin"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("not-an-email", "Str0ng!Pass1", "Jane", "Admin"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task Register_MissingNames_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterAdminCommand("admin@example.com", "Str0ng!Pass1", "", null!));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
        Assert.Empty(_users.Users);
    }
}
