using FluxCommerce.Modules.Identity.Application.Commands.ResendVerification;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ResendVerificationCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakeEmailVerificationService _emails = new();
    private readonly PasswordHasher _hasher = new();

    private ResendVerificationCommandHandler CreateHandler() =>
        new(_users, new FakeTokenGenerator("new-verification-token"), _emails, _clock, _unitOfWork);

    private async Task SeedUnverifiedUser()
    {
        var user = TestUsers.CreateUnverified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
    }

    [Fact]
    public async Task Resend_Succeeds_AndSendsNewToken()
    {
        await SeedUnverifiedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResendVerificationCommand("user@example.com"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.EmailSent);
        Assert.Equal(_clock.UtcNow.AddHours(24), result.Value.ExpiresAtUtc);
        Assert.True(_users.Users.Single().VerificationToken!.Matches("new-verification-token"));
        var sent = Assert.Single(_emails.Sent);
        Assert.Equal("new-verification-token", sent.Token);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Resend_WithUnknownEmail_DoesNotSend()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResendVerificationCommand("missing@example.com"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EmailSent);
        Assert.Empty(_emails.Sent);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Resend_ForVerifiedUser_DoesNotSend()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResendVerificationCommand("user@example.com"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EmailSent);
        Assert.Empty(_emails.Sent);
    }

    [Fact]
    public async Task Resend_WithInvalidEmail_ReturnsValidation_WithoutSending()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResendVerificationCommand("not-an-email"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
        Assert.Empty(_emails.Sent);
    }

    [Fact]
    public async Task Resend_WithEmptyEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ResendVerificationCommand(""));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
