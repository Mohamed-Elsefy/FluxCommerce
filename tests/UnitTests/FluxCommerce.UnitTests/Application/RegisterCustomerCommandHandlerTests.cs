using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class RegisterCustomerCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordHasher _hasher = new();
    private readonly TokenGenerator _tokenGenerator = new();
    private readonly FakeEmailVerificationService _emails = new();

    private RegisterCustomerCommandHandler CreateHandler() =>
        new(_users, _hasher, _tokenGenerator, _emails, _clock, _unitOfWork);

    [Fact]
    public async Task Register_Succeeds_AndCreatesCustomer()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterCustomerCommand("user@example.com", "Str0ng!Pass1"));

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_users.Users);
        Assert.Equal(Roles.Customer, Assert.Single(user.Roles));
        Assert.Equal("user@example.com", result.Value.Email);
        Assert.False(result.Value.IsEmailVerified);
        Assert.NotEqual("Str0ng!Pass1", user.PasswordHash);
        Assert.True(_hasher.Verify("Str0ng!Pass1", user.PasswordHash));
        Assert.NotNull(user.VerificationToken);
        Assert.False(user.VerificationToken.IsUsed);
        Assert.True(user.VerificationToken.ExpiresAtUtc > _clock.UtcNow);
        var sent = Assert.Single(_emails.Sent);
        Assert.Equal(user.VerificationToken.Value, sent.Token);
        Assert.Equal(user.Id, sent.UserId);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Register_EmailSendFails_ReturnsFailure_ButUserIsPersisted()
    {
        _emails.ExceptionToThrow = new InvalidOperationException("SMTP unavailable");
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterCustomerCommand("user@example.com", "Str0ng!Pass1"));

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

        var result = await handler.HandleAsync(new RegisterCustomerCommand("user@example.com", "Str0ng!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.DuplicateEmail", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Register_DuplicateEmail_IsCaseInsensitive()
    {
        await _users.AddAsync(TestUsers.CreateUnverified(email: "user@example.com", passwordHash: _hasher.Hash(TestUsers.Password)));
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterCustomerCommand("USER@EXAMPLE.COM", "Str0ng!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.DuplicateEmail", result.Error.Code);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterCustomerCommand("user@example.com", "weak"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterCustomerCommand("not-an-email", "Str0ng!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task Register_NullEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new RegisterCustomerCommand(null!, "Str0ng!Pass1"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
