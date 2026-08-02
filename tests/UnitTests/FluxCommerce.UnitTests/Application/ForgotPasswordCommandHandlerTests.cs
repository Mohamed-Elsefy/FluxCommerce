using FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ForgotPasswordCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly FakePasswordResetService _resetEmails = new();
    private readonly PasswordHasher _hasher = new();

    private ForgotPasswordCommandHandler CreateHandler() =>
        new(_users, new FakeTokenGenerator("new-reset-token"), _resetEmails, _clock, _unitOfWork);

    private async Task SeedUser()
    {
        var user = TestUsers.CreateVerified(passwordHash: _hasher.Hash(TestUsers.Password));
        await _users.AddAsync(user);
    }

    [Fact]
    public async Task ForgotPassword_Succeeds_AndSendsResetToken()
    {
        await SeedUser();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ForgotPasswordCommand("user@example.com"));

        Assert.True(result.IsSuccess);
        Assert.True(_users.Users.Single().ResetToken!.Matches("new-reset-token"));
        var sent = Assert.Single(_resetEmails.Sent);
        Assert.Equal("new-reset-token", sent.Token);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task ForgotPassword_EmailSendFails_StillSucceedsSilently_AndTokenIsPersisted()
    {
        await SeedUser();
        _resetEmails.ExceptionToThrow = new InvalidOperationException("SMTP unavailable");
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ForgotPasswordCommand("user@example.com"));

        Assert.True(result.IsSuccess);
        Assert.True(_users.Users.Single().ResetToken!.Matches("new-reset-token"));
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
        Assert.Empty(_resetEmails.Sent);
    }

    [Fact]
    public async Task ForgotPassword_WithUnknownEmail_SucceedsSilently()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ForgotPasswordCommand("missing@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Empty(_resetEmails.Sent);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task ForgotPassword_WithInvalidEmail_ReturnsValidation_WithoutSending()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ForgotPasswordCommand("not-an-email"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
        Assert.Empty(_resetEmails.Sent);
    }

    [Fact]
    public async Task ForgotPassword_WithEmptyEmail_ReturnsValidation()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new ForgotPasswordCommand(""));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }
}
